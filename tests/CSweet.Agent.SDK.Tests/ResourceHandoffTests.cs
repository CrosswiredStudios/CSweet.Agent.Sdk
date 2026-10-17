using System.Text.Json;

namespace CSweet.Agent.SDK.Tests;

public sealed class ResourceHandoffTests
{
    [Fact]
    public void Typed_handoff_preserves_checkpoint_and_operation_inputs_without_claiming_success()
    {
        var receipt = new WorkspaceCheckpointReceipt(Guid.NewGuid(), 7, new('a', 64));
        var request = new WorkloadResourceRequest(8192, receipt, "build", new('b', 64), "project-estimate");
        var result = AgentWorkResult.ResourceHandoff(request);
        Assert.False(result.Succeeded); Assert.True(result.Retryable);
        Assert.Equal("runtime.resources.handoff", result.FailureCode);
        Assert.Equal(request, result.Value!.Value.Deserialize<WorkloadResourceRequest>(new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }
}
