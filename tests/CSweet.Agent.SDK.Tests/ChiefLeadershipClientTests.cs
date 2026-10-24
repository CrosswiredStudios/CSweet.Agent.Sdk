namespace CSweet.Agent.SDK.Tests;

public sealed class ChiefLeadershipClientTests
{
    [Fact]
    public async Task TypedLeadershipClientPreservesImmutableSourcesRevisionsAndOperationKeys()
    {
        var turn = Guid.NewGuid(); var decision = Guid.NewGuid(); var row = Guid.NewGuid();
        var state = new ChiefLeadershipSnapshot(7, null, [], [], false, true, Guid.NewGuid(), null, "inputs");
        var present = new PresentChiefFocusRequest("general", 1, Guid.NewGuid(), Guid.NewGuid(),
            [new("product", "Product", "Product responsibility", 1), new("legal", "Legal", "Legal responsibility", 2)], 7, "present", turn, "product");
        var capture = new CaptureChiefFocusRequest(turn, 7, "capture", decision);
        var control = new ControlChiefLeadershipRequest(turn, true, 7, "control");
        var assessment = new ChiefCoverageAssessment(row, "general", 1, "product", "Product", "Product responsibility", 1,
            "NeedsCoverage", "Blocked", 5, null, null, null, null, null, null, null, null, "evidence_missing", "inputs");
        var record = new RecordChiefAssessmentRequest(row, 4, 7, "inputs",
            new("NeedsCoverage", "Unresolved", null, "No supported ownership evidence.", "Blocked", []), "assessment");
        var read = new ReadChiefLeadershipRequest(row, 20, turn);
        var calls = 0;
        var runtime = new AgentTestRuntime()
            .RegisterCapability<ReadChiefLeadershipRequest, ChiefLeadershipSnapshot>(ChiefLeadershipCapabilities.Read, (request, _) =>
            { Assert.Equal(read, request); calls++; return Task.FromResult(state); })
            .RegisterCapability<PresentChiefFocusRequest, PresentedChiefFocus>(ChiefLeadershipCapabilities.Present, (request, _) =>
            { Assert.Equal(present.IdempotencyKey, request.IdempotencyKey); Assert.Equal(present.Domains, request.Domains);
                Assert.Equal(turn, request.OwnerTurnId); Assert.Equal("product", request.ExplicitDomainId); calls++; return Task.FromResult(new PresentedChiefFocus(decision, present.MessageId, 7)); })
            .RegisterCapability<CaptureChiefFocusRequest, ChiefLeadershipSnapshot>(ChiefLeadershipCapabilities.Capture, (request, _) =>
            { Assert.Equal(capture, request); calls++; return Task.FromResult(state); })
            .RegisterCapability<RecordChiefAssessmentRequest, ChiefCoverageAssessment>(ChiefLeadershipCapabilities.Assess, (request, _) =>
            { Assert.Equal(record.AssessmentId, request.AssessmentId); Assert.Equal(record.ExpectedRevision, request.ExpectedRevision);
                Assert.Equal(record.InputFingerprint, request.InputFingerprint); Assert.Equal(record.IdempotencyKey, request.IdempotencyKey); calls++; return Task.FromResult(assessment); })
            .RegisterCapability<ControlChiefLeadershipRequest, ChiefLeadershipSnapshot>(ChiefLeadershipCapabilities.Control, (request, _) =>
            { Assert.Equal(control, request); calls++; return Task.FromResult(state); });
        var client = runtime.CreateContext().Platform.ChiefLeadership;
        Assert.Equal(state.FocusRevision, (await client.ReadAsync(read)).FocusRevision);
        Assert.Equal(decision, (await client.PresentAsync(present)).DecisionId);
        Assert.Equal(state.ResponsibleInstallationId, (await client.CaptureAsync(capture)).ResponsibleInstallationId);
        Assert.Equal(assessment, await client.AssessAsync(record));
        Assert.True((await client.ControlAsync(control)).Paused); Assert.Equal(5, calls);
    }

    [Fact]
    public async Task MissingSupportAndInvalidPageBoundsFailWithoutInventingState()
    {
        var client = new AgentTestRuntime().CreateContext().Platform.ChiefLeadership;
        await Assert.ThrowsAsync<PlatformCapabilityException>(() => client.ReadAsync());
        await Assert.ThrowsAsync<ArgumentException>(() => client.ReadAsync(new(PageSize: 101)));
        await Assert.ThrowsAsync<ArgumentException>(() => client.ReadAsync(new(PageSize: 0)));
        Assert.All(ChiefLeadershipCapabilities.All, key => Assert.True(CapabilityCatalog.IsKnown(key)));
    }
}
