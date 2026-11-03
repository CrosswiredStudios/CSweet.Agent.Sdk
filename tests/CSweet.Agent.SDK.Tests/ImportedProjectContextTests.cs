using System.Text.Json;
using Xunit;

namespace CSweet.Agent.SDK.Tests;

public sealed class ImportedProjectContextTests
{
    [Fact]
    public void LegacySnapshotsAndHiringRequestsRetainTheirNonImportDefaults()
    {
        var project = JsonSerializer.Deserialize<WorkstreamSummary>("{}")!;
        Assert.Null(project.ImportedProject);
        var hire = JsonSerializer.Deserialize<UpsertHiringRecommendationRequest>("{}")!;
        Assert.False(hire.AssignAsProjectManager);
        var imported = project with { ImportedProject = new(Guid.NewGuid(), 9, "Review", null, null, null) };
        var restored = JsonSerializer.Deserialize<WorkstreamSummary>(JsonSerializer.Serialize(imported))!;
        Assert.Equal(imported.ImportedProject, restored.ImportedProject);
        Assert.Null(restored.ImportedProject!.AcceptedProfile);
        Assert.Null(restored.AccountableManagerOrganizationUserId);
    }
}
