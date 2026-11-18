using System.Text.Json;

namespace CSweet.Agent.SDK.Tests;

public sealed class ContributionAdmissionTests
{
    [Fact]
    public void OnlyTheRecordedAuthorityCanReleaseTheRecoveredStory()
    {
        var authority = Guid.NewGuid();
        var basis = new ContributionAdmission(Guid.NewGuid(), "renderer", Guid.NewGuid(), "result", Guid.NewGuid(),
            2, authority, "Hold pending repair", ["Observe the scene"]);
        var read = ContributionAdmission.Read(JsonSerializer.SerializeToElement(basis, JsonSerializerOptions.Web))!;
        Assert.Equal(basis.PriorDecisionId, read.PriorDecisionId);
        Assert.True(read.Allows("Decided", ContributionAdmission.Admit, authority, "Validated result covers the request."));
        Assert.False(read.Allows("Pending", ContributionAdmission.Admit, authority, "Recommended"));
        Assert.False(read.Allows("Decided", ContributionAdmission.Admit, Guid.NewGuid(), "Approved elsewhere"));
        Assert.False(read.Allows("Decided", ContributionAdmission.Hold, authority, "Need more evidence"));
        Assert.Null(ContributionAdmission.Read(JsonSerializer.SerializeToElement(new { contract = "requirement-resolution.v1" })));
    }
}
