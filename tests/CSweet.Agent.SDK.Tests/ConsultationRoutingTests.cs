using Microsoft.Extensions.AI;

namespace CSweet.Agent.SDK.Tests;

public sealed class ConsultationRoutingTests
{
    [Fact]
    public async Task Question_intent_can_select_manager_despite_self_capability_hint()
    {
        var self = Guid.NewGuid(); var manager = Guid.NewGuid(); var other = Guid.NewGuid();
        AgentTeammate Member(Guid id, string name) => new(id.ToString(), name, "Agent", null, null, "Member", "Available") {
            AgentInstallationId = Guid.NewGuid(), RuntimeEligibility = "Eligible" };
        var candidates = ConsultationRouting.Candidates([Member(self,"Gabriel"), Member(manager,"Marcus"), Member(other,"Victor")], self.ToString());
        Assert.DoesNotContain(candidates, p => p.EmployeeId == self.ToString());
        var index = candidates.ToList().FindIndex(p => p.EmployeeId == manager.ToString());
        using var client = new ReplyClient("{\"candidate\":" + index + ",\"reason\":\"The question requests Marcus's management judgment.\"}");
        var choice = await ConsultationRouting.SelectAsync(candidates, new(self.ToString(), "Gabriel", null, null, null, [], null, manager.ToString(), "Marcus"),
            "Ask Marcus Vale one bounded question: approve refining the current increment to a versioned technical feasibility/decomposition record, deferring implementation until staffing and build authorization.",
            "Producer", client, default);
        Assert.Equal(manager.ToString(), candidates[choice.Candidate!.Value].EmployeeId);
        Assert.Contains("suggestedCapability", client.Prompt);
        Assert.Contains("reportingManager", client.Prompt);
    }

    [Theory]
    [InlineData("{\"candidate\":9,\"reason\":\"Invented\"}")]
    [InlineData("{\"candidate\":-1,\"reason\":\"Self\"}")]
    public async Task Invalid_selection_gets_one_correction_then_stops(string reply)
    {
        var member = new AgentTeammate(Guid.NewGuid().ToString(), "Reviewer", "Agent", null,null,"Member","Available");
        using var client = new ReplyClient(reply);
        var error = await Assert.ThrowsAsync<AgentModelJsonException>(() => ConsultationRouting.SelectAsync([member],
            new("self","Owner",null,null,null,[],null,null,null), "Review this question", null, client, default));
        Assert.Equal(2, client.Calls); Assert.Equal(2, error.Attempts);
    }

    private sealed class ReplyClient(string reply) : IChatClient
    {
        public int Calls; public string Prompt = "";
        public void Dispose() { }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        { Calls++; Prompt = string.Join("\n", messages.Select(m => m.Text)); return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,reply))); }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
