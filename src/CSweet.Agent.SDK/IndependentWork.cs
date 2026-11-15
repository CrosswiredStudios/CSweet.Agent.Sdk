namespace CSweet.Agent.SDK;

/// <summary>Run independent owned continuations despite a sibling failure, preserving every failure for the caller.</summary>
public static class IndependentWork
{
    public static async Task RunAsync(IEnumerable<Func<CancellationToken, Task>> actions, CancellationToken ct)
    {
        var failures = new List<Exception>();
        foreach (var action in actions)
        {
            ct.ThrowIfCancellationRequested();
            try { await action(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception e) { failures.Add(e); }
        }
        if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Owned work needs attention: " + string.Join("; ", failures.Select(e => e.Message)), failures);
    }
}
