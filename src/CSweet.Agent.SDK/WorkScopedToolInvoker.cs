using System.Runtime.CompilerServices;
using System.Text.Json;

namespace CSweet.Agent.SDK;

// A session client created during activation must still honor an inherited closed work flow.
internal sealed class FlowScopedToolInvoker(IPlatformToolInvoker inner) : IPlatformToolInvoker
{
    private IPlatformToolInvoker Current => InferenceExecutionScope.Current is { } owner
        ? new WorkScopedToolInvoker(inner, owner) : inner;
    public Task<JsonElement> InvokeAsync(string capability, JsonElement arguments, CancellationToken cancellationToken = default) =>
        Current.InvokeAsync(capability, arguments, cancellationToken);
    public IAsyncEnumerable<JsonElement> InvokeStreamingAsync(string capability, JsonElement arguments, CancellationToken cancellationToken = default) =>
        Current.InvokeStreamingAsync(capability, arguments, cancellationToken);
    public Task<IReadOnlyList<AgentToolDescriptor>> ListToolsAsync(CancellationToken cancellationToken = default) =>
        Current.ListToolsAsync(cancellationToken);
}

internal sealed class WorkScopedProgressReporter(IAgentProgressReporter inner, InferenceExecutionScope owner) : IAgentProgressReporter
{
    public async Task ReportAsync(object? value, CancellationToken cancellationToken = default)
    {
        const string capability = "platform.work.progress";
        owner.RequireActive(capability);
        using var call = CancellationTokenSource.CreateLinkedTokenSource(owner.Token, cancellationToken);
        await inner.ReportAsync(value, call.Token);
        owner.RequireActive(capability);
        call.Token.ThrowIfCancellationRequested();
    }
}

// Binding is private SDK state, not agent-selected authority or a disposal acknowledgement.
internal sealed class WorkScopedToolInvoker(IPlatformToolInvoker inner, InferenceExecutionScope owner) : IPlatformToolInvoker
{
    public async Task<JsonElement> InvokeAsync(string capability, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        owner.RequireActive(capability);
        using var call = CancellationTokenSource.CreateLinkedTokenSource(owner.Token, cancellationToken);
        var result = await inner.InvokeAsync(capability, arguments, call.Token);
        owner.RequireActive(capability);
        call.Token.ThrowIfCancellationRequested();
        return result;
    }

    public async IAsyncEnumerable<JsonElement> InvokeStreamingAsync(string capability, JsonElement arguments,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        owner.RequireActive(capability);
        using var call = CancellationTokenSource.CreateLinkedTokenSource(owner.Token, cancellationToken);
        await foreach (var result in inner.InvokeStreamingAsync(capability, arguments, call.Token))
        {
            owner.RequireActive(capability);
            call.Token.ThrowIfCancellationRequested();
            yield return result;
        }
        owner.RequireActive(capability);
        call.Token.ThrowIfCancellationRequested();
    }

    public async Task<IReadOnlyList<AgentToolDescriptor>> ListToolsAsync(CancellationToken cancellationToken = default)
    {
        const string capability = "platform.tools.list";
        owner.RequireActive(capability);
        using var call = CancellationTokenSource.CreateLinkedTokenSource(owner.Token, cancellationToken);
        var result = await inner.ListToolsAsync(call.Token);
        owner.RequireActive(capability);
        call.Token.ThrowIfCancellationRequested();
        return result;
    }
}
