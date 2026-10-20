namespace CSweet.Agent.SDK;

/// <summary>Private engine state, separate from publishable source. Checkpoints never grant execution authority.</summary>
public sealed class PlatformExecutionCheckpointClient
{
    private readonly PlatformCapabilityClient _platform;
    internal PlatformExecutionCheckpointClient(PlatformCapabilityClient platform) => _platform = platform;

    public async Task<ExecutionCheckpoint?> ReadAsync(ExecutionCheckpointRead request, CancellationToken token = default)
    {
        Validate(request.SessionKey, request.EngineVersion);
        var result = await _platform.InvokeAsync<ExecutionCheckpointRead, ExecutionCheckpointReadResult>(ExecutionCheckpointCapabilities.Read, request, token);
        if (result.Transfer is not { } transfer) return result.Checkpoint;
        var engine = await DownloadAsync(request, transfer.Revision, "engine", transfer.EngineLength, transfer.EngineDigest, token);
        var source = transfer.SourceDigest is { } digest
            ? await DownloadAsync(request, transfer.Revision, "source", transfer.SourceLength, digest, token) : null;
        return new(request.SessionKey,request.EngineVersion,transfer.Revision,transfer.EventCursor,engine,transfer.EngineDigest,
            transfer.WorkspaceId,transfer.AssignmentRevision,source,transfer.SourceDigest);
    }

    public async Task<ExecutionCheckpoint> WriteAsync(ExecutionCheckpointWrite request, CancellationToken token = default)
    {
        Validate(request.SessionKey, request.EngineVersion);
        if (request.ExpectedRevision < 0 || request.EventCursor < 0 || request.EngineArchive is not { Length: > 0 and <= ExecutionCheckpointTransfer.MaximumArchiveBytes } ||
            request.SourceArchive is { Length: > ExecutionCheckpointTransfer.MaximumArchiveBytes } ||
            string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 160 ||
            (request.WorkspaceId is null) != (request.AssignmentRevision is null) ||
            request.WorkspaceId is not null && request.SourceArchive is null || request.AssignmentRevision is < 1)
            throw new ArgumentException("A bounded engine archive, revision, idempotency key and coherent optional source binding are required.");
        if (request.EngineArchive.Length + (request.SourceArchive?.Length ?? 0) <= ExecutionCheckpointTransfer.InlineBytes)
            return await _platform.InvokeAsync<ExecutionCheckpointWrite, ExecutionCheckpoint>(ExecutionCheckpointCapabilities.Write, request, token);
        var engineDigest = await UploadAsync(request, request.EngineArchive, token);
        var sourceDigest = request.SourceArchive is { } source ? await UploadAsync(request, source, token) : null;
        var committed = await _platform.InvokeAsync<ExecutionCheckpointCommit,ExecutionCheckpointCommitReceipt>(ExecutionCheckpointCapabilities.Write,
            new("commit",request with { EngineArchive=[],SourceArchive=null },engineDigest,sourceDigest),token);
        return new(request.SessionKey,request.EngineVersion,committed.Revision,request.EventCursor,request.EngineArchive,engineDigest,
            request.WorkspaceId,request.AssignmentRevision,request.SourceArchive,sourceDigest);
    }

