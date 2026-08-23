using System.Text;

namespace CSweet.Agent.SDK;

/// <summary>Writes ordered, durable reasoning, activity, draft, and final events for an interactive chat turn.</summary>
public sealed class AgentTurnStreamWriter : IAsyncDisposable
{
    private const int MinimumFlushCharacters = 64;
    private const int MaximumFragmentCharacters = 4_096;
    private readonly IAgentProgressReporter _progress;
    private readonly string _conversationId;
    private readonly Guid _turnId;
    private readonly int _attempt;
    private readonly string _sensitivity;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly StringBuilder _reasoning = new();
    private readonly StringBuilder _draft = new();
    private int _sequence;
    private bool _terminal;

    internal AgentTurnStreamWriter(
        IAgentProgressReporter progress,
        string conversationId,
        Guid turnId,
        int attempt,
        string sensitivity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        ArgumentOutOfRangeException.ThrowIfNegative(attempt);
        _progress = progress;
        _conversationId = conversationId;
        _turnId = turnId;
        _attempt = attempt;
        _sensitivity = sensitivity switch
        {
            "Public" => "Public",
            "Internal" => "Internal",
            "Confidential" => "Confidential",
            "Restricted" => "Restricted",
            _ => throw new ArgumentOutOfRangeException(nameof(sensitivity), "Sensitivity must be Public, Internal, Confidential, or Restricted.")
        };
    }

    public Task WriteReasoningAsync(string delta, CancellationToken cancellationToken = default) =>
        BufferAsync(_reasoning, delta, AgentTurnStreamKinds.ReasoningDelta, cancellationToken);

    public async Task CompleteReasoningAsync(CancellationToken cancellationToken = default)
    {
        await FlushBufferAsync(_reasoning, AgentTurnStreamKinds.ReasoningDelta, cancellationToken);
        await WriteAsync(string.Empty, AgentTurnStreamKinds.ReasoningCompleted, false, null, null, cancellationToken);
    }

    public Task WriteDraftAsync(string delta, CancellationToken cancellationToken = default) =>
        BufferAsync(_draft, delta, AgentTurnStreamKinds.DraftDelta, cancellationToken);

