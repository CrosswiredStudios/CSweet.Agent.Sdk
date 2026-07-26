using CSweet.Agent.SDK;
using Microsoft.Extensions.Hosting;

if (args.Contains("--self-test", StringComparer.Ordinal))
{
    var runtime = new AgentTestRuntime();
    var result = await runtime.ExecuteCapabilityAsync(
        new HelloAgent(),
        "hello.say.v1",
        new { name = "C-Sweet" });
    Console.WriteLine(result.Value);
    return;
}

var builder = Host.CreateApplicationBuilder(args);
builder.AddCSweetAgent<HelloAgent>();
await builder.Build().RunAsync();

internal sealed class HelloAgent : CSweetAgentBase
{
    public override string AgentId => "com.csweet.sample.hello";
    public override string Version => "1.0.0";

    protected override Task<AgentWorkResult> ExecuteCapabilityCoreAsync(
        AgentCapabilityRequest request,
        AgentRuntimeContext context,
        CancellationToken cancellationToken)
    {
        if (request.Capability != "hello.say.v1")
            return Task.FromResult(AgentWorkResult.Failure("Unsupported capability."));
        var name = request.Arguments.GetProperty("name").GetString() ?? "world";
        return Task.FromResult(AgentWorkResult.Success(new { message = $"Hello, {name}!" }));
    }
}
