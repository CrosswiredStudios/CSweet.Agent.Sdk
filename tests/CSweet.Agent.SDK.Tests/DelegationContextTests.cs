using System.Text.Json;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK.Tests;

public sealed class DelegationContextTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeExecutionReceivesCurrentDelegationProjectAndPreservesSource(bool transportHasContext)
    {
        var organization = Guid.NewGuid(); var project = Guid.NewGuid(); var team = Guid.NewGuid();
        var item = Guid.NewGuid(); var chat = Guid.NewGuid(); var message = Guid.NewGuid();
        var delegation = Snapshot(project, team, item) with { ExecutionMessageId = message };
        var incoming = new CommunicationMessageReceivedEvent(Guid.NewGuid(), chat.ToString(), "Director", "Review the player experience", null, Guid.NewGuid(), 1, message) {
            Context = new Dictionary<string, string> { ["currentUserMessage"] = "Review the player experience" },
            WorkContext = transportHasContext ? new(organization, project, team, Guid.NewGuid(), item, null, null, Guid.NewGuid(), null, "game") : null };
        var runtime = new AgentTestRuntime()
            .RegisterCapability<DelegateWithTrackingRequest, DelegationSnapshot>(DelegationCapabilities.Create, (_, _) => throw new InvalidOperationException("No nested delegation"))
            .RegisterCapability<ReadDelegationRequest, IReadOnlyList<DelegationSnapshot>>(DelegationCapabilities.Read, (_, _) => Task.FromResult<IReadOnlyList<DelegationSnapshot>>([delegation]))
            .RegisterCapability<DelegationResponseRequest, DelegationSnapshot>(DelegationCapabilities.Respond, (_, _) => Task.FromResult(delegation));
        var agent = new NativeAgent();
        var envelope = new AgentEventEnvelope(Guid.NewGuid(), Guid.NewGuid(), CommunicationEvents.MessageReceived,
            JsonSerializer.SerializeToElement(incoming, Json), DateTimeOffset.UtcNow);
        Assert.True(await DelegationRuntime.DispatchAsync(agent, envelope, runtime.CreateContext(organization.ToString()), default));
        Assert.Equal(project, agent.Message!.WorkContext!.WorkstreamId);
        Assert.Equal(team, agent.Message.WorkContext.TeamId);
        Assert.Equal(item, agent.Message.WorkContext.WorkItemId);
        Assert.Equal(organization, agent.Message.WorkContext.OrganizationId);
        Assert.Equal(agent.Message.WorkContext, agent.Envelope!.WorkContext);
        Assert.Equal(incoming.Message, agent.Message.Message);
        Assert.Equal(incoming.MessageId, agent.Message.MessageId);
        Assert.Equal(incoming.Context, agent.Message.Context);
        if (transportHasContext) Assert.Equal(incoming.WorkContext, agent.Message.WorkContext);
    }

    [Theory]
    [InlineData("organization")]
    [InlineData("project")]
    [InlineData("team")]
    [InlineData("item")]
    public void ConflictingTransportScopeIsNeverReboundOrExecuted(string field)
    {
        var organization = Guid.NewGuid(); var project = Guid.NewGuid(); var team = Guid.NewGuid(); var item = Guid.NewGuid();
        var incoming = new CommunicationMessageReceivedEvent(Guid.NewGuid(), Guid.NewGuid().ToString(), "Director", "Review", null, Guid.NewGuid(), 1, Guid.NewGuid()) {
            WorkContext = new(field == "organization" ? Guid.NewGuid() : organization, field == "project" ? Guid.NewGuid() : project,
                field == "team" ? Guid.NewGuid() : team, null, field == "item" ? Guid.NewGuid() : item, null, null, Guid.NewGuid(), null, null) };
        Assert.Throws<InvalidOperationException>(() => DelegationRuntime.BindExecutionContext(Snapshot(project, team, item), incoming,
            new AgentTestRuntime().CreateContext(organization.ToString())));
    }

    private static DelegationSnapshot Snapshot(Guid project, Guid team, Guid item) => new(Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), "Director", "Creative Director", "Review the player experience", "Return creative findings", "Active", true,
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, null, Guid.NewGuid(), null, new(project, team, item), DateTimeOffset.UtcNow, [], []);

    private sealed class NativeAgent : CSweetAgentBase
    {
        public override string AgentId => "test.native-context";
        public override string Version => "1.0.0";
        public CommunicationMessageReceivedEvent? Message;
        public AgentEventEnvelope? Envelope;
        public override Task<DelegationIntent> ClassifyDelegationIntentAsync(CommunicationMessageReceivedEvent incoming, AgentRuntimeContext context, CancellationToken token) =>
            Task.FromResult(new DelegationIntent("Other", null, null, null, null));
        public override async Task HandleEventAsync(AgentEventEnvelope envelope, AgentRuntimeContext context, CancellationToken token)
        {
            Envelope = envelope; Message = envelope.Data.Deserialize<CommunicationMessageReceivedEvent>(Json)!;
            await using var stream = context.CreateTurnStream(Message.ConversationId, Message.TurnId, Message.Attempt);
            await stream.CommitAsync("Read the approved project context; creative review is next.", token);
        }
        public override Task<Guid?> ResolveDelegationWorkAsync(DelegationSnapshot delegation, string response, AgentRuntimeContext context, CancellationToken token) => Task.FromResult<Guid?>(null);
    }
}
