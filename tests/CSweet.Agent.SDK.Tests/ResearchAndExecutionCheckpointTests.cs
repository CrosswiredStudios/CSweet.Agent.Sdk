using System.Text.Json;

namespace CSweet.Agent.SDK.Tests;

public sealed class ResearchAndExecutionCheckpointTests
{
    [Fact]
    public async Task ResearchUsesTypedGrantedCallsAndBoundedDefaults()
    {
        var runtime = new AgentTestRuntime()
            .RegisterCapability<WebSearchRequest,WebSearchResult>(ResearchCapabilities.Search,(request,_)=>
            {
                Assert.Equal(5,request.Limit); Assert.Equal("docs",request.Query);
                return Task.FromResult(new WebSearchResult([new("Docs","https://example.com/docs","API")],["upstream: timeout"]));
            })
            .RegisterCapability<WebPageRequest,WebPageResult>(ResearchCapabilities.Read,(request,_)=>
            {
                Assert.Equal(20000,request.MaximumCharacters);
                return Task.FromResult(new WebPageResult(request.Url,"Docs","API",false));
            });
        var research=runtime.CreateContext().Platform.Research;
        Assert.Single((await research.SearchAsync(new("docs"))).Results);
        Assert.Equal("API",(await research.ReadPageAsync(new("https://example.com/docs"))).Text);
        Assert.Equal(new[]{"search_web","read_web_page"},research.GetTools().Select(x=>x.Name));
        Assert.Contains(ResearchCapabilities.Search,CapabilityCatalog.All);
    }

    [Theory]
    [InlineData("",5)]
    [InlineData("docs",0)]
    [InlineData("docs",11)]
    public async Task InvalidSearchInputsNeverReachTransport(string query,int count)
    {
        var research=new AgentTestRuntime().CreateContext().Platform.Research;
        await Assert.ThrowsAsync<ArgumentException>(()=>research.SearchAsync(new(query,count)));
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://user:secret@example.com")]
    public async Task PageInputsCannotCarryFilesystemOrCredentialAuthority(string url)
    {
        var research=new AgentTestRuntime().CreateContext().Platform.Research;
        await Assert.ThrowsAsync<ArgumentException>(()=>research.ReadPageAsync(new(url)));
    }

    [Fact]
    public async Task MissingCheckpointIsExplicitAndNoWorkspaceSourceSupportsDirectWork()
    {
        var runtime=new AgentTestRuntime()
            .RegisterCapability<ExecutionCheckpointRead,ExecutionCheckpointReadResult>(ExecutionCheckpointCapabilities.Read,(_,_)=>Task.FromResult(new ExecutionCheckpointReadResult(null)))
            .RegisterCapability<ExecutionCheckpointWrite,ExecutionCheckpoint>(ExecutionCheckpointCapabilities.Write,(input,_)=>
            {
                Assert.Equal("stable-work",input.SessionKey); Assert.Null(input.WorkspaceId); Assert.NotNull(input.SourceArchive);
                return Task.FromResult(new ExecutionCheckpoint(input.SessionKey,input.EngineVersion,1,1,input.EngineArchive,"digest",SourceArchive:input.SourceArchive));
            });
        var checkpoints=runtime.CreateContext().Platform.ExecutionCheckpoints;
        Assert.Null(await checkpoints.ReadAsync(new("stable-work","engine-1")));
        Assert.Equal(1,(await checkpoints.WriteAsync(new("stable-work","engine-1",0,1,[1],"stable-effect",SourceArchive:[2]))).Revision);
        Assert.Contains(ExecutionCheckpointCapabilities.Read,CapabilityCatalog.All);
    }

    [Fact]
    public async Task IncompleteSourceBindingIsRejectedBeforeTransport()
    {
        var checkpoints=new AgentTestRuntime().CreateContext().Platform.ExecutionCheckpoints;
        await Assert.ThrowsAsync<ArgumentException>(()=>checkpoints.WriteAsync(new("work","engine-1",0,0,[1],"effect",Guid.NewGuid())));
    }

    [Fact]
    public async Task LargeCheckpointsUseBoundedChunksAndVerifyCompleteDigests()
    {
        var engine=new byte[5*1024*1024]; var source=new byte[8*1024*1024];
        System.Security.Cryptography.RandomNumberGenerator.Fill(engine);
        System.Security.Cryptography.RandomNumberGenerator.Fill(source);
        static string Digest(byte[] bytes)=>Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
        var chunks=new Dictionary<string,SortedDictionary<int,byte[]>>();
        var writes=0; var reads=0;
        var json=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var runtime=new AgentTestRuntime()
            .RegisterCapability<JsonElement,JsonElement>(ExecutionCheckpointCapabilities.Write,(request,_)=>
            {
                Assert.True(System.Text.Encoding.UTF8.GetByteCount(request.GetRawText())<6*1024*1024);
                var mode=request.GetProperty("mode").GetString();
                if(mode=="stage")
                {
                    var stage=request.Deserialize<ExecutionCheckpointChunkWrite>(json)!;
                    if(!chunks.TryGetValue(stage.Digest,out var archive)) chunks[stage.Digest]=archive=[];
                    archive.Add(stage.Offset,stage.Data); writes++;
                    return Task.FromResult(JsonSerializer.SerializeToElement(new ExecutionCheckpointChunkReceipt(true),json));
                }
                var commit=request.Deserialize<ExecutionCheckpointCommit>(json)!;
                Assert.Equal(engine,chunks[commit.EngineDigest].Values.SelectMany(x=>x).ToArray());
                Assert.Equal(source,chunks[commit.SourceDigest!].Values.SelectMany(x=>x).ToArray());
                return Task.FromResult(JsonSerializer.SerializeToElement(new ExecutionCheckpointCommitReceipt(1),json));
            })
            .RegisterCapability<JsonElement,JsonElement>(ExecutionCheckpointCapabilities.Read,(request,_)=>
            {
                if(!request.TryGetProperty("mode",out var mode))
                    return Task.FromResult(JsonSerializer.SerializeToElement(new ExecutionCheckpointReadResult(null,
                        new(1,9,Digest(engine),engine.Length,Digest(source),source.Length,null,null)),json));
                var chunk=request.Deserialize<ExecutionCheckpointChunkRead>(json)!;
                var archive=chunk.Blob=="engine"?engine:source;
                reads++;
                return Task.FromResult(JsonSerializer.SerializeToElement(new ExecutionCheckpointChunk(archive.AsSpan(chunk.Offset,
                    Math.Min(ExecutionCheckpointTransfer.ChunkBytes,archive.Length-chunk.Offset)).ToArray()),json));
            });
        var checkpoints=runtime.CreateContext().Platform.ExecutionCheckpoints;
        var saved=await checkpoints.WriteAsync(new("large","engine-1",0,9,engine,"large-write",SourceArchive:source));
        var loaded=await checkpoints.ReadAsync(new("large","engine-1"));
        Assert.Equal(engine,loaded!.EngineArchive); Assert.Equal(source,loaded.SourceArchive);
        Assert.Equal(saved.EngineDigest,loaded.EngineDigest); Assert.Equal(4,writes); Assert.Equal(4,reads);
    }
}
