using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK.Tests;

/// <summary>
/// Super Awesome Games ticket 38 (2026-11-13): the page judged its own scenarios from what it intended, and the record
/// transcribed intent. The shared runner judges from emitted events and generates the record's measured section.
/// </summary>
public sealed class BrowserScenariosTests
{
    // A deterministic fixture in the shape of the spike harness: the chain check is measured from emitted hits, the voice
    // cap from emitted start/stop events. The planned path is adjacent; the hits that actually happened are not.
    private const string Page = """
        <!doctype html><html><body><canvas width="320" height="200"></canvas><script>
        const hits = [[2,0],[2,1],[1,1],[2,2]];
        window.csweetScenarios = { run: async () => {
          const chain = { id: 'shortChain', events: hits.map(([row, col], i) => ({ type: 'brick.hit', t: i * 0.1, data: { row, col } })) };
          const adjacent = hits.slice(1).every(([r, c], i) => Math.abs(r - hits[i][0]) + Math.abs(c - hits[i][1]) === 1);
          chain.checks = [
            { name: 'consecutive hits are orthogonally adjacent', criterion: 1, expected: true, observed: adjacent, evidence: [0, 1, 2, 3] },
            { name: 'four bricks hit', criterion: 1, expected: 4, observed: hits.length, evidence: [0, 1, 2, 3] }];
          const voices = { id: 'voiceCap', events: [], checks: [] };
          let live = 0, peak = 0;
          for (let i = 0; i < 5; i++) { live++; peak = Math.max(peak, live); voices.events.push({ type: 'voice.start', data: { live } }); }
          voices.checks.push({ name: 'peak concurrent voices', criterion: 2, comparison: 'atMost', expected: 4, observed: peak, evidence: [4] });
          voices.checks.push({ name: 'claimed without evidence', criterion: 2, expected: true, observed: true });
          return { contract: 1, scenarios: [chain, voices] };
        } };
        </script></body></html>
        """;

    [Fact]
    public void Scripts_load_and_the_qa_script_fits_the_qa_script_budget()
    {
        Assert.Contains("function csweetEvaluateScenarios", BrowserScenarios.RunnerScript);
        Assert.Contains("function csweetRenderRecord", BrowserScenarios.RunnerScript);
        Assert.Contains("csweetMain()", BrowserScenarios.RunnerScript);
        Assert.Contains("attach('scenarios', evaluation)", BrowserScenarios.PlaywrightScript);
        Assert.DoesNotContain("csweetMain", BrowserScenarios.PlaywrightScript);
        Assert.True(Encoding.UTF8.GetByteCount(BrowserScenarios.PlaywrightScript) <= 12000);
    }

    [Fact]
    public void Commands_are_standard_receipts_and_unsafe_paths_are_refused()
    {
        var command = BrowserScenarios.Command("dist/spike", ["spike-notes/observation.md"]);
        Assert.Equal("node .csweet/browser-scenarios.cjs dist/spike --record spike-notes/observation.md", command);
        Assert.True(WorkItemVerifications.IsBrowserScenariosReceipt(command, true, 0));
        Assert.Throws<ArgumentException>(() => BrowserScenarios.Command("../dist"));
        Assert.Throws<ArgumentException>(() => BrowserScenarios.Command("dist", ["notes.txt"]));
    }

    [Fact]
    public void The_note_appears_only_when_interaction_is_declared()
    {
        Assert.Equal("", BrowserScenarios.AssignmentNote(new(["x"], ["y"]) { Verifications = [] }));
        var note = BrowserScenarios.AssignmentNote(new(["x"], ["a", "b"]) { Verifications = [
            new(WorkItemVerificationKinds.BrowserSmoke) { RequireCanvas = true },
            new(WorkItemVerificationKinds.BrowserInteraction) { CriterionIndexes = [1] }] });
        Assert.Contains("criteria 1", note);
        Assert.Contains("run_browser_scenarios", note);
        Assert.Contains("Never compute observed from the plan", note);
    }

    [Fact]
    public void The_evaluator_judges_from_observations_and_rejects_uncited_claims()
    {
        if (Node() is not { } node) return;
        var evaluation = Evaluate(node, """
            {"contract":1,"scenarios":[{"id":"a","status":"pass","events":[{"type":"hit","data":{"n":1}}],"checks":[
              {"name":"cited","expected":1,"observed":1,"evidence":[0]},
              {"name":"uncited","expected":true,"observed":true},
              {"name":"bad index","expected":1,"observed":1,"evidence":[7]},
              {"name":"cap","comparison":"atMost","expected":4,"observed":5,"evidence":[0]}]}]}
            """);
        var scenario = Assert.Single(evaluation.Scenarios);
        Assert.False(scenario.Passed); // the page's own "status":"pass" is ignored
        Assert.True(scenario.Checks.Single(c => c.Name == "cited").Passed);
        Assert.Contains("evidence", scenario.Checks.Single(c => c.Name == "uncited").Problem);
        Assert.Contains("evidence", scenario.Checks.Single(c => c.Name == "bad index").Problem);
        Assert.False(scenario.Checks.Single(c => c.Name == "cap").Passed);
        Assert.True(evaluation.Valid);
        Assert.False(evaluation.Passed);

        var invalid = Evaluate(node, """{"contract":2,"scenarios":[]}""");
        Assert.False(invalid.Valid);
        Assert.Contains(invalid.Problems, p => p.Contains("contract"));
    }

