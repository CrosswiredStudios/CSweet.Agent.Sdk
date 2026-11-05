using System.Security.Cryptography;
using System.Text;
using CSweet.Agent.SDK.Compute;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK.Tests;

public sealed class DeliveryComputeLegacyRecipeTests
{
    [Theory]
    [InlineData("missing",true)] [InlineData("defined",false)] [InlineData("changed-source",false)]
    [InlineData("changed-command",false)] [InlineData("unknown",false)] [InlineData("wrong-request",false)]
    [InlineData("truncated",false)] [InlineData("real-test-failure",false)] [InlineData("wrong-mode",false)]
    public void OnlyTheExactKnownMissingOptionalScriptQualifies(string scenario,bool expected)
    {
        var source=DeliveryComputeTestTests.NodeArchive(withTest:scenario=="defined");
        var hash=Convert.ToHexStringLower(SHA256.HashData(source));var digest=new string('d',64);var plan=Guid.NewGuid();
        var repository=new WorkDeliveryRepositoryCandidate(Guid.NewGuid(),"release","main",new('a',40),new('b',40),new('c',40));
        var script=DeliveryComputeTest.ValidationScript($"/var/lib/csweet-compute/work/review-{repository.RepositoryId:N}-{digest[..16]}",repository,hash,"node",plan)
            .Replace("npm test --if-present && npm run build","npm test && npm run build",StringComparison.Ordinal);
        if(scenario=="changed-command")script+="\necho changed";
        var pending=new ExecuteComputeCommandRequest(Guid.NewGuid(),6,"retained",new(Guid.NewGuid(),"/bin/sh","/var/lib/csweet-compute/work",["-c",script]));
        var result=new ComputeCommandResult(scenario=="wrong-request"?Guid.NewGuid():pending.Command.RequestId,scenario=="unknown"?null:1,false,[],
            Encoding.UTF8.GetBytes(scenario=="real-test-failure"?"assertion failed":"npm error Missing script: \"test\"\n"),Truncated:scenario=="truncated",ErrorCode:scenario=="unknown"?"outcome-unknown":null);
        var state=new DeliveryComputeTest.State(ArchiveHash:scenario=="changed-source"?new('0',64):hash,Mode:scenario=="wrong-mode"?"static":"node",Networked:true);
        Assert.Equal(expected,DeliveryComputeTest.CanResumeLegacyMissingTest(state,pending,result,source,repository,digest,plan));
    }
}
