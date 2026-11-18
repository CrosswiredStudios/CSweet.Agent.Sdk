using System.Text;

namespace CSweet.Agent.SDK;

internal sealed record ReviewSection(string File, string FileHash, int FileOffset, int Offset, string Hunk, string Text)
{
    public string CacheKey => $"{FileHash}:{FileOffset}";
}

internal static class TechnicalReviewPatch
{
    // Keep bounded transport independent of the amount of one file the reviewer sees.
    private const int SectionLimit = 24000;
    private const string FileStart = "diff --git ";

    internal static async Task<(string Text, int Pages)> ReadAsync(GitMergeReview reference,
        Func<GitReviewDiffRead, CancellationToken, Task<GitMergeReview>> read, CancellationToken ct)
    {
        var patch = new StringBuilder();
        int? total = null;
        for (var number = 0; number < 512; number++)
        {
            var offset = patch.Length;
            var current = await read(new(reference.PublicationId, reference.CandidateCommitSha,
                reference.TargetCommitSha!, reference.PatchDigest!, offset), ct);
            var page = current.DiffPage ?? throw new InvalidOperationException("The platform returned no bounded diff page.");
            if (current.PublicationId != reference.PublicationId || current.CandidateCommitSha != reference.CandidateCommitSha ||
                current.TargetCommitSha != reference.TargetCommitSha || current.PatchDigest != reference.PatchDigest ||
                page.PatchDigest != reference.PatchDigest || page.Offset != offset || page.Text.Length is < 1 or > 12000 ||
                page.TotalCharacters < offset + page.Text.Length || total.HasValue && total != page.TotalCharacters ||
                page.NextOffset != (offset + page.Text.Length < page.TotalCharacters ? offset + page.Text.Length : (int?)null))
                throw new InvalidOperationException("Review diff coverage is incomplete, overlapping or bound to a changed candidate.");
            total ??= page.TotalCharacters;
            patch.Append(page.Text);
            if (page.NextOffset.HasValue) continue;
            var text = patch.ToString();
            if (BranchDiffTechnicalReview.Hash(text) != reference.PatchDigest)
                throw new InvalidOperationException("The complete review patch differs from its published SHA-256 digest.");
            return (text, number + 1);
        }
        throw new InvalidOperationException("Technical review exceeds the platform limit of 512 diff pages.");
    }

    internal static IEnumerable<ReviewSection> Sections(string patch)
    {
        var start = 0;
        while (start < patch.Length)
        {
            var next = patch.IndexOf("\n" + FileStart, start, StringComparison.Ordinal);
            var end = next < 0 ? patch.Length : next + 1;
            var text = patch[start..end];
            // Retain the literal Git header, including quoting and both names on renames.
            var gitFile = text.StartsWith(FileStart, StringComparison.Ordinal);
            var headerEnd = text.IndexOf('\n');
            var file = gitFile ? text[FileStart.Length..(headerEnd < 0 ? text.Length : headerEnd)].TrimEnd('\r') : "(patch)";
            var fileHash = BranchDiffTechnicalReview.Hash(text);
            var limit = gitFile ? SectionLimit : 12000;
            var hunk = "";
            for (var offset = 0; offset < text.Length;)
            {
                var length = Math.Min(limit, text.Length - offset);
                if (offset + length < text.Length)
                {
                    // Prefer a nearby hunk boundary, then a complete line. A giant single line still advances.
                    var hunkBreak = text.LastIndexOf("\n@@ ", offset + length - 1, length, StringComparison.Ordinal);
                    var lineBreak = text.LastIndexOf('\n', offset + length - 1, length);
                    if (hunkBreak >= offset + length / 2) length = hunkBreak + 1 - offset;
                    else if (lineBreak >= offset + length / 2) length = lineBreak + 1 - offset;
                    if (char.IsHighSurrogate(text[offset + length - 1]) && char.IsLowSurrogate(text[offset + length])) length--;
                }
                var section = text.Substring(offset, length);
                yield return new(file, fileHash, offset, start + offset, hunk, section);
                var last = section.LastIndexOf("\n@@ ", StringComparison.Ordinal);
                var at = last >= 0 ? last + 1 : section.StartsWith("@@ ", StringComparison.Ordinal) ? 0 : -1;
                if (at >= 0)
                {
                    var close = section.IndexOf(" @@", at + 3, StringComparison.Ordinal);
                    if (close >= 0 && close - at < 100) hunk = section[at..(close + 3)];
                }
                offset += length;
            }
            start = end;
        }
    }
}
