using System.Security.Cryptography;
using System.Text.Json;
using CSweet.Agent.SDK.Compute;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK.Tests;

public sealed class DeliveryNodeDependenciesTests
{
    [Theory]
    [InlineData("https://registry.npmjs.org/phaser/-/phaser-3.90.0.tgz", true)]
    [InlineData("http://registry.npmjs.org/phaser/-/phaser-3.90.0.tgz", false)]
    [InlineData("https://127.0.0.1/a.tgz", false)]
    [InlineData("https://registry.npmjs.org.evil.test/a.tgz", false)]
    [InlineData("file:///tmp/a.tgz", false)]
    [InlineData("https://user:secret@registry.npmjs.org/a.tgz", false)]
    public void Lockfile_keeps_exact_registry_and_integrity(string url, bool accepted)
    {
        var integrity = "sha512-" + Convert.ToBase64String(SHA512.HashData([1,2,3]));
        var archive = DeliveryComputeTestTests.NodeArchive(JsonSerializer.Serialize(new {lockfileVersion=3,packages=new Dictionary<string,object>{["node_modules/phaser"]=new{resolved=url,integrity}}}));
        if(!accepted) {Assert.Throws<InvalidDataException>(()=>DeliveryNodeDependencies.Read(archive));return;}
        var dependencies=DeliveryNodeDependencies.Read(archive);
        Assert.Equal(new ComputeNodePackage(url,integrity),Assert.Single(dependencies.Packages));
        Assert.Equal(64,dependencies.LockfileSha256.Length);
    }
    [Theory]
    [InlineData("{}")] [InlineData("{bad")]
    [InlineData("{\"lockfileVersion\":1,\"packages\":{}}")]
    [InlineData("{\"lockfileVersion\":3,\"packages\":{\"node_modules/a\":{\"link\":true}}}")]
    public void Unsupported_or_unpinned_dependency_sets_fail_closed(string json) =>
        Assert.Throws<InvalidDataException>(()=>DeliveryNodeDependencies.Read(DeliveryComputeTestTests.NodeArchive(json)));

    [Fact]
    public void Installation_does_not_supply_readiness_and_node_build_uses_prepared_guest_dependencies()
    {
        var archive = DeliveryComputeTestTests.NodeArchive();
        var dependencies=DeliveryNodeDependencies.Read(archive);var hash=Convert.ToHexStringLower(SHA256.HashData(archive));
        const string root="/var/lib/csweet-compute/work/review-abcd";
        var restore=DeliveryComputeTest.RestoreScript(root,hash,dependencies.LockfileSha256);
        Assert.Contains("npm ci --offline --cache /npm-cache",restore);Assert.Contains(dependencies.LockfileSha256,restore);
        Assert.DoesNotContain("csweet-candidate:",restore);
        var script=DeliveryComputeTest.ValidationScript(root,new(Guid.NewGuid(),"release","main",new string('a',40),new string('b',40),new string('c',40)),hash,"node",Guid.NewGuid());
        Assert.Contains("npm test && npm run build",script);Assert.Contains("source/dist/index.html",script);
        Assert.Contains("csweet-candidate:",script);Assert.DoesNotContain("--network=host",script);
    }
}
