using System.Text.Json;
using CSweet.Agent.Contracts.Packaging;

namespace CSweet.Agent.SDK.Tests;

public sealed class ManagerAgentTests
{
    [Theory]
    [InlineData("game-producer")]
    [InlineData("product-manager")]
    [InlineData("future-manager-specialty")]
    public async Task JsonDeclaresTheSameBaseTypeForDifferentSpecializedRoles(string specialty)
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, $$$"""
            {"manifestVersion":"2.0","kind":"agent","id":"example.manager","name":"Manager","version":"1.0.0",
             "publisher":{"id":"example","name":"Example"},
             "rolePolicy":{"baseType":"manager","profile":"manager.v1","declaredRoleKeys":["manager","{{{specialty}}}"]},
             "runtime":{"type":"dotnet-project","projectPath":"src/Manager.csproj","targetFramework":"net10.0","defaultActivationMode":"OnDemand"},
             "protocol":{"minimumVersion":"2.0","maximumVersion":"2.x"},"provides":[],"requires":[],"events":{"subscribes":[]}}
            """);
            var manifest = await AgentManifestLoader.LoadAsync(path, default);
            Assert.Equal(AgentBaseTypes.Manager, manifest.RolePolicy!.EffectiveBaseType);
            Assert.True(RoleTaxonomy.SatisfiesRole(manifest.RolePolicy.DeclaredRoleKeys, "manager"));
            Assert.False(RoleTaxonomy.SatisfiesRole(["manager"], specialty));
            Assert.Contains("\"baseType\":\"manager\"", JsonSerializer.Serialize(manifest.RolePolicy, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            AgentManifestLoader.Validate(manifest);
            var inconsistent = JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)).Replace("\"baseType\":\"manager\"", "\"baseType\":\"individual-contributor\"");
            await File.WriteAllTextAsync(path, inconsistent);
            await Assert.ThrowsAsync<InvalidOperationException>(() => AgentManifestLoader.LoadAsync(path, default));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LegacyPolicyKeepsItsManagerFamily()
    {
        var policy = new AgentRolePolicyManifest { Profile = AgentRolePolicyProfiles.Manager };
        Assert.Equal(AgentBaseTypes.Manager, policy.EffectiveBaseType);
        Assert.DoesNotContain("baseType", JsonSerializer.Serialize(policy, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BaseDispatchesBeforeSpecializedWorkflowAndRecordsRecoveryOrDeterministicFailure(bool fail)
    {
        var self = Guid.NewGuid();
        var incident = new ManagementIncident(Guid.NewGuid(), Guid.NewGuid(), self, self, "Open", "schema failure", null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(15), 1, "payload_invalid", "Unknown", "Unknown", "Review", []);
        var agent = new ExampleManager(fail); ReportManagementIncident? report = null;
        var runtime = new AgentTestRuntime()
            .RegisterCapability<ReadManagementIncidents, ManagementIncidentPage>(ProjectHealthCapabilities.Incidents, (_, _) => Task.FromResult(new ManagementIncidentPage([incident], null)))
            .RegisterCapability<ReportManagementIncident, ManagementIncident>(ProjectHealthCapabilities.Report, (r, _) => { report = r; return Task.FromResult(incident); });
        var context = runtime.CreateContext(identity: new AgentIdentity(self.ToString(), "Manager", null, null, null, [], null, null, null));
        var hint = JsonSerializer.SerializeToElement(new ProjectHealthHint(incident.WorkstreamId, incident.Id, 1), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var message = new AgentEventEnvelope(Guid.NewGuid(), Guid.NewGuid(), ProjectHealthEvents.ReviewDue, hint, DateTimeOffset.UtcNow, "test");
        await agent.HandleEventAsync(message, context, default);
        Assert.False(agent.OrdinaryEventCalled); Assert.Equal("manager", agent.BaseType);
        Assert.Equal(fail ? IncidentDispositions.Escalate : IncidentDispositions.AwaitingRecovery, report!.Disposition);
        if (fail) Assert.Contains("assessment failed", report.MissingEvidence);
        else Assert.Equal("existing-recovery-request", report.ActionReference);
        await agent.HandleEventAsync(message, context, default);
        Assert.Single(agent.ActionKeys.Distinct());
    }

    [Fact]
    public async Task MissingMonitoringGrantsDoNotPreventSpecializedAttentionWork()
    {
        var agent = new ExampleManager(false);
        await agent.HandleAttentionReviewAsync(new(Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(5), "test"),
            new AgentTestRuntime().CreateContext(), default);
        Assert.True(agent.OrdinaryAttentionCalled);
    }

    private sealed class ExampleManager(bool fail) : CSweetManagerAgentBase
    {
        public override string AgentId => "example.manager";
        public override string Version => "1.0.0";
        protected override string ManagementResponsibility => "example project management";
        public bool OrdinaryEventCalled, OrdinaryAttentionCalled;
        public List<string> ActionKeys { get; } = [];
        protected override Task<ManagerIncidentAssessment> AssessIncidentAsync(ManagerIncidentContext incident, AgentRuntimeContext context, CancellationToken token)
        {
            ActionKeys.Add(incident.ActionIdempotencyKey);
            if (fail) throw new InvalidOperationException("private diagnostic detail");
            return Task.FromResult(ManagerIncidentAssessment.AwaitingRecovery("Authorized recovery is pending.", DateTimeOffset.UtcNow.AddMinutes(5), "existing-recovery-request"));
        }
        protected override Task HandleManagerEventAsync(AgentEventEnvelope message, AgentRuntimeContext context, CancellationToken token)
        { OrdinaryEventCalled = true; return Task.CompletedTask; }
        protected override Task HandleManagerAttentionReviewAsync(AgentAttentionReviewContext review, AgentRuntimeContext context, CancellationToken token)
        { OrdinaryAttentionCalled = true; return Task.CompletedTask; }
    }
}
