using System.Text.Json;
using CSweet.Agent.SDK;

namespace CSweet.Agent.Template;

public sealed record TemplateRequest(string? Input);
public sealed record TemplateResponse(string Message);

public sealed class TemplateAgent : CSweetAgentBase
{
    public const string PrimaryCapability = "example.execute.v1";

    public override string AgentId => "com.example.agent";
    public override string Version => "0.1.0";

    protected override async Task<AgentWorkResult> ExecuteCapabilityCoreAsync(
        AgentCapabilityRequest request,
        AgentRuntimeContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Capability != PrimaryCapability)
            return AgentWorkResult.Failure($"Capability '{request.Capability}' is not supported.");

        TemplateRequest? input;
        try
        {
            input = DeserializePayload<TemplateRequest>(request.Arguments);
        }
        catch (JsonException)
        {
            return AgentWorkResult.Failure("The request payload is not valid.");
        }

        if (string.IsNullOrWhiteSpace(input?.Input))
            return AgentWorkResult.Failure("input is required.");

        await context.ReportProgressAsync(
            new { stage = "accepted", message = "The request is being processed." },
            cancellationToken);
        return AgentWorkResult.Success(new TemplateResponse($"Processed: {input.Input}"));
    }
}