    public async Task ResetDraftAsync(string reason, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfTerminal();
            _draft.Clear();
            await WriteCoreAsync(reason, AgentTurnStreamKinds.DraftReset, false, null, null, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task ActivityStartedAsync(
        string title,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default) =>
        WriteAsync(title, AgentTurnStreamKinds.ActivityStarted, false, null, metadata, cancellationToken);

    public Task ActivityCompletedAsync(
        string title,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default) =>
        WriteAsync(title, AgentTurnStreamKinds.ActivityCompleted, false, null, metadata, cancellationToken);

    public Task ActivityFailedAsync(
        string title,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default) =>
        WriteAsync(title, AgentTurnStreamKinds.ActivityFailed, false, null, metadata, cancellationToken);

    /// <summary>Starts a timed activity whose completion or failure is emitted by the returned scope.</summary>
    public async Task<AgentTurnActivityScope> StartActivityAsync(
        string title,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        await ActivityStartedAsync(title, metadata, cancellationToken);
        return new AgentTurnActivityScope(this, title, metadata);
    }

    public async Task CommitAsync(string finalText, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(finalText);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfTerminal();
            await FlushBufferCoreAsync(_reasoning, AgentTurnStreamKinds.ReasoningDelta, cancellationToken);
            await FlushBufferCoreAsync(_draft, AgentTurnStreamKinds.DraftDelta, cancellationToken);
            await WriteCoreAsync(finalText, AgentTurnStreamKinds.FinalCommit, true, null, null, cancellationToken);
            _terminal = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task FailAsync(string safeMessage, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(safeMessage);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfTerminal();
            await FlushBufferCoreAsync(_reasoning, AgentTurnStreamKinds.ReasoningDelta, cancellationToken);
            await FlushBufferCoreAsync(_draft, AgentTurnStreamKinds.DraftDelta, cancellationToken);
            await WriteCoreAsync(safeMessage, AgentTurnStreamKinds.TurnFailed, true, "agent_error", null, cancellationToken);
            _terminal = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await FlushBufferCoreAsync(_reasoning, AgentTurnStreamKinds.ReasoningDelta, cancellationToken);
            await FlushBufferCoreAsync(_draft, AgentTurnStreamKinds.DraftDelta, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_terminal)
            await FlushAsync();
        _gate.Dispose();
    }

    private async Task BufferAsync(
        StringBuilder buffer,
        string delta,
        string kind,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(delta)) return;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfTerminal();
            buffer.Append(delta);
            if (buffer.Length >= MinimumFlushCharacters)
                await FlushBufferCoreAsync(buffer, kind, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task FlushBufferAsync(
        StringBuilder buffer,
        string kind,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await FlushBufferCoreAsync(buffer, kind, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task FlushBufferCoreAsync(
        StringBuilder buffer,
        string kind,
        CancellationToken cancellationToken)
    {
        if (buffer.Length == 0) return;
        var value = buffer.ToString();
        buffer.Clear();
        for (var offset = 0; offset < value.Length; offset += MaximumFragmentCharacters)
        {
            var length = Math.Min(MaximumFragmentCharacters, value.Length - offset);
            await WriteCoreAsync(value.Substring(offset, length), kind, false, null, null, cancellationToken);
        }
    }

    private async Task WriteAsync(
        string delta,
        string kind,
        bool isFinal,
        string? error,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfTerminal();
            await WriteCoreAsync(delta, kind, isFinal, error, metadata, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private Task WriteCoreAsync(
        string delta,
        string kind,
        bool isFinal,
        string? error,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken) =>
        _progress.ReportAsync(new AgentTurnStreamEvent(
            _conversationId,
            _sequence++,
            delta,
            isFinal,
            error,
            _turnId,
            kind,
            metadata,
            _attempt,
            DateTimeOffset.UtcNow,
            _sensitivity), cancellationToken);

    private void ThrowIfTerminal()
    {
        if (_terminal)
            throw new InvalidOperationException("The turn stream is already complete.");
    }
}

/// <summary>A timed activity in an interactive turn stream.</summary>
public sealed class AgentTurnActivityScope : IAsyncDisposable
{
    private readonly AgentTurnStreamWriter _writer;
    private readonly string _title;
    private readonly IReadOnlyDictionary<string, string>? _startedMetadata;
    private readonly System.Diagnostics.Stopwatch _stopwatch = System.Diagnostics.Stopwatch.StartNew();
    private bool _terminal;

    internal AgentTurnActivityScope(
        AgentTurnStreamWriter writer,
        string title,
        IReadOnlyDictionary<string, string>? startedMetadata)
    {
        _writer = writer;
        _title = title;
        _startedMetadata = startedMetadata;
    }

    public async Task CompleteAsync(
        string? title = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfTerminal();
        await _writer.ActivityCompletedAsync(
            title ?? _title,
            WithDuration(metadata),
            cancellationToken);
        _terminal = true;
    }

    public async Task FailAsync(
        string? title = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfTerminal();
        await _writer.ActivityFailedAsync(
            title ?? _title,
            WithDuration(metadata),
            cancellationToken);
        _terminal = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_terminal)
            await CompleteAsync();
    }

    private IReadOnlyDictionary<string, string> WithDuration(
        IReadOnlyDictionary<string, string>? metadata)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (_startedMetadata is not null)
            foreach (var item in _startedMetadata) result[item.Key] = item.Value;
        if (metadata is not null)
            foreach (var item in metadata) result[item.Key] = item.Value;
        result["durationMs"] = _stopwatch.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return result;
    }

    private void ThrowIfTerminal()
    {
        if (_terminal)
            throw new InvalidOperationException("The turn activity is already complete.");
    }
}

public sealed record AgentTurnStreamEvent(
    string ConversationId,
    int Sequence,
    string Delta,
    bool IsFinal,
    string? Error,
    Guid TurnId,
    string Kind,
    IReadOnlyDictionary<string, string>? Metadata,
    int Attempt,
    DateTimeOffset OccurredAt,
    string Sensitivity);

public static class AgentTurnStreamKinds
{
    public const string ReasoningDelta = "reasoning.delta";
    public const string ReasoningCompleted = "reasoning.completed";
    public const string ActivityStarted = "activity.started";
    public const string ActivityCompleted = "activity.completed";
    public const string ActivityFailed = "activity.failed";
    public const string DraftDelta = "draft.delta";
    public const string DraftReset = "draft.reset";
    public const string FinalCommit = "final.commit";
    public const string TurnFailed = "turn.failed";
}
