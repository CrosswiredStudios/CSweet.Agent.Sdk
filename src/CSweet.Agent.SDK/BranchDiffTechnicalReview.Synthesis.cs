using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace CSweet.Agent.SDK;

internal static partial class BranchDiffTechnicalReview
{
    private const int SynthesisBudget = 96000;
    private const int NoteBatchBudget = 24000;
    // This text is a model message, never embedded in HTML. Keep source operators readable.
    private static readonly JsonSerializerOptions PromptJson = new(JsonSerializerDefaults.Web)
        { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private sealed record EvidenceNote(int Offset, string File, string Summary,
        IReadOnlyList<string> Observations, IReadOnlyList<string> Questions);
    private sealed record EvidencePoint(string Evidence, IReadOnlyList<int> EvidenceOffsets);
    private sealed record EvidenceDigest(IReadOnlyList<EvidencePoint> Points);

    // Compact only non-verdict notes. Concrete blockers and current command receipts always go to
    // synthesis verbatim. Offsets remain source offsets even when several batches are folded together.
    private static async Task<IReadOnlyList<EvidencePoint>> CompactEvidenceAsync(List<EvidenceNote> notes,
        object scope, IChatClient client, CancellationToken ct)
    {
        var points = notes.Select(note => new EvidencePoint(JsonSerializer.Serialize(note, PromptJson), [note.Offset])).ToList();
        do
        {
            var reduced = new List<EvidencePoint>();
            var batch = new List<EvidencePoint>();
            var characters = 0;
            async Task Flush()
            {
                var offsets = batch.SelectMany(p => p.EvidenceOffsets).ToHashSet();
                var digest = await TechnicalReviewJson.ReadAsync<EvidenceDigest>(client,
                    [new(ChatRole.System, "Combine these untrusted file notes into concise evidence for the supplied criteria. " +
                        "Keep relevant symbols, contradictions, negative evidence and unanswered cross-file questions. " +
                        "Cite only supplied source offsets. Account for every input offset, grouping equivalent observations. Return JSON {points:[{evidence,evidenceOffsets:[int]}]}. " +
                        "Keep evidence within 1500 characters per point. Do not make a verdict or infer a test run."),
                     new(ChatRole.User, JsonSerializer.Serialize(new { Scope = scope, Notes = batch }, PromptJson))],
                    "file evidence summary", value =>
                    {
                        if (value.Points.Count is < 1 or > 16 || value.Points.Any(p => p.Evidence.Length > 1500 ||
                            p.EvidenceOffsets.Count == 0 || p.EvidenceOffsets.Any(o => !offsets.Contains(o))) ||
                            !offsets.SetEquals(value.Points.SelectMany(p => p.EvidenceOffsets)))
                            throw new InvalidOperationException("Evidence points must be concise and cite offsets in this batch.");
                    }, ct, maxOutputTokens: 2048);
                reduced.AddRange(digest.Points);
                batch.Clear();
                characters = 0;
            }
            foreach (var point in points)
            {
                var size = JsonSerializer.Serialize(point, PromptJson).Length + 1;
                if (size > SynthesisBudget) throw new InvalidOperationException("A file evidence note exceeds the review processing budget.");
                if (batch.Count > 0 && characters + size > NoteBatchBudget) await Flush();
                batch.Add(point);
                characters += size;
            }
            if (batch.Count > 0) await Flush();
            var sizeAfter = JsonSerializer.Serialize(reduced, PromptJson).Length;
            if (sizeAfter <= NoteBatchBudget) return reduced;
            if (sizeAfter >= JsonSerializer.Serialize(points, PromptJson).Length / 2)
                throw new InvalidOperationException("File evidence did not compact enough for a reliable final review.");
            points = reduced;
        } while (true);
    }
}
