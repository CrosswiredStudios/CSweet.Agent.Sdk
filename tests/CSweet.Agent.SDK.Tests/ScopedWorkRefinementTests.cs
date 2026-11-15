using System.Text.Json;
using CSweet.WorkManagement.Contracts;
namespace CSweet.Agent.SDK.Tests;

public sealed class ScopedWorkRefinementTests
{
    [Theory]
    [InlineData("foreign-role")]
    [InlineData("parent")]
    [InlineData("lost-criterion")]
    public void InvalidRefinementCannotRewriteResponsibilitiesOrChooseAnUnavailableRole(string condition)
    {
        var input = Request(); var draft = Draft();
        var items = draft.Items.ToArray();
        if (condition == "foreign-role") items[2] = items[2] with { RoleKey = "invented-specialist" };
        if (condition == "parent") items[2] = items[2] with { ParentIndex = 0 };
        if (condition == "lost-criterion") items[2] = items[2] with { AcceptanceCriteria = ["Weakened check"] };
        Assert.Throws<AgentModelJsonException>(() => ScopedWorkRefinement.Validate(draft with { Items = items }, input));
    }
    [Fact]
    public async Task InterruptedPublicationReplaysTheSameAdditiveWorkAndSourceChangesStopTheNextEffect()
    {
        var input = Request(); var draft = Draft(); var manager = Guid.NewGuid(); var source = input.Source.RevisionId;
        var created = new Dictionary<string,WorkItem>(); var lost = true;
        var runtime = new AgentTestRuntime()
            .RegisterCapability<WorkBoardReference,WorkBoardDetail>(WorkItemCapabilities.Read, (_,_) => Task.FromResult(
                JsonSerializer.Deserialize<WorkBoardDetail>("{}")! with { Board = JsonSerializer.Deserialize<WorkBoardSummary>("{}")! with {
                    Id = input.BoardId, WorkstreamId = input.WorkstreamId, ManagerOrganizationUserId = manager }, Items = created.Values.ToArray() }))
            .RegisterCapability<object,ArtifactDocument>(PlatformCapabilities.ArtifactRead, (_,_) => Task.FromResult(
                new ArtifactDocument(input.Source.DocumentId,"Approved game","scope","Accepted",source,null,source,null,null,
                    [new(input.Source.RevisionId,1,null,"Approved player outcome and accessibility constraint",input.Source.Digest,"Accepted",DateTimeOffset.UtcNow,null,null)]) { WorkstreamId = input.WorkstreamId }))
            .RegisterCapability<CreateWorkItemRequest,WorkItem>(WorkItemCapabilities.Create, (r,_) => {
                if (!created.TryGetValue(r.IdempotencyKey, out var item)) created[r.IdempotencyKey] = item = JsonSerializer.Deserialize<WorkItem>("{}")! with {
                    Id = Guid.NewGuid(), Kind = r.Kind, Title = r.Title, ParentItemId = r.ParentItemId, Planning = r.Planning, Status = "Backlog" };
                if (lost) { lost = false; throw new IOException("Lost response after the first platform acceptance"); }
                return Task.FromResult(item);
            });
        var context = runtime.CreateContext(identity: new(manager.ToString(),"Owner",null,null,null,[],null,null,null));
        await Assert.ThrowsAsync<IOException>(() => ScopedWorkRefinement.PublishAsync(input,draft,"approved-proposal",context,default));
        var ids = await ScopedWorkRefinement.PublishAsync(input,draft,"approved-proposal",context,default);
        Assert.Equal(3, created.Count); Assert.Equal(3, ids.Distinct().Count());
        Assert.Equal(ids[0], created.Values.Single(i => i.Kind == "Story").ParentItemId);
        source = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() => ScopedWorkRefinement.PublishAsync(input,draft,"next-proposal",context,default));
        Assert.Equal(3, created.Count);
    }
    internal static WorkRefinementRequest Request() => new(Guid.NewGuid(), Guid.NewGuid(), "Deliver the remaining approved outcome",
        new(Guid.NewGuid(), Guid.NewGuid(), "approved-source"), [new("Epic","general.epic.v1",false,null),new("Story","general.story.v1",false,"Epic"),new("Task","general.task.v1",true,"Story")],
        [new("third-party-capable-author","Artifact","author")], ["Preserve the approved exclusions"]);
    internal static WorkRefinementDraft Draft() => new("Add the missing approved outcome without rewriting completed history", [
        new("Epic","Outcome","Approved future outcome",["Approved behavior works"],null,[],null),
        new("Story","Approaching outcome","A coherent independently verifiable increment",["Approved behavior works"],0,[],null),
        new("Task","Deliver outcome","Implementation and exact platform evidence",["Approved behavior works"],1,[],"third-party-capable-author")]);
}

