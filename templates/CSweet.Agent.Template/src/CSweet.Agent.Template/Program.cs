using CSweet.Agent.SDK;
using CSweet.Agent.Template;
using Microsoft.Extensions.Hosting;

if (args.Contains("--self-test", StringComparer.Ordinal))
{
    var result = await new AgentTestRuntime().ExecuteCapabilityAsync(
        new TemplateAgent(),
        TemplateAgent.PrimaryCapability,
        new TemplateRequest("C-Sweet"));
    Console.WriteLine(result.Value);
    Environment.ExitCode = result.Succeeded ? 0 : 1;
    return;
}

var builder = Host.CreateApplicationBuilder(args);
builder.AddCSweetAgent<TemplateAgent>();
await builder.Build().RunAsync();
