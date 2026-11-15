namespace CSweet.Agent.SDK.Tests;
public sealed class IndependentWorkTests
{
    [Fact] public async Task FailuresRetainTheirCauseWhileEveryIndependentContinuationRuns()
    {
        var progressed = false;
        var error = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => IndependentWork.RunAsync([
            _ => throw new UnauthorizedAccessException("Affected contribution denied"),
            _ => { progressed = true; return Task.CompletedTask; }], default));
        Assert.True(progressed); Assert.Contains("Affected contribution denied", error.Message);
    }
}
