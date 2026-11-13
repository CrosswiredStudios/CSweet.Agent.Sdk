using System.Text.Json;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK.Tests;

/// <summary>
/// Super Awesome Games #22 (2026-11-10): the reviewer approved the code but rejected the ticket because the browser
/// check lived only in the author's narrative. A declared requirement is now checked in code before any model call.
/// </summary>
public sealed class DeclaredVerificationReviewTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string Source = new('a', 40), Target = new('b', 40), Digest = new('c', 64);
    private static readonly WorkItemVerificationRequirement Canvas = new(WorkItemVerificationKinds.BrowserSmoke) { RequireCanvas = true };

    private static GitMergeReview Reference(params GitValidationResult[] evidence) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Game", Source, null, "", [], [], "AwaitingValidation")
        { TargetCommitSha = Target, TargetBranch = "story", PatchDigest = Digest, ChangedFiles = ["src/GameScene.js"], ImplementationEvidence = evidence };

    private static WorkItemPlanningSpecification Planning(IReadOnlyList<WorkItemVerificationRequirement>? verifications) =>
        new(["Break blocks"], ["Ball hits and breaks a block"]) { Verifications = verifications };

    private static Task<GitMergeReview> Read(GitMergeReview reference, GitReviewDiffRead read) =>
        Task.FromResult(reference with { DiffPage = new(Digest, read.Offset, 100, new string('x', 100), null) });

    private static string Respond(JsonElement input) => input.TryGetProperty("page", out _)
        ? JsonSerializer.Serialize(new BranchDiffTechnicalReview.PageContent("Reviewed", [], [], []), Json)
        : JsonSerializer.Serialize(new BranchDiffTechnicalReview.FinalContent(true, "Approved", [], [],
            [new("Ball hits and breaks a block", true, "Collision handler destroys the block", [0])]), Json);

    [Fact]
    public async Task A_missing_declared_receipt_is_rejected_without_calling_the_model()
    {
        var reference = Reference(new GitValidationResult("npm test", true, 0));
        var client = new BranchDiffTechnicalReviewTests.Client((_, input) => Respond(input));

        var result = await BranchDiffTechnicalReview.ExecuteAsync(reference, Planning([Canvas]), (read, _) => Read(reference, read), client, default);

        Assert.False(result.Approved);
        Assert.Equal(0, client.Calls);
        var finding = Assert.Single(result.Findings);
        Assert.Contains("node .csweet/browser-smoke.cjs <build directory> --canvas", finding);
    }

    [Fact]
    public async Task A_bound_receipt_satisfies_the_declaration_and_tells_the_model()
    {
        var reference = Reference(new GitValidationResult("npm test", true, 0), new GitValidationResult("node .csweet/browser-smoke.cjs dist --canvas", true, 0));
        string? note = null;
        var client = new BranchDiffTechnicalReviewTests.Client((_, input) =>
        {
            note ??= input.GetProperty("reference").GetProperty("declaredVerification").GetString();
            return Respond(input);
        });

        var result = await BranchDiffTechnicalReview.ExecuteAsync(reference, Planning([Canvas]), (read, _) => Read(reference, read), client, default);

        Assert.True(result.Approved);
        Assert.Contains("satisfied by the bound receipt `node .csweet/browser-smoke.cjs dist --canvas`", note);
    }

    [Fact]
    public void A_declared_none_tells_the_model_not_to_require_a_browser_and_legacy_tickets_get_no_note()
    {
        var none = BranchDiffTechnicalReview.DeclaredVerification(Planning([]), []);
        Assert.Null(none.MissingReceipt);
        Assert.Contains("needs no browser verification", none.Note);

        var legacy = BranchDiffTechnicalReview.DeclaredVerification(Planning(null), []);
        Assert.Null(legacy.Note);
        Assert.Null(legacy.MissingReceipt);
    }

    [Fact]
    public void Declared_interaction_needs_the_shared_scenario_receipt_as_well_as_the_smoke_receipt()
    {
        var interaction = new WorkItemVerificationRequirement(WorkItemVerificationKinds.BrowserInteraction) { CriterionIndexes = [0] };
        var smoke = new GitValidationResult("node .csweet/browser-smoke.cjs dist --canvas", true, 0);

        var missing = BranchDiffTechnicalReview.DeclaredVerification(Planning([Canvas, interaction]), [smoke]);
        Assert.Contains("node .csweet/browser-scenarios.cjs <build directory>", missing.MissingReceipt);

        var failed = BranchDiffTechnicalReview.DeclaredVerification(Planning([Canvas, interaction]),
            [smoke, new("node .csweet/browser-scenarios.cjs dist", false, 1)]);
        Assert.NotNull(failed.MissingReceipt);

        var bound = BranchDiffTechnicalReview.DeclaredVerification(Planning([Canvas, interaction]),
            [smoke, new("node .csweet/browser-scenarios.cjs dist --record notes/observation.md", true, 0)]);
        Assert.Null(bound.MissingReceipt);
        Assert.Contains("satisfied by the bound receipt `node .csweet/browser-smoke.cjs dist --canvas`", bound.Note);
        Assert.Contains("measures its criterion from emitted events", bound.Note);
        Assert.Contains("notes/observation.md", bound.Note);
    }

    [Fact]
    public void A_corrective_ticket_names_every_finding_and_says_the_whole_task_is_reverified()
    {
        var first = Guid.NewGuid();
        var text = DeliveryCorrectionCoordinator.CorrectionDescription([(first, "Needs a root Dockerfile"), (Guid.NewGuid(), new string('x', 900))]);
        Assert.Contains(first.ToString("D") + ": Needs a root Dockerfile", text);
        Assert.Contains("re-verified in full", text);
        Assert.Contains("every original acceptance criterion", text);
        Assert.True(text.Length <= 4000);
    }

    [Fact]
    public void A_failed_or_non_canvas_receipt_does_not_satisfy_a_canvas_requirement()
    {
        Assert.NotNull(BranchDiffTechnicalReview.DeclaredVerification(Planning([Canvas]),
            [new("node .csweet/browser-smoke.cjs dist --canvas", false, 1)]).MissingReceipt);
        Assert.NotNull(BranchDiffTechnicalReview.DeclaredVerification(Planning([Canvas]),
            [new("node .csweet/browser-smoke.cjs dist", true, 0)]).MissingReceipt);
    }
}
