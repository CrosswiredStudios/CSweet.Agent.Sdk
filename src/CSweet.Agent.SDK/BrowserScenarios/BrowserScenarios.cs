using System.Text;
using System.Text.Json;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK;

/// <summary>
/// The shared browser scenario contract (SDK 5.25.0). The author, the technical reviewer and independent QA observe
/// interactive behavior with the same runner, on the same build: the page reports events and, for each check, what it
/// expected and what it observed; the runner judges. Observation records carry a generated section produced from the
/// run, so a record's measured values are captured, never written by hand.
/// </summary>
/// <remarks>
/// Super Awesome Games ticket 38 (2026-11-13): the engineer's only browser tool proved the page loaded without
/// exceptions, the page decided its own results from what it intended (a planned brick path, a cap counted only at
/// booking time), and the record transcribed intent. QA, the only role able to observe, found one mismatch per round.
/// </remarks>
public static class BrowserScenarios
{
    /// <summary>The page exposes <c>window.csweetScenarios = { run: async () =&gt; report }</c>.</summary>
    public const string Global = "csweetScenarios";
    public const int ContractVersion = 1;
    public const string RecordBegin = "<!-- csweet:observed:begin -->";
    public const string RecordEnd = "<!-- csweet:observed:end -->";
    /// <summary>Workspace path of the author's runner; matches <see cref="WorkItemVerificationKinds.BrowserScenariosReceiptCommand"/>.</summary>
    public const string RunnerPath = ".csweet/browser-scenarios.cjs";
    /// <summary>Where the runner writes the full capture and screenshot (keep it out of source control).</summary>
    public const string CapturePath = "test-results/browser-scenarios/capture.json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The author's Node runner (real Chromium over CDP, no npm dependency).</summary>
    public static string RunnerScript { get; } = Resource("browser-scenarios-core.js") + "\n" +
        Resource("browser-scenarios-record.js") + "\n" + Resource("browser-scenarios-cdp.js");

    /// <summary>
    /// The independent QA run: the body of a Playwright check script receiving <c>(page, context, origin, check, attach)</c>.
    /// It evaluates with the same code as <see cref="RunnerScript"/> and attaches the full evaluation as <c>scenarios</c>.
    /// </summary>
    public static string PlaywrightScript { get; } = Resource("browser-scenarios-core.js") + "\n" + Resource("browser-scenarios-playwright.js");

    /// <summary>What an author must build for a ticket that declares browser interaction.</summary>
    public const string Guidance =
        "Browser scenario contract (required when the ticket declares browser interaction): the built page exposes " +
        "window.csweetScenarios = { run: async () => report } (you may install it only when the URL has ?csweet-scenarios). " +
        "report = { contract: 1, scenarios: [ { id, events: [ { type, t, data } ], checks: [ { name, criterion, expected, observed, " +
        "comparison, evidence } ] } ] }. Drive each scenario through the real game code (scripted input or physics steps), and " +
        "push an event for everything that actually happens: collisions, hits, sounds started and stopped, state changes, with the " +
        "concrete ids and values the game produced. Each check states the value the criterion requires (expected) and the value " +
        "measured from those emitted events (observed), with comparison equals (default), atMost, atLeast or includes, the " +
        "zero-based acceptance criterion it serves, and evidence: the indexes of the events the observed value was computed from. " +
        "Never compute observed from the plan, the scenario script, configuration or what the code intended; a check whose " +
        "evidence cites no emitted event fails. Every invariant the harness computes must be a check: a value that is computed " +
        "and not checked is a hidden failure. Do not set pass or fail yourself; the runner decides. run() must finish within 10 " +
        "seconds. Then build and call run_browser_scenarios with the build directory and every observation record (markdown) " +
        "this ticket delivers: the runner regenerates each record's measured section between csweet:observed markers from the " +
        "actual run. Call it before check_browser_build, because regenerating a record changes source and an earlier browser " +
        "check would have to be repeated. Write interpretation, uncertainties and reasoning from source outside that section, and never restate a " +
        "measured value there by hand; label reasoning from source as expected from source. Fix the game, or a wrong " +
        "expectation, until every check passes. Independent QA repeats the same run on the same build and compares it with " +
        "the generated section.";

