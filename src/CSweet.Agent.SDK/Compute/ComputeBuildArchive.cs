namespace CSweet.Agent.SDK.Compute;

public sealed record ComputeBuildInputPart(string SourceCommitSha, string BuildSha256, int Length, int Offset, byte[] Content);
public sealed record ComputeBuildArchiveReceipt(string SourceCommitSha, string BuildSha256, int Length);

public sealed record ComputeBuildArchiveInput(string SourceCommitSha, string BuildSha256, int Length, IReadOnlyList<string> PartSha256);
