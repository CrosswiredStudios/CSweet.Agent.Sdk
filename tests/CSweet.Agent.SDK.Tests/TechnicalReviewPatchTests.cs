namespace CSweet.Agent.SDK.Tests;

public sealed class TechnicalReviewPatchTests
{
    [Fact]
    public void QuotedRenameModeChangesAndGiantLinesRetainExactFileAndOffsetCoverage()
    {
        const string rename = "diff --git \"a/old name\" \"b/new name\"\nsimilarity index 100%\nrename from old name\nrename to new name\n";
        const string mode = "diff --git a/script b/script\nold mode 100644\nnew mode 100755\n";
        const string header = "diff --git a/large.js b/large.js\n--- a/large.js\n+++ b/large.js\n@@ -0,0 +1,2 @@\n+";
        var large = header + new string('x', 23999 - header.Length) + char.ConvertFromUtf32(0x1F3AE) +
            new string('x', 8000) + "\n+diff --git a/fake b/fake\n";
        var patch = rename + large + mode;
        var parts = TechnicalReviewPatch.Sections(patch).ToArray();
        Assert.Equal(patch, string.Concat(parts.Select(p => p.Text)));
        Assert.Equal(4, parts.Length);
        Assert.Equal("\"a/old name\" \"b/new name\"", parts[0].File);
        Assert.Equal(parts[1].FileHash, parts[2].FileHash);
        Assert.Equal("@@ -0,0 +1,2 @@", parts[2].Hunk);
        Assert.False(char.IsHighSurrogate(parts[1].Text[^1]));
        Assert.Equal(rename.Length, parts[1].Offset);
        Assert.Equal(rename.Length + large.Length, parts[3].Offset);
        Assert.Equal(4, parts.Select(p => p.CacheKey).Distinct().Count());
    }

    [Fact]
    public void FileCacheKeysSurviveOffsetShiftsButNotChangesElsewhereInTheSameFile()
    {
        const string head = "diff --git a/code b/code\n@@ -1 +1 @@\n";
        var file = head + "+" + new string('x', 34000);
        var original = TechnicalReviewPatch.Sections(file).Last();
        var moved = TechnicalReviewPatch.Sections("diff --git a/first b/first\n+new\n" + file).Last();
        var changed = TechnicalReviewPatch.Sections(file.Replace("+xxx", "+yxx")).Last();
        Assert.NotEqual(original.Offset, moved.Offset);
        Assert.Equal(original.CacheKey, moved.CacheKey);
        Assert.Equal(original.Text, changed.Text);
        Assert.NotEqual(original.CacheKey, changed.CacheKey);
    }
}