    /// <summary>The note an author gets when planning declares browser interaction for its ticket.</summary>
    public static string AssignmentNote(WorkItemPlanningSpecification? planning)
    {
        if (WorkItemVerifications.BrowserInteraction(planning) is not { } interaction) return "";
        var criteria = string.Join(", ", interaction.CriterionIndexes ?? []);
        return $"This ticket declares browser interaction for acceptance criteria {criteria} (zero-based). Implement the " +
            "browser scenario contract for them and, after your last source edit, build and call run_browser_scenarios. Its receipt (`" +
            WorkItemVerificationKinds.BrowserScenariosReceiptCommand + " <build directory> [--record <path.md>]...`) is added to " +
            "your validations automatically; the technical reviewer requires it and QA repeats the run. " + Guidance;
    }

    /// <summary>The receipt command for a build directory and the observation records the run regenerates.</summary>
    public static string Command(string buildDirectory, IEnumerable<string>? records = null)
    {
        var parts = new List<string> { WorkItemVerificationKinds.BrowserScenariosReceiptCommand, (buildDirectory ?? "").Replace('\\', '/').Trim('/') };
        foreach (var record in records ?? []) parts.AddRange(["--record", (record ?? "").Replace('\\', '/')]);
        var command = string.Join(' ', parts);
        if (!WorkItemVerifications.IsBrowserScenariosReceipt(command, true, 0))
            throw new ArgumentException("Supply a workspace-relative build directory (such as dist) and workspace-relative .md observation records.");
        return command;
    }

    /// <summary>Reads the machine-readable capture from a record's generated section; null when it has none.</summary>
    public static ScenarioCapture? ReadRecord(string markdown)
    {
        var begin = markdown.IndexOf(RecordBegin, StringComparison.Ordinal);
        var end = markdown.IndexOf(RecordEnd, StringComparison.Ordinal);
        if (begin < 0 || end <= begin) return null;
        var block = markdown[begin..end];
        var start = block.IndexOf("```json", StringComparison.Ordinal);
        if (start < 0) return null;
        start = block.IndexOf('\n', start) + 1;
        var stop = block.IndexOf("```", start, StringComparison.Ordinal);
        if (start <= 0 || stop < 0) return null;
        try { return JsonSerializer.Deserialize<ScenarioCapture>(block[start..stop].Trim(), Json); }
        catch (JsonException) { return null; }
    }

