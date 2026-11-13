# Browser scenarios: observed behavior, judged outside the page

SDK 5.25.0 adds one way to verify interactive web output, used identically by the author, the technical reviewer and
independent QA. It applies to tickets whose planning declares `browser-interaction`
(`WorkItemVerificationRequirement.CriterionIndexes` names the criteria that need behavior observed in the running build).

## Why

Super Awesome Games ticket 38 (2026-11-13) failed QA twice:

- The engineer's only browser tool showed that the page loaded without exceptions. The page then decided its own
  scenario results from what the code intended: a planned brick path, and a voice cap counted only at booking time.
- The observation record transcribed that intent.
- QA was the only role able to observe the build, and it found one mismatch per round, with a full rework cycle each
  time.

## The contract (version 1)

The built page exposes `window.csweetScenarios = { run: async () => report }`. It may install the harness only when the
URL carries `?csweet-scenarios` (`WorkItemVerificationKinds.ScenarioQueryParameter`).

```json
{ "contract": 1,
  "scenarios": [ { "id": "shortChain",
    "events": [ { "type": "brick.hit", "t": 0.83, "data": { "row": 2, "col": 0, "toneId": "brick-row-2" } } ],
    "checks": [ { "name": "consecutive hits are orthogonally adjacent", "criterion": 1,
                  "expected": true, "observed": false, "comparison": "equals", "evidence": [0, 1, 2, 3] } ] } ] }
```

- **Events** record what actually happened, with the ids and values the program produced. Limit: 2,000 per scenario.
- **Checks** state the value a criterion requires (`expected`) and the value measured from the events (`observed`).
  - `comparison` is `equals` (default, by value), `atMost`, `atLeast` or `includes`.
  - `criterion` is the zero-based acceptance criterion the check serves.
  - `evidence` lists the indexes of the events the observation was computed from.
- **The page never sets a result.** `csweetEvaluateScenarios` decides each check:
  - A check with no evidence, or evidence pointing at no event, fails.
  - Any `status` or `passed` field the page adds is ignored.
- **Limits.** 32 scenarios, 64 checks per scenario, and each value fits 4,000 characters. `run()` must finish within
  10 seconds (QA's container stops after about 20).

The author's guidance (`BrowserScenarios.Guidance`, `BrowserScenarios.AssignmentNote(planning)`) adds three rules:

- Observations must come from emitted events, never from the plan, the scenario script or configuration.
- Every invariant the harness computes must be a check.
- Reasoning from source belongs outside the generated section, labelled as such.

## Running it

| Role | How |
|---|---|
| Author | Write `BrowserScenarios.RunnerScript` to `.csweet/browser-scenarios.cjs` and run `node .csweet/browser-scenarios.cjs <build> [--record <path.md>]...` (`BrowserScenarios.Command`). It drives real Chromium over CDP (`CHROMIUM_PATH` or `CHROME_BIN`), writes `test-results/browser-scenarios/capture.json` and a screenshot, regenerates each record's section, and exits 0 only when every check passes with no page errors. |
| Technical reviewer | `BranchDiffTechnicalReview` requires a passing receipt (`WorkItemVerifications.IsBrowserScenariosReceipt`) when interaction is declared, in addition to the smoke receipt. |
| QA | Run `BrowserScenarios.PlaywrightScript` as a behavioral script body with `(page, context, origin, check, attach)`. It evaluates with the same code, reports one check per criterion, and attaches the full evaluation as `scenarios`. |

## Observation records

`--record notes/observation.md` writes a section between `<!-- csweet:observed:begin -->` and
`<!-- csweet:observed:end -->`. If the markers are absent, the section is appended under "Observed run (generated)".

- **Contents.** Per-scenario check tables, the event list (first 60), and a machine-readable capture: no timings,
  events capped at 400, plus the build digest.
- **Rerunning.** A rerun replaces the section in place, and the same deterministic build produces the same capture.
- **Reading and comparing.** `BrowserScenarios.ReadRecord` reads the capture back. `BrowserScenarios.Compare(independent,
  recorded)` lists every difference in scenarios, expectations, observations, results and event sequence.
  `BrowserScenarios.Summarize` gives a bounded text summary for a model.

## Limits

- **The page still computes `observed`.** A wrong computation that cites real events can pass. The safeguards are:
  - the events are visible in the record;
  - reviewers judge whether a check measures its criterion;
  - QA can still write its own probes.
- **Possible later extension.** Declarative invariants the runner evaluates over events itself, such as "max
  concurrent voices ≤ 4" or a pairwise property over hits.
