using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CSweet.Agent.SDK;

/// <summary>Retain one input-bound handoff or judgment. Effects still require their domain command receipts.</summary>
public static class DurableWorkHandoff
{
    public static async Task<T> GetOrCreateAsync<T>(string key, string schema, Func<Task<T>> create,
        AgentRuntimeContext context, CancellationToken ct) where T : class
    {
        var prior = await context.Platform.ReadOperatingStateAsync<T>(key,ct);
        if (prior is not null) return prior.Payload;
        var value = await create();
        try
        {
            var fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
            var written = await context.Platform.WriteOperatingStateAsync(new AgentOperatingStateWriteRequest(key,schema,1,"Active",
                new Dictionary<string,string>(),[],fingerprint,[],Guid.NewGuid(),JsonSerializer.SerializeToElement(value),null,key),ct);
            return written.Payload.Deserialize<T>() ?? throw new InvalidOperationException("The accepted handoff payload is missing.");
        }
        catch (PlatformCapabilityException error) when (error.Code == PlatformCapabilityErrorCode.Conflict)
        {
            var accepted = await context.Platform.ReadOperatingStateAsync<T>(key,ct);
            if (accepted is null) throw;
            return accepted.Payload;
        }
    }
}