    private async Task<string> UploadAsync(ExecutionCheckpointWrite request, byte[] archive, CancellationToken token)
    {
        if (archive.Length > ExecutionCheckpointTransfer.MaximumArchiveBytes) throw new ArgumentException("Checkpoint archive exceeds its bound.");
        var digest=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(archive));
        for(var offset=0;offset<archive.Length;offset+=ExecutionCheckpointTransfer.ChunkBytes)
            await _platform.InvokeAsync<ExecutionCheckpointChunkWrite,ExecutionCheckpointChunkReceipt>(ExecutionCheckpointCapabilities.Write,
                new("stage",request.SessionKey,request.EngineVersion,digest,offset,archive.Length,
                    archive.AsSpan(offset,Math.Min(ExecutionCheckpointTransfer.ChunkBytes,archive.Length-offset)).ToArray(),request.WorkspaceId,request.AssignmentRevision),token);
        return digest;
    }

    private async Task<byte[]> DownloadAsync(ExecutionCheckpointRead request,long revision,string blob,int length,string digest,CancellationToken token)
    {
        if(length is < 1 or > ExecutionCheckpointTransfer.MaximumArchiveBytes) throw new InvalidDataException("Invalid checkpoint transfer length.");
        var archive=new byte[length];
        for(var offset=0;offset<length;offset+=ExecutionCheckpointTransfer.ChunkBytes)
        {
            var chunk=await _platform.InvokeAsync<ExecutionCheckpointChunkRead,ExecutionCheckpointChunk>(ExecutionCheckpointCapabilities.Read,
                new("chunk",request.SessionKey,request.EngineVersion,revision,blob,offset),token);
            var expected=Math.Min(ExecutionCheckpointTransfer.ChunkBytes,length-offset);
            if(chunk.Data.Length!=expected) throw new InvalidDataException("Checkpoint chunk length changed.");
            chunk.Data.CopyTo(archive,offset);
        }
        if(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(archive))!=digest) throw new InvalidDataException("Checkpoint digest mismatch.");
        return archive;
    }

    private static void Validate(string key, string version)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 160 || string.IsNullOrWhiteSpace(version) || version.Length > 100)
            throw new ArgumentException("A session key and engine format version are required.");
    }
}

public static class ExecutionCheckpointCapabilities
{
    public const string Read = CapabilityNames.ExecutionCheckpoint.Read;
    public const string Write = CapabilityNames.ExecutionCheckpoint.Write;
}

public sealed record ExecutionCheckpointRead(string SessionKey, string EngineVersion);
public sealed record ExecutionCheckpointReadResult(ExecutionCheckpoint? Checkpoint, ExecutionCheckpointTransferMetadata? Transfer = null);
public sealed record ExecutionCheckpointWrite(string SessionKey, string EngineVersion, long ExpectedRevision,
    long EventCursor, byte[] EngineArchive, string IdempotencyKey,
    Guid? WorkspaceId = null, long? AssignmentRevision = null, byte[]? SourceArchive = null);
public sealed record ExecutionCheckpoint(string SessionKey, string EngineVersion, long Revision, long EventCursor,
    byte[] EngineArchive, string EngineDigest, Guid? WorkspaceId = null, long? AssignmentRevision = null,
    byte[]? SourceArchive = null, string? SourceDigest = null);

/// <summary>Bounded wire transfers used by the typed client; never model tools or execution grants.</summary>
public static class ExecutionCheckpointTransfer
{
    public const int ChunkBytes=4*1024*1024;
    public const int InlineBytes=4*1024*1024;
    public const int MaximumArchiveBytes=16*1024*1024;
}
public sealed record ExecutionCheckpointTransferMetadata(long Revision,long EventCursor,string EngineDigest,int EngineLength,
    string? SourceDigest,int SourceLength,Guid? WorkspaceId,long? AssignmentRevision);
public sealed record ExecutionCheckpointChunkWrite(string Mode,string SessionKey,string EngineVersion,string Digest,int Offset,int TotalLength,
    byte[] Data,Guid? WorkspaceId,long? AssignmentRevision);
public sealed record ExecutionCheckpointChunkReceipt(bool Stored);
public sealed record ExecutionCheckpointCommit(string Mode,ExecutionCheckpointWrite Request,string EngineDigest,string? SourceDigest);
public sealed record ExecutionCheckpointCommitReceipt(long Revision);
public sealed record ExecutionCheckpointChunkRead(string Mode,string SessionKey,string EngineVersion,long Revision,string Blob,int Offset);
public sealed record ExecutionCheckpointChunk(byte[] Data);
