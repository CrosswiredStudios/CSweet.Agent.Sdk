using System.Text.Json;
using CSweet.Agent.SDK;
using Microsoft.Extensions.AI;

namespace CSweet.Agent.SDK.Tests;

public sealed class AgentModelJsonTests
{
    private sealed record Verdict(string Decision, IReadOnlyList<string> Reasons);

    private static readonly ChatMessage[] Messages = [new(ChatRole.System, "Decide."), new(ChatRole.User, "Evidence")];

    private static AgentModelJsonRequest<Verdict> Request(Func<Verdict, Verdict>? normalize = null) => new("test decision")
    {
        Schema = AgentModelJson.SchemaFor<Verdict>(),
        Normalize = normalize
    };

    [Theory]
    [InlineData("{\"decision\":\"yes\",\"reasons\":[\"a\"]}")]
    [InlineData("```json\n{\"decision\":\"yes\",\"reasons\":[\"a\"]}\n```")]
    [InlineData("Here it is: {\"decision\":\"yes\",\"reasons\":[\"a\"]} Thanks.")]
    [InlineData("{\"decision\":\"yes\",\"reasons\":[\"a\"]}\n<think>{\"draft\": true}</think>")]
    public void Parse_reads_bare_fenced_or_wrapped_objects(string text) =>
        Assert.Equal("yes", AgentModelJson.Parse<Verdict>(text).Decision);

    [Theory]
    [InlineData("", "empty")]
    [InlineData("no json here", "no JSON object")]
    [InlineData("{\"decision\": }", "not valid JSON")]
    public void Parse_names_the_problem(string text, string problem) =>
        Assert.Contains(problem, Assert.Throws<AgentModelJsonException>(() => AgentModelJson.Parse<Verdict>(text)).Message);

    [Fact]
    public async Task A_valid_reply_takes_one_call_with_the_schema()
    {
        var client = new ScriptedClient("{\"decision\":\"yes\",\"reasons\":[]}");
        var verdict = await AgentModelJson.ReadAsync(client, Messages, Request(), default);
        Assert.Equal("yes", verdict.Decision);
        Assert.Equal(1, client.Calls.Count);
        var format = Assert.IsType<ChatResponseFormatJson>(client.Formats[0]);
        Assert.Equal("test_decision", format.SchemaName);
        Assert.Equal("object", format.Schema!.Value.GetProperty("type").GetString());
    }

    [Fact]
    public async Task A_rejected_reply_is_corrected_in_the_same_conversation()
    {
        var client = new ScriptedClient("{\"decision\":\"maybe\",\"reasons\":[]}", "{\"decision\":\"no\",\"reasons\":[\"x\"]}");
        var verdict = await AgentModelJson.ReadAsync(client, Messages, Request(v =>
            v.Decision is "yes" or "no" ? v : throw new AgentModelJsonException($"decision \"{v.Decision}\" must be yes or no")), default);
        Assert.Equal("no", verdict.Decision);
        var second = client.Calls[1];
        Assert.Equal(4, second.Count); // Original two messages, the rejected reply, the correction.
        Assert.Equal(ChatRole.Assistant, second[2].Role);
        Assert.Contains("maybe", second[2].Text);
        Assert.Contains("decision \"maybe\" must be yes or no", second[3].Text);
        Assert.Contains("Correction 1/2", second[3].Text);
    }

    [Fact]
    public async Task Only_the_latest_rejected_reply_is_kept()
    {
        var client = new ScriptedClient("first", "second", "{\"decision\":\"yes\",\"reasons\":[]}");
        await AgentModelJson.ReadAsync(client, Messages, Request(), default);
        Assert.Equal(4, client.Calls[2].Count);
        Assert.Equal("second", client.Calls[2][2].Text);
        Assert.Contains("Correction 2/2", client.Calls[2][3].Text);
    }

    [Fact]
    public async Task Exhaustion_reports_the_operation_attempts_and_last_problem()
    {
        var client = new ScriptedClient("nothing");
        var error = await Assert.ThrowsAsync<AgentModelJsonException>(() => AgentModelJson.ReadAsync(client, Messages, Request(), default));
        Assert.Equal(3, client.Calls.Count);
        Assert.Equal(3, error.Attempts);
        Assert.Equal("the reply contained no JSON object", error.Problem);
        Assert.StartsWith("test decision: the model reply was not accepted after 3 attempt(s)", error.Message);
    }

    [Fact]
    public async Task A_truncated_reply_asks_for_a_shorter_answer()
    {
        var client = new ScriptedClient("{\"decision\":\"yes\",\"reasons\":[]}") { Finish = ChatFinishReason.Length };
        var error = await Assert.ThrowsAsync<AgentModelJsonException>(() =>
            AgentModelJson.ReadAsync(client, Messages, Request() with { CorrectionLimit = 0 }, default));
        Assert.Contains("output budget", error.Problem);
        Assert.Single(client.Calls);
    }

    [Fact]
    public async Task Caller_options_are_copied_not_changed()
    {
        var options = new ChatOptions { MaxOutputTokens = 123 };
        var client = new ScriptedClient("{\"decision\":\"yes\",\"reasons\":[]}");
        await AgentModelJson.ReadAsync(client, Messages, Request() with { Options = options }, default);
        Assert.Null(options.ResponseFormat);
        Assert.Equal(123, client.Options[0]!.MaxOutputTokens);
    }

    private sealed class ScriptedClient(params string[] replies) : IChatClient
    {
        private int _next;
        public ChatFinishReason Finish { get; init; } = ChatFinishReason.Stop;
        public List<IReadOnlyList<ChatMessage>> Calls { get; } = [];
        public List<ChatResponseFormat?> Formats { get; } = [];
        public List<ChatOptions?> Options { get; } = [];
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls.Add(messages.ToArray()); Formats.Add(options?.ResponseFormat); Options.Add(options);
            var text = replies[Math.Min(_next++, replies.Length - 1)];
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)) { FinishReason = Finish });
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
