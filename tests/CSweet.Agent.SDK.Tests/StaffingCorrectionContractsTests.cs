using System.Text.Json;

namespace CSweet.Agent.SDK.Tests;

public sealed class StaffingCorrectionContractsTests
{
    [Fact]
    public async Task DiscoveryResolvesAuthorizedSourcesOutsideTheRecentPageWithoutChangingFilteredReads()
    {
        var sourceId = Guid.NewGuid(); var replacement = Guid.NewGuid();
        var correction = new StaffingCorrectionResponse(Guid.NewGuid(), sourceId, Guid.NewGuid(), Guid.NewGuid(), null, null,
            "planning", "Correct the capability", "Pending", replacement, 2, DateTimeOffset.UtcNow);
        var source = JsonSerializer.Deserialize<ResourceChangeRequestResponse>("{}")! with { Id = sourceId, Status = "Approved" };
        var draft = source with { Id = replacement, Status = "RevisionRequested" };
        var reads = new List<Guid?>();
        var runtime = new AgentTestRuntime().RegisterCapability<ResourceChangeReadRequest, ResourceChangeReadResponse>(PlatformCapabilities.ResourceChangeRead,
            (request, _) => { reads.Add(request.RequestId); return Task.FromResult(request.RequestId is { } id
                ? new ResourceChangeReadResponse(id == sourceId ? [source] : [draft])
                : new ResourceChangeReadResponse([]) { Corrections = [correction] }); });
        var current = await runtime.CreateContext().Platform.ReadResourceChangeContextAsync();
        Assert.Equal(new Guid?[] { null, sourceId, replacement }, reads);
        Assert.Contains(current.Requests, x => x.Id == sourceId && x.Status == "Approved");
        Assert.Contains(current.Requests, x => x.Id == replacement && x.Status == "RevisionRequested");
        reads.Clear();
        await runtime.CreateContext().Platform.ReadResourceChangesAsync(new(Statuses: ["Pending"]));
        Assert.Single(reads); Assert.Null(reads[0]);
    }
    [Fact]
    public void ResourceReadDiscoversCorrectionAndProposalBindsItWithoutNewAuthority()
    {
        var correction = new StaffingCorrectionResponse(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), "technical-planning", "The selected role does not match its work.", "Pending", null, 1, DateTimeOffset.UtcNow);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var read = new ResourceChangeReadResponse([]) { Corrections = [correction] };
        Assert.Equal(correction, Assert.Single(JsonSerializer.Deserialize<ResourceChangeReadResponse>(JsonSerializer.Serialize(read, options), options)!.Corrections));
        var proposal = JsonSerializer.Deserialize<ResourceChangeProposalRequest>("{}", options)! with { CorrectionId = correction.Id };
        Assert.Equal(correction.Id, JsonSerializer.SerializeToElement(proposal, options).GetProperty("correctionId").GetGuid());
        Assert.Empty(JsonSerializer.Deserialize<ResourceChangeReadResponse>("{\"requests\":[]}", options)!.Corrections);
    }
}
