using CSweet.Agent.SDK;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.AddCSweetAgent<HelloAgent>();
await builder.Build().RunAsync();

internal sealed class HelloAgent : CSweetAgentBase
{
    public override string AgentId => "com.csweet.sample.hello";
    public override string Version => "0.1.0";
}
