using CSweet.Agent.SDK;

namespace HelloAgentSample;

public sealed record HelloRequest(string? Name);
public sealed record HelloResponse(string Message);

public sealed class HelloAgent : CSweetAgentBase
{
    public const string PrimaryCapability = "hello.say.v1";

    public override string AgentId => "com.csweet.sample.hello";
    public override string Version => "0.1.0";

    protected override async Task<AgentWorkResult> ExecuteCapabilityCoreAsync(
        AgentCapabilityRequest request,
        AgentRuntimeContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Capability != PrimaryCapability)
            return AgentWorkResult.Failure($"Capability '{request.Capability}' is not supported.");

        var input = DeserializePayload<HelloRequest>(request.Arguments);
        if (string.IsNullOrWhiteSpace(input?.Name))
            return AgentWorkResult.Failure("name is required.");

        await context.ReportProgressAsync(new { stage = "accepted" }, cancellationToken);
        return AgentWorkResult.Success(new HelloResponse($"Hello, {input.Name}!"));
    }
}
