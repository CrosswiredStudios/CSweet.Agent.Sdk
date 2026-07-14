using System.Collections.Concurrent;
using System.Threading.Channels;
using CSweet.Agent.Contracts.Grpc;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace CSweet.Agent.SDK;

public sealed class GrpcAgentBrokerClient : IAgentBrokerClient
{
    private readonly AgentBroker.AgentBrokerClient _client;
    private readonly ILogger<GrpcAgentBrokerClient> _logger;
    private readonly Channel<AgentToBrokerMessage> _outbound;
    private readonly Channel<BrokerToAgentMessage> _inbound;
    private readonly ConcurrentDictionary<string, Channel<CapabilityResult>> _pendingCapabilities = new();
    private readonly TaskCompletionSource<RegistrationResult> _registration =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private CancellationTokenSource? _lifetimeCts;
    private AsyncDuplexStreamingCall<AgentToBrokerMessage, BrokerToAgentMessage>? _call;
    private Task? _sendTask;
    private Task? _receiveTask;
    private int _started;

    public GrpcAgentBrokerClient(
        AgentBroker.AgentBrokerClient client,
        ILogger<GrpcAgentBrokerClient> logger)
    {
        _client = client;
        _logger = logger;
        _outbound = Channel.CreateBounded<AgentToBrokerMessage>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
        _inbound = Channel.CreateBounded<BrokerToAgentMessage>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true
        });
    }

    public async Task StartAsync(RegisterAgent registration, CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("The broker client has already been started.");
        }

        _lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _call = _client.Connect(cancellationToken: _lifetimeCts.Token);
        _sendTask = SendLoopAsync(_call, _lifetimeCts.Token);
        _receiveTask = ReceiveLoopAsync(_call, _lifetimeCts.Token);

        await QueueAsync(new AgentToBrokerMessage
        {
            MessageId = NewMessageId(),
            Register = registration
        }, cancellationToken);

        var result = await _registration.Task.WaitAsync(cancellationToken);
        if (!result.Accepted)
        {
            throw new InvalidOperationException(
                $"The broker rejected agent registration: {result.RejectionReason}");
        }

        _logger.LogInformation(
            "Agent registered with broker session {SessionId}.",
            result.SessionId);
    }

    public IAsyncEnumerable<BrokerToAgentMessage> ReadAllAsync(CancellationToken cancellationToken) =>
        _inbound.Reader.ReadAllAsync(cancellationToken);

    public Task PublishEventAsync(
        PublishEvent message,
        string? correlationId = null,
        CancellationToken cancellationToken = default) =>
        QueueAsync(new AgentToBrokerMessage
        {
            MessageId = NewMessageId(),
            CorrelationId = correlationId ?? string.Empty,
            PublishEvent = message
        }, cancellationToken);

    public async Task<CapabilityResult> InvokeCapabilityAsync(
        RequestCapability request,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RequestId))
        {
            request.RequestId = Guid.NewGuid().ToString("N");
        }

        CapabilityResult? terminal = null;
        await foreach (var result in InvokeStreamingCapabilityAsync(
            request,
            correlationId,
            cancellationToken))
        {
            terminal = result;
        }

        return terminal ?? throw new InvalidOperationException(
            $"Capability request '{request.RequestId}' completed without a result.");
    }

    public async IAsyncEnumerable<CapabilityResult> InvokeStreamingCapabilityAsync(
        RequestCapability request,
        string? correlationId = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RequestId))
        {
            request.RequestId = Guid.NewGuid().ToString("N");
        }

        var results = Channel.CreateBounded<CapabilityResult>(new BoundedChannelOptions(128)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true
        });

        if (!_pendingCapabilities.TryAdd(request.RequestId, results))
        {
            throw new InvalidOperationException(
                $"Capability request '{request.RequestId}' is already pending.");
        }

        try
        {
            await QueueAsync(new AgentToBrokerMessage
            {
                MessageId = NewMessageId(),
                CorrelationId = correlationId ?? request.RequestId,
                CapabilityRequest = request
            }, cancellationToken);

            await foreach (var result in results.Reader.ReadAllAsync(cancellationToken))
            {
                yield return result;
            }
        }
        finally
        {
            _pendingCapabilities.TryRemove(request.RequestId, out _);
            results.Writer.TryComplete();
        }
    }

    public Task SendCapabilityResultAsync(
        CapabilityResult result,
        string? correlationId = null,
        CancellationToken cancellationToken = default) =>
        QueueAsync(new AgentToBrokerMessage
        {
            MessageId = NewMessageId(),
            CorrelationId = correlationId ?? result.RequestId,
            CapabilityResult = result
        }, cancellationToken);

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_lifetimeCts is null)
        {
            return;
        }

        _outbound.Writer.TryComplete();

        if (_sendTask is not null)
        {
            await AwaitShutdownTaskAsync(_sendTask, cancellationToken);
        }

        await _lifetimeCts.CancelAsync();

        if (_receiveTask is not null)
        {
            await AwaitShutdownTaskAsync(_receiveTask, cancellationToken);
        }

        _call?.Dispose();
        _lifetimeCts.Dispose();
        _lifetimeCts = null;
    }

    public async ValueTask DisposeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await StopAsync(timeout.Token);
    }

    private async Task QueueAsync(
        AgentToBrokerMessage message,
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _started) == 0)
        {
            throw new InvalidOperationException("The broker client has not been started.");
        }

        await _outbound.Writer.WriteAsync(message, cancellationToken);
    }

    private async Task SendLoopAsync(
        AsyncDuplexStreamingCall<AgentToBrokerMessage, BrokerToAgentMessage> call,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var message in _outbound.Reader.ReadAllAsync(cancellationToken))
            {
                await call.RequestStream.WriteAsync(message);
            }

            await call.RequestStream.CompleteAsync();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Agent broker send loop failed.");
            _registration.TrySetException(exception);
            _inbound.Writer.TryComplete(exception);
        }
    }

    private async Task ReceiveLoopAsync(
        AsyncDuplexStreamingCall<AgentToBrokerMessage, BrokerToAgentMessage> call,
        CancellationToken cancellationToken)
    {
        try
        {
            while (await call.ResponseStream.MoveNext(cancellationToken))
            {
                var message = call.ResponseStream.Current;

                if (message.PayloadCase == BrokerToAgentMessage.PayloadOneofCase.Registration)
                {
                    _registration.TrySetResult(message.Registration);
                    continue;
                }

                if (message.PayloadCase == BrokerToAgentMessage.PayloadOneofCase.CapabilityResult &&
                    _pendingCapabilities.TryGetValue(
                        message.CapabilityResult.RequestId,
                        out var results))
                {
                    await results.Writer.WriteAsync(message.CapabilityResult, cancellationToken);
                    if (!message.CapabilityResult.HasMore)
                    {
                        results.Writer.TryComplete();
                    }
                    continue;
                }

                await _inbound.Writer.WriteAsync(message, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Agent broker receive loop failed.");
            _registration.TrySetException(exception);

            foreach (var results in _pendingCapabilities.Values)
            {
                results.Writer.TryComplete(exception);
            }

            _inbound.Writer.TryComplete(exception);
        }
        finally
        {
            _inbound.Writer.TryComplete();
        }
    }

    private static async Task AwaitShutdownTaskAsync(
        Task task,
        CancellationToken cancellationToken)
    {
        try
        {
            await task.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static string NewMessageId() => Guid.NewGuid().ToString("N");
}
