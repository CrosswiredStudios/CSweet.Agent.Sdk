using System.Security.Cryptography;
using System.Text.Json;
using CSweet.Agent.SDK.Compute;

namespace CSweet.Agent.SDK.Tests;

public sealed class ComputeBuildUploadTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task One_compute_operation_reuses_staged_parts_after_a_lost_submission_reply(bool lostReply)
    {
        var bytes = new byte[530000]; RandomNumberGenerator.Fill(bytes);
        var states = new Dictionary<string, AgentOperatingStateResponse>(); var writes = 0; var submits = 0;
        var operation = new ComputeOperation(Guid.NewGuid(), Guid.NewGuid(), 8, "Pending", null, null);
        var runtime = new AgentTestRuntime()
            .RegisterCapability<AgentOperatingStateReadRequest, AgentOperatingStateReadResponse>(PlatformCapabilities.AgentOperatingStateRead,
                (r, _) => Task.FromResult(new AgentOperatingStateReadResponse(states.GetValueOrDefault(r.StateKey))))
            .RegisterCapability<AgentOperatingStateWriteRequest, AgentOperatingStateResponse>(PlatformCapabilities.AgentOperatingStateWrite, (r, _) => {
                writes++; Assert.InRange(r.Payload.GetRawText().Length, 1, 128000);
                var row = new AgentOperatingStateResponse(Guid.NewGuid(), r.StateKey, r.SchemaId, 1, "Active", new Dictionary<string,string>(), [],
                    r.DecisionFingerprint, [], Guid.NewGuid(), r.Payload, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
                states.Add(r.StateKey, row); return Task.FromResult(row);
            })
            .RegisterCapability<JsonElement, ComputeOperation>(ComputeCapabilities.Execute, (r, _) => {
                submits++;
                Assert.Equal("same-build", r.GetProperty("idempotencyKey").GetString());
                var input = r.GetProperty("workload").GetProperty("buildArchive").Deserialize<ComputeBuildArchiveInput>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
                Assert.Equal(bytes.Length, input.Length); Assert.Equal(9, input.PartSha256.Count);
                Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), input.BuildSha256);
                Assert.True(r.GetRawText().Length < 2000); // No archive bytes in the compute dispatch request.
                if (lostReply && submits == 1) throw new IOException("Lost accepted reply");
                return Task.FromResult(operation);
            });
        var client = runtime.CreateContext().Platform.Compute;
        if (lostReply) await Assert.ThrowsAsync<IOException>(() => client.UploadBuildAsync(operation.EnvironmentId, 7, new('a',40), bytes, "same-build"));
        Assert.Equal(operation.Id, (await client.UploadBuildAsync(operation.EnvironmentId, 7, new('a',40), bytes, "same-build")).Id);
        Assert.Equal(9, writes); Assert.Equal(lostReply ? 2 : 1, submits);
        var changed = states.First();
        states[changed.Key] = changed.Value with { Payload = JsonSerializer.SerializeToElement(new ComputeBuildInputPart(new('a',40), new('f',64), bytes.Length, 0, [1])) };
        await Assert.ThrowsAsync<InvalidDataException>(() => client.UploadBuildAsync(operation.EnvironmentId, 7, new('a',40), bytes, "same-build"));
    }

    [Fact]
    public async Task Invalid_build_never_reaches_transport()
    {
        var client = new AgentTestRuntime().CreateContext().Platform.Compute;
        await Assert.ThrowsAsync<ArgumentException>(() => client.StageBuildAsync("main", new byte[1]));
        await Assert.ThrowsAsync<ArgumentException>(() => client.StageBuildAsync(new('a',40), ReadOnlyMemory<byte>.Empty));
        await Assert.ThrowsAsync<ArgumentException>(() => client.StageBuildAsync(new('a',40), new byte[16*1024*1024+1]));
    }
}
