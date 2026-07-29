using CSweet.Agent.SDK;
using HelloAgentSample;
using Microsoft.Extensions.Hosting;

if (args.Contains("--self-test", StringComparer.Ordinal))
{
    var result = await new AgentTestRuntime().ExecuteCapabilityAsync(
        new HelloAgent(),
        HelloAgent.PrimaryCapability,
        new HelloRequest("C-Sweet"));
    Console.WriteLine(result.Value);
    Environment.ExitCode = result.Succeeded ? 0 : 1;
    return;
}

var builder = Host.CreateApplicationBuilder(args);
builder.AddCSweetAgent<HelloAgent>();
await builder.Build().RunAsync();
