using System.Text.Json;
using Microsoft.Extensions.AI;

namespace CSweet.Agent.SDK.Tests;

public sealed class ManagerResourceReviewTests
{
    [Fact]
    public async Task Missed_and_duplicate_wakes_use_current_requests_and_approve_the_exact_plan_once()
    {
        var state = new ResourceRequestState(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            ResourceCapabilities.BuildToolchain, "phaser.web-2d.v1", "Build the approved game", "PendingApproval", 3, "plan-hash",
            new("node", "Prepare approved Node environment", Guid.NewGuid(), "node", "1.0.2", "manifest", "package", Guid.NewGuid(),
                "definition", "linux", "image", [], [], [], 0, "USD"), null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var decisions = new List<ResourceDecision>();
        var runtime = new AgentTestRuntime()
            .RegisterCapability<ResourceRead, IReadOnlyList<ResourceRequestState>>(ResourceCapabilities.Read,
                (_, _) => Task.FromResult<IReadOnlyList<ResourceRequestState>>(state.Status == "PendingApproval" ? [state] : []))
            .RegisterCapability<ResourceDecision, ResourceRequestState>(ResourceCapabilities.Decide, (input, _) => {
                decisions.Add(input); Assert.Equal(state.Id, input.RequestId); Assert.Equal(state.Revision, input.ExpectedRevision);
                Assert.Equal(state.PlanHash, input.PlanHash); Assert.Equal("Approve", input.Decision);
                state = state with { Status = "Approved", Revision = state.Revision + 1 }; return Task.FromResult(state);
            });
        var agent = new Manager();
        // Attention recovery supplies the missed initial notification.
        await agent.HandleAttentionReviewAsync(new(Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(5), "Reconnect"), runtime.CreateContext(), default);
        await agent.HandleEventAsync(new(Guid.NewGuid(), Guid.NewGuid(), ResourceCapabilities.Changed, JsonSerializer.SerializeToElement(new { }), DateTimeOffset.UtcNow), runtime.CreateContext(), default);
        Assert.Single(decisions); Assert.Equal(1, agent.Calls);
    }
    private sealed class Manager : CSweetManagerAgentBase
    {
        public int Calls;
        public override string AgentId => "test.manager";
        public override string Version => "1.0.0";
        protected override string ManagementResponsibility => "Project delivery within approved scope";
        protected override bool SupportsResourceApproval => true;
        protected override IChatClient CreateResourceReviewChatClient(AgentRuntimeContext context) => new ReviewClient(() => Calls++);
    }
    private sealed class ReviewClient(Action called) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            called(); Assert.Empty(options!.Tools!); Assert.Null(options.MaxOutputTokens);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "{\"decision\":\"Approve\",\"reason\":\"Required zero-cost capacity within project scope.\"}")));
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
