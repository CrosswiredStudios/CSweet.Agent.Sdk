using System.Security.Cryptography;
using System.Text.Json;
using CSweet.Agent.SDK.Compute;

namespace CSweet.Agent.SDK.Tests;

public sealed class ComputeEvidenceTests
{
    [Theory]
    [InlineData(956767)] [InlineData(4194304)]
    public async Task One_export_and_bounded_reads_download_the_exact_archive(int size)
    {
        var bytes=new byte[size];RandomNumberGenerator.Fill(bytes);var hash=Convert.ToHexStringLower(SHA256.HashData(bytes));
        var request=new ExportComputeEvidenceRequest(Guid.NewGuid(),7,"archive",new(new('a',40),new('b',64),hash,size));
        var operation=new ComputeOperation(Guid.NewGuid(),request.EnvironmentId,8,"Completed",null,
            new(Artifact:new(request.Archive.SourceCommitSha,request.Archive.BuildSha256,hash,size)));
        var exports=0;var reads=0;
        var runtime=new AgentTestRuntime().RegisterCapability<JsonElement,ComputeOperation>(ComputeCapabilities.Execute,(input,_)=>{
            exports++;Assert.True(input.GetProperty("workload").TryGetProperty("evidenceArchive",out var archiveInput));return Task.FromResult(operation);
        }).RegisterCapability<JsonElement,ComputeEvidencePage>(ComputeCapabilities.Read,(input,_)=>{
            reads++;Assert.Equal(operation.Id,input.GetProperty("artifactId").GetGuid());var offset=input.GetProperty("offset").GetInt32();
            return Task.FromResult(new ComputeEvidencePage(operation.Id,hash,size,offset,bytes.AsSpan(offset,Math.Min(65536,size-offset)).ToArray()));
        });
        var client=runtime.CreateContext().Platform.Compute;
        var observed=await client.ExportEvidenceAsync(request);Assert.Equal(bytes,await client.ReadEvidenceAsync(observed));
        Assert.Equal(1,exports);Assert.InRange(reads,1,64);
    }
    [Theory]
    [InlineData("offset")] [InlineData("identity")] [InlineData("truncated")] [InlineData("digest")]
    public async Task Changed_pages_never_produce_an_artifact(string scenario)
    {
        var operation=new ComputeOperation(Guid.NewGuid(),Guid.NewGuid(),2,"Completed",null,new(Artifact:new(new('a',40),new('b',64),new('c',64),1)));
        var runtime=new AgentTestRuntime().RegisterCapability<JsonElement,ComputeEvidencePage>(ComputeCapabilities.Read,(_,_)=>Task.FromResult(
            new ComputeEvidencePage(scenario=="identity"?Guid.NewGuid():operation.Id,new('c',64),1,scenario=="offset"?1:0,scenario=="truncated"?[]:[1])));
        await Assert.ThrowsAsync<InvalidDataException>(()=>runtime.CreateContext().Platform.Compute.ReadEvidenceAsync(operation));
    }
    [Fact]
    public async Task Invalid_export_never_reaches_transport()
    {
        var client=new AgentTestRuntime().CreateContext().Platform.Compute;
        await Assert.ThrowsAsync<ArgumentException>(()=>client.ExportEvidenceAsync(new(Guid.NewGuid(),1,"key",new(new('a',40),"../outside",new('c',64),1))));
        await Assert.ThrowsAsync<ArgumentException>(()=>client.ExportEvidenceAsync(new(Guid.NewGuid(),1,"key",new(new('a',40),new('b',64),new('c',64),4194305))));
    }
}
