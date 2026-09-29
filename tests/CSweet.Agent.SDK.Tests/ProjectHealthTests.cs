using System.Text.Json;

namespace CSweet.Agent.SDK.Tests;

public sealed class ProjectHealthTests
{
    [Fact]
    public async Task ProducerReadsCurrentRevisionAndReportsWithoutModelOrRepairCapabilities()
    {
        var employee = Guid.NewGuid(); var incident = Incident(employee);
        ReportManagementIncident? sent = null;
        var runtime = new AgentTestRuntime()
            .RegisterCapability<ReadManagementIncidents, ManagementIncidentPage>(ProjectHealthCapabilities.Incidents, (r, _) => Task.FromResult(new ManagementIncidentPage([incident], null)))
            .RegisterCapability<ReadProjectDiagnostics, ProjectDiagnosticsPage>(ProjectHealthCapabilities.Diagnostics, (r, _) => Task.FromResult(new ProjectDiagnosticsPage(
                [new("validation", Guid.NewGuid(), DateTimeOffset.UtcNow, "agent.payload_invalid: required field is missing")], null, [])))
            .RegisterCapability<ReportManagementIncident, ManagementIncident>(ProjectHealthCapabilities.Report, (r, _) => { sent = r; return Task.FromResult(incident); });
        await ProjectIncidentReview.HandleAsync(Hint(incident), runtime.CreateContext(identity: Identity(employee)), "production", true, default);
        Assert.NotNull(sent); Assert.Equal(incident.Revision, sent.ExpectedRevision);
        Assert.Contains("schema", sent.LikelyCause); Assert.Contains("required field", sent.Facts);
    }

    [Fact]
    public async Task UnavailableDiagnosticsStillProduceBaselineReport()
    {
        var employee = Guid.NewGuid(); var incident = Incident(employee); ReportManagementIncident? sent = null;
        var runtime = new AgentTestRuntime()
            .RegisterCapability<ReadManagementIncidents, ManagementIncidentPage>(ProjectHealthCapabilities.Incidents, (_, _) => Task.FromResult(new ManagementIncidentPage([incident], null)))
            .RegisterCapability<ReportManagementIncident, ManagementIncident>(ProjectHealthCapabilities.Report, (r, _) => { sent = r; return Task.FromResult(incident); });
        await ProjectIncidentReview.HandleAsync(Hint(incident), runtime.CreateContext(identity: Identity(employee)), "production", true, default);
        Assert.Contains("unavailable", sent!.MissingEvidence);
    }

    [Fact]
    public async Task StaleHintCannotMakePriorRecipientForwardAgain()
    {
        var employee = Guid.NewGuid(); var incident = Incident(Guid.NewGuid());
        var runtime = new AgentTestRuntime().RegisterCapability<ReadManagementIncidents, ManagementIncidentPage>(
            ProjectHealthCapabilities.Incidents, (_, _) => Task.FromResult(new ManagementIncidentPage([incident], null)));
        await ProjectIncidentReview.HandleAsync(Hint(incident), runtime.CreateContext(identity: Identity(employee)), "creative direction", false, default);
    }

    [Fact]
    public async Task ManagerForwardsSameIncidentWithResponsibilityAssessment()
    {
        var employee = Guid.NewGuid(); var incident = Incident(employee); ForwardManagementIncident? sent = null;
        var runtime = new AgentTestRuntime()
            .RegisterCapability<ReadManagementIncidents, ManagementIncidentPage>(ProjectHealthCapabilities.Incidents, (_, _) => Task.FromResult(new ManagementIncidentPage([incident], null)))
            .RegisterCapability<ForwardManagementIncident, ManagementIncident>(ProjectHealthCapabilities.Forward, (r, _) => { sent = r; return Task.FromResult(incident); });
        await ProjectIncidentReview.HandleAsync(Hint(incident), runtime.CreateContext(identity: Identity(employee)), "creative direction", false, default);
        Assert.Equal(incident.Id, sent!.IncidentId); Assert.Contains("creative direction", sent.Reason);
    }

    [Fact]
    public async Task RecoveryWithOldGrantsDoesNotBreakExistingAgentWork() =>
        await ProjectIncidentReview.RecoverAsync(new AgentTestRuntime().CreateContext(), "production", true, default);

    private static ManagementIncident Incident(Guid recipient) => new(Guid.NewGuid(), Guid.NewGuid(), recipient, recipient,
        "Open", "Failure", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(15), 7,
        "No progress", "Unknown", "Unknown", "Review", []);
    private static AgentIdentity Identity(Guid employee) => new(employee.ToString(), "Manager", null, null, null, [], null, null, null);
    private static AgentEventEnvelope Hint(ManagementIncident incident) => new(Guid.NewGuid(), Guid.NewGuid(), ProjectHealthEvents.IncidentChanged,
        JsonSerializer.SerializeToElement(new ProjectHealthHint(incident.WorkstreamId, incident.Id, 1), new JsonSerializerOptions(JsonSerializerDefaults.Web)), DateTimeOffset.UtcNow, "test");
}
