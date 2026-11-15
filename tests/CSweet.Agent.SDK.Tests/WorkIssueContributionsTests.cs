using System.Text.Json;
using CSweet.WorkManagement.Contracts;
namespace CSweet.Agent.SDK.Tests;
public sealed class WorkIssueContributionsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CurrentOwnerSeesARejectionOnceAndFreshRecordsThenContinuesIndependentContributions(bool permanent)
    {
        var owner=Guid.NewGuid();var board=Guid.NewGuid();var doc=Guid.NewGuid();var revision=Guid.NewGuid();
        var source=new WorkApprovedSource(doc,revision,"approved");
        var first=new WorkIssueCommissionResponse(Guid.NewGuid(),board,owner,[],source,"Proposed",null,null,1) {ProposedChanges=[new("collision","Reconcile collision","Current evidence",[])]};
        var second=first with {Id=Guid.NewGuid(),ProposedChanges=[new("audio","Reconcile audio","Independent evidence",[])]};
        var proposals=new[]{first,second};long caseRevision=1;var calls=0;var judgments=0;
        var current=new WorkIssueCaseResponse(Guid.NewGuid(),board,Guid.NewGuid(),"collision","Current canonical collision obligation","Evidence","Open","Working",owner,"Repair the source",source,[],[],null,1,DateTimeOffset.UtcNow);
        var runtime=new AgentTestRuntime()
            .RegisterCapability<ReadWorkIssuesRequest,WorkIssueDirectory>(WorkIssueCapabilities.Read,(_,_)=>Task.FromResult(new WorkIssueDirectory([current with {Revision=caseRevision}],proposals) {CanReconcile=true,AccountableEmployeeId=owner}))
            .RegisterCapability<object,ArtifactDocument>(PlatformCapabilities.ArtifactRead,(_,_)=>Task.FromResult(new ArtifactDocument(doc,"Approved scope","scope","Accepted",revision,null,revision,null,null,
                [new(revision,1,null,"Preserve the approved collision and audio requirements","approved","Accepted",DateTimeOffset.UtcNow,null,null)])))
            .RegisterCapability<DecideWorkIssueProposalRequest,WorkIssueCommissionResponse>(WorkIssueCapabilities.DecideProposal,(r,_)=> {
                calls++;
                if(r.ProposalId==first.Id&&r.Accept&&(permanent||caseRevision==1)) {caseRevision=2;throw new PlatformCapabilityException(WorkIssueCapabilities.DecideProposal,PlatformCapabilityErrorCode.Conflict,"The canonical record changed");}
                if(r.Accept&&r.ProposalId==first.Id)Assert.Equal(2,Assert.Single(r.ReviewedIssues!).Revision);
                var index=Array.FindIndex(proposals,p=>p.Id==r.ProposalId);proposals[index]=proposals[index] with {Status=r.Accept?"Accepted":"Rejected"};
                return Task.FromResult(proposals[index]);
            });
        await WorkIssueContributions.ReviewAsync(board,runtime.CreateContext(identity:new(owner.ToString(),"Owner",null,null,null,[],null,null,null)),(review,_)=> {
            judgments++;
            if(review.Proposal.Id==first.Id&&caseRevision==2){Assert.Equal("The canonical record changed",review.PlatformRejection);Assert.Equal(2,Assert.Single(review.CurrentIssues).Revision);}
            Assert.Contains("approved collision",review.ApprovedSource);return Task.FromResult(new WorkIssueContributionJudgment(true,"Reviewed current evidence and source"));
        },default);
        Assert.Equal(permanent?"Rejected":"Accepted",proposals[0].Status);Assert.Equal("Accepted",proposals[1].Status);
        Assert.Equal(3,judgments);Assert.Equal(permanent?4:3,calls);
    }
}