    [Fact]
    public async Task The_runner_observes_real_chromium_and_generates_the_record_that_qa_compares()
    {
        if (Node() is not { } node || Chromium() is not { } chromium) return;
        var root = Path.Combine(Path.GetTempPath(), "csweet-scenarios-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "dist"));
        Directory.CreateDirectory(Path.Combine(root, ".csweet"));
        Directory.CreateDirectory(Path.Combine(root, "notes"));
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "dist", "index.html"), Page);
            await File.WriteAllTextAsync(Path.Combine(root, BrowserScenarios.RunnerPath), BrowserScenarios.RunnerScript);
            await File.WriteAllTextAsync(Path.Combine(root, "notes", "observation.md"),
                "# Observation record\n\n## Interpretation\n\nThe chain was planned as r1c0, r1c1, r2c1, r2c2.\n");
            var command = BrowserScenarios.Command("dist", ["notes/observation.md"]);
            var (exit, output) = await RunAsync(node, root, chromium, [BrowserScenarios.RunnerPath, "dist", "--record", "notes/observation.md"]);

            Assert.True(exit == 1, output); // two real failures and one uncited claim
            using var summary = JsonDocument.Parse(output.Trim().Split('\n').Last());
            Assert.False(summary.RootElement.GetProperty("passed").GetBoolean());
            var record = await File.ReadAllTextAsync(Path.Combine(root, "notes", "observation.md"));
            Assert.Contains("The chain was planned as r1c0", record); // hand-written interpretation kept
            Assert.Contains(BrowserScenarios.RecordBegin, record);
            Assert.Contains("| consecutive hits are orthogonally adjacent | 1 | equals | true | false |", record);
            Assert.Contains(command, record);
            var recorded = BrowserScenarios.ReadRecord(record)!;
            Assert.False(recorded.Passed);
            Assert.Equal(4, recorded.Scenarios.Single(s => s.Id == "shortChain").Events.Count);
            Assert.True(File.Exists(Path.Combine(root, BrowserScenarios.CapturePath)));

            // A second run regenerates the section in place; the same build gives the same capture.
            await RunAsync(node, root, chromium, [BrowserScenarios.RunnerPath, "dist", "--record", "notes/observation.md"]);
            var again = await File.ReadAllTextAsync(Path.Combine(root, "notes", "observation.md"));
            Assert.Equal(1, again.Split(BrowserScenarios.RecordBegin).Length - 1);
            Assert.Empty(BrowserScenarios.Compare(BrowserScenarios.ReadRecord(again)!, recorded));

            // QA compares its own run with the record: a hand-edited observation is a difference.
            var edited = record.Replace("\"observed\":false", "\"observed\":true");
            var differences = BrowserScenarios.Compare(recorded, BrowserScenarios.ReadRecord(edited)!);
            Assert.Contains(differences, d => d.Contains("shortChain/consecutive hits are orthogonally adjacent: observed false here, true in the record"));
            Assert.Contains("FAILED peak concurrent voices (criterion 2): expected 4 (atMost), observed 5", BrowserScenarios.Summarize(recorded));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task A_page_without_the_contract_fails_and_says_why()
    {
        if (Node() is not { } node || Chromium() is not { } chromium) return;
        var root = Path.Combine(Path.GetTempPath(), "csweet-scenarios-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "dist"));
        Directory.CreateDirectory(Path.Combine(root, ".csweet"));
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "dist", "index.html"), "<!doctype html><canvas></canvas>");
            await File.WriteAllTextAsync(Path.Combine(root, BrowserScenarios.RunnerPath), BrowserScenarios.RunnerScript);
            var (exit, output) = await RunAsync(node, root, chromium, [BrowserScenarios.RunnerPath, "dist"], TimeSpan.FromSeconds(60));
            Assert.Equal(1, exit);
            Assert.Contains("window.csweetScenarios.run is not defined", output);
        }
        finally { Directory.Delete(root, true); }
    }

    private static ScenarioCapture Evaluate(string node, string report)
    {
        var script = Path.Combine(Path.GetTempPath(), "csweet-evaluate-" + Guid.NewGuid().ToString("N") + ".cjs");
        File.WriteAllText(script, BrowserScenarios.PlaywrightScript[..BrowserScenarios.PlaywrightScript.IndexOf("// Independent QA launcher", StringComparison.Ordinal)] +
            "\nprocess.stdout.write(JSON.stringify(csweetEvaluateScenarios(JSON.parse(require('node:fs').readFileSync(0,'utf8')))));\n");
        try
        {
            var start = new ProcessStartInfo(node, [script]) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using var process = Process.Start(start)!;
            process.StandardInput.Write(report);
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
            return BrowserScenarios.ReadEvaluation(output)!;
        }
        finally { File.Delete(script); }
    }

    private static async Task<(int Exit, string Output)> RunAsync(string node, string root, string chromium, string[] arguments, TimeSpan? timeout = null)
    {
        var start = new ProcessStartInfo(node, arguments) { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["CHROMIUM_PATH"] = chromium;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var cancel = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(90));
        await process.WaitForExitAsync(cancel.Token);
        return (process.ExitCode, await output + await error);
    }

    private static string? Node() => Find("node");

    private static string? Chromium() =>
        Environment.GetEnvironmentVariable("CHROMIUM_PATH") is { Length: > 0 } configured && File.Exists(configured) ? configured :
        new[] { "/usr/bin/google-chrome", "/usr/bin/chromium", "/usr/bin/chromium-browser" }.FirstOrDefault(File.Exists);

    private static string? Find(string name) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Select(directory => Path.Combine(directory, OperatingSystem.IsWindows() ? name + ".exe" : name)).FirstOrDefault(File.Exists);
}