    /// <summary>Reads the evaluation the runner or the QA script produced.</summary>
    public static ScenarioCapture? ReadEvaluation(string json)
    {
        try { return JsonSerializer.Deserialize<ScenarioCapture>(json, Json); }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// Differences between an independent run and a recorded one: scenarios, check expectations, observations and
    /// results, and the emitted event sequence (types and data; timings ignored). Empty when they agree.
    /// </summary>
    public static IReadOnlyList<string> Compare(ScenarioCapture independent, ScenarioCapture recorded, int limit = 40)
    {
        var differences = new List<string>();
        void Add(string text) { if (differences.Count < limit) differences.Add(text); }
        var mine = independent.Scenarios.ToDictionary(s => s.Id, StringComparer.Ordinal);
        var theirs = recorded.Scenarios.ToDictionary(s => s.Id, StringComparer.Ordinal);
        foreach (var id in theirs.Keys.Except(mine.Keys)) Add($"Scenario '{id}' is in the record but did not run here.");
        foreach (var id in mine.Keys.Except(theirs.Keys)) Add($"Scenario '{id}' ran here but is not in the record.");
        foreach (var (id, run) in mine)
        {
            if (!theirs.TryGetValue(id, out var record)) continue;
            if (run.Passed != record.Passed) Add($"{id}: {(run.Passed ? "passed" : "failed")} here, {(record.Passed ? "passed" : "failed")} in the record.");
            var checks = record.Checks.ToDictionary(c => c.Name, StringComparer.Ordinal);
            foreach (var check in run.Checks)
            {
                if (!checks.TryGetValue(check.Name, out var other)) { Add($"{id}/{check.Name}: not in the record."); continue; }
                if (Canonical(check.Expected) != Canonical(other.Expected))
                    Add($"{id}/{check.Name}: expected {Canonical(check.Expected)} here, {Canonical(other.Expected)} in the record.");
                if (Canonical(check.Observed) != Canonical(other.Observed))
                    Add($"{id}/{check.Name}: observed {Canonical(check.Observed)} here, {Canonical(other.Observed)} in the record.");
                else if (check.Passed != other.Passed) Add($"{id}/{check.Name}: {(check.Passed ? "passed" : "failed")} here, {(other.Passed ? "passed" : "failed")} in the record.");
            }
            foreach (var name in checks.Keys.Except(run.Checks.Select(c => c.Name), StringComparer.Ordinal))
                Add($"{id}/{name}: in the record but not reported here.");
            var count = Math.Min(run.Events.Count, record.Events.Count);
            for (var i = 0; i < count; i++)
                if (run.Events[i].Type != record.Events[i].Type || Canonical(run.Events[i].Data) != Canonical(record.Events[i].Data))
                {
                    Add($"{id}: event {i} is {run.Events[i].Type} {Canonical(run.Events[i].Data)} here, {record.Events[i].Type} {Canonical(record.Events[i].Data)} in the record.");
                    break;
                }
            var total = run.Events.Count + (run.EventsTruncated ?? 0);
            var recordedTotal = record.Events.Count + (record.EventsTruncated ?? 0);
            if (total != recordedTotal) Add($"{id}: {total} events here, {recordedTotal} in the record.");
        }
        return differences;
    }

    /// <summary>A bounded plain-text summary of a run for a model: contract problems and every failed check.</summary>
    public static string Summarize(ScenarioCapture capture, int maximumCharacters = 6000)
    {
        var text = new StringBuilder();
        text.Append(capture.Passed ? "Every scenario check passed. " : "The scenario run did not pass. ");
        foreach (var problem in capture.Problems) text.Append("Contract problem: ").Append(problem).Append(' ');
        foreach (var scenario in capture.Scenarios)
        {
            var failed = scenario.Checks.Where(c => !c.Passed).ToArray();
            text.Append($"[{scenario.Id}] {scenario.Checks.Count - failed.Length}/{scenario.Checks.Count} checks passed, {scenario.Events.Count + (scenario.EventsTruncated ?? 0)} events. ");
            foreach (var check in failed)
                text.Append($"FAILED {check.Name}{(check.Criterion is { } c ? $" (criterion {c})" : "")}: expected {Canonical(check.Expected)} ({check.Comparison}), " +
                    $"observed {Canonical(check.Observed)}{(check.Problem is { } p ? $" [{p}]" : "")}. ");
        }
        var value = text.ToString().TrimEnd();
        return value.Length <= maximumCharacters ? value : value[..(maximumCharacters - 3)] + "...";
    }

    /// <summary>Key-sorted compact JSON, so two runs compare by value.</summary>
    public static string Canonical(JsonElement? value)
    {
        if (value is not { } element || element.ValueKind is JsonValueKind.Undefined) return "null";
        var output = new StringBuilder();
        void Write(JsonElement node)
        {
            switch (node.ValueKind)
            {
                case JsonValueKind.Object:
                    output.Append('{');
                    var first = true;
                    foreach (var property in node.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                    {
                        if (!first) output.Append(',');
                        first = false;
                        output.Append(JsonSerializer.Serialize(property.Name)).Append(':');
                        Write(property.Value);
                    }
                    output.Append('}');
                    break;
                case JsonValueKind.Array:
                    output.Append('[');
                    var index = 0;
                    foreach (var item in node.EnumerateArray()) { if (index++ > 0) output.Append(','); Write(item); }
                    output.Append(']');
                    break;
                default: output.Append(node.GetRawText()); break;
            }
        }
        Write(element);
        return output.ToString();
    }

    private static string Resource(string name)
    {
        using var stream = typeof(BrowserScenarios).Assembly.GetManifestResourceStream("CSweet.Agent.SDK.BrowserScenarios." + name)
            ?? throw new InvalidOperationException("Missing browser scenario resource " + name + ".");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("\r\n", "\n");
    }
}

/// <summary>A scenario run or the machine-readable section of an observation record.</summary>
public sealed record ScenarioCapture(int Contract, bool Valid, bool Passed, IReadOnlyList<string> Problems, IReadOnlyList<ScenarioResult> Scenarios)
{
    public string? Build { get; init; }
}

public sealed record ScenarioResult(string Id, bool Passed, IReadOnlyList<ScenarioCheckResult> Checks, IReadOnlyList<ScenarioEvent> Events)
{
    public int? EventsTruncated { get; init; }
}

public sealed record ScenarioCheckResult(string Name, string Comparison, JsonElement? Expected, JsonElement? Observed, IReadOnlyList<int> Evidence, bool Passed)
{
    public int? Criterion { get; init; }
    public string? Problem { get; init; }
}

public sealed record ScenarioEvent(string Type, JsonElement? Data)
{
    public double? T { get; init; }
}
