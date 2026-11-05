using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using CSweet.Agent.SDK.Compute;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK;

public static partial class DeliveryComputeTest
{
    internal static bool CanResumeLegacyMissingTest(State state,ExecuteComputeCommandRequest pending,
        ComputeCommandResult failed,byte[] source,WorkDeliveryRepositoryCandidate repository,string candidateDigest,Guid planId)
    {
        if(state.ArchiveHash is null || state.Mode!="node" || state.Networked!=true ||
            failed is not {ExitCode:1,TimedOut:false,Truncated:false,ErrorCode:null} ||
            failed.RequestId!=pending.Command.RequestId ||
            !failed.StandardErrorText.StartsWith("npm error Missing script: \"test\"",StringComparison.Ordinal) ||
            Convert.ToHexStringLower(SHA256.HashData(source))!=state.ArchiveHash ||
            pending.Command.Executable!="/bin/sh" || pending.Command.WorkingDirectory!="/var/lib/csweet-compute/work")return false;
        var root=$"/var/lib/csweet-compute/work/review-{repository.RepositoryId:N}-{candidateDigest[..16]}";
        var legacy=ValidationScript(root,repository,state.ArchiveHash,"node",planId)
            .Replace("npm test --if-present && npm run build","npm test && npm run build",StringComparison.Ordinal);
        if(!pending.Command.Arguments.SequenceEqual(new[]{"-c",legacy}))return false;
        try
        {
            using var zip=new ZipArchive(new MemoryStream(source),ZipArchiveMode.Read);
            if(zip.GetEntry("package.json") is not {} package)return false;
            using var stream=package.Open();using var json=JsonDocument.Parse(stream);
            return json.RootElement.TryGetProperty("scripts",out var scripts) && scripts.ValueKind==JsonValueKind.Object &&
                scripts.TryGetProperty("build",out var build) && build.ValueKind==JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(build.GetString()) && !scripts.TryGetProperty("test",out _);
        }
        catch(Exception error) when(error is InvalidDataException or JsonException){return false;}
    }
}
