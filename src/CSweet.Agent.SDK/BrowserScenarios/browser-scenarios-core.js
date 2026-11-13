// C-Sweet browser scenario contract, version 1. Shared byte-for-byte by the author's runner and independent QA.
//
// The page exposes window.csweetScenarios = { run: async () => report } (it may install it only when the URL has
// ?csweet-scenarios). report = { contract: 1, scenarios: [ { id, events: [ { type, t?, data? } ],
//   checks: [ { name, expected, observed, comparison?, criterion?, evidence: [eventIndex, ...] } ] } ] }.
// The page records what happened and what it expected. It never decides a result: this evaluator does, from
// expected and observed values, and rejects any observation that cites no emitted event.
const CSWEET_SCENARIO_LIMITS = { scenarios: 32, checks: 64, events: 2000, evidence: 32, value: 4000, recordEvents: 400 };
const CSWEET_COMPARISONS = ['equals', 'atMost', 'atLeast', 'includes'];

function csweetCanonical(value) {
  if (value === undefined) return 'undefined';
  if (value === null || typeof value !== 'object') return JSON.stringify(value);
  if (Array.isArray(value)) return '[' + value.map(csweetCanonical).join(',') + ']';
  return '{' + Object.keys(value).sort().map(key => JSON.stringify(key) + ':' + csweetCanonical(value[key])).join(',') + '}';
}

function csweetCompare(comparison, expected, observed) {
  switch (comparison) {
    case 'atMost': return typeof expected === 'number' && typeof observed === 'number' && observed <= expected;
    case 'atLeast': return typeof expected === 'number' && typeof observed === 'number' && observed >= expected;
    case 'includes':
      if (Array.isArray(observed)) return observed.some(item => csweetCanonical(item) === csweetCanonical(expected));
      return typeof observed === 'string' && typeof expected === 'string' && observed.includes(expected);
    default: return csweetCanonical(expected) === csweetCanonical(observed);
  }
}

function csweetEvaluateScenarios(report) {
  const problems = [];
  const scenarios = [];
  const problem = text => { if (problems.length < 40) problems.push(String(text).slice(0, 400)); };
  if (!report || typeof report !== 'object' || Array.isArray(report)) {
    problem('run() must resolve to an object { contract: 1, scenarios: [...] }.');
    return { contract: 1, valid: false, passed: false, problems, scenarios };
  }
  if (report.contract !== 1) problem('report.contract must be 1.');
  const list = Array.isArray(report.scenarios) ? report.scenarios : [];
  if (list.length === 0 || list.length > CSWEET_SCENARIO_LIMITS.scenarios)
    problem(`report.scenarios must list 1-${CSWEET_SCENARIO_LIMITS.scenarios} scenarios.`);
  const ids = new Set();
  for (const [index, raw] of list.slice(0, CSWEET_SCENARIO_LIMITS.scenarios).entries()) {
    const id = raw && typeof raw.id === 'string' ? raw.id : '';
    if (!/^[A-Za-z0-9._-]{1,80}$/.test(id)) problem(`scenarios[${index}].id must be 1-80 letters, digits, '.', '_' or '-'.`);
    else if (ids.has(id)) problem(`Scenario id '${id}' is repeated.`);
    ids.add(id);
    const events = [];
    const rawEvents = Array.isArray(raw?.events) ? raw.events : [];
    if (!Array.isArray(raw?.events)) problem(`Scenario '${id}' must list the events it emitted (events: []).`);
    if (rawEvents.length > CSWEET_SCENARIO_LIMITS.events) problem(`Scenario '${id}' emitted more than ${CSWEET_SCENARIO_LIMITS.events} events.`);
    for (const [eventIndex, event] of rawEvents.slice(0, CSWEET_SCENARIO_LIMITS.events).entries()) {
      if (!event || typeof event.type !== 'string' || !event.type || event.type.length > 80) {
        problem(`Scenario '${id}' event ${eventIndex} needs a type of 1-80 characters.`);
        events.push({ type: '(invalid)' });
        continue;
      }
      const item = { type: event.type };
      if (typeof event.t === 'number' && Number.isFinite(event.t)) item.t = event.t;
      if (event.data !== undefined) {
        if (csweetCanonical(event.data).length > CSWEET_SCENARIO_LIMITS.value) problem(`Scenario '${id}' event ${eventIndex} data exceeds ${CSWEET_SCENARIO_LIMITS.value} characters.`);
        else item.data = event.data;
      }
      events.push(item);
    }
    const checks = [];
    const rawChecks = Array.isArray(raw?.checks) ? raw.checks : [];
    if (rawChecks.length === 0 || rawChecks.length > CSWEET_SCENARIO_LIMITS.checks)
      problem(`Scenario '${id}' must list 1-${CSWEET_SCENARIO_LIMITS.checks} checks.`);
    const names = new Set();
    for (const [checkIndex, check] of rawChecks.slice(0, CSWEET_SCENARIO_LIMITS.checks).entries()) {
      const name = check && typeof check.name === 'string' ? check.name.trim() : '';
      const result = { name: name || `(check ${checkIndex})`, comparison: check?.comparison ?? 'equals',
        expected: check?.expected ?? null, observed: check?.observed ?? null, evidence: [], passed: false };
      if (Number.isInteger(check?.criterion) && check.criterion >= 0) result.criterion = check.criterion;
      let issue = null;
      if (!name || name.length > 160) issue = 'needs a name of 1-160 characters';
      else if (names.has(name)) issue = 'repeats another check name';
      else if (!check || !('expected' in check) || !('observed' in check)) issue = 'needs both expected and observed';
      else if (!CSWEET_COMPARISONS.includes(result.comparison)) issue = `comparison must be one of ${CSWEET_COMPARISONS.join(', ')}`;
      else if (csweetCanonical(result.expected).length > CSWEET_SCENARIO_LIMITS.value || csweetCanonical(result.observed).length > CSWEET_SCENARIO_LIMITS.value)
        issue = `expected and observed must each fit ${CSWEET_SCENARIO_LIMITS.value} characters`;
      else if (!Array.isArray(check.evidence) || check.evidence.length === 0 || check.evidence.length > CSWEET_SCENARIO_LIMITS.evidence ||
        check.evidence.some(i => !Number.isInteger(i) || i < 0 || i >= events.length))
        issue = 'must cite, in evidence, the indexes of the emitted events its observed value was measured from';
      names.add(name);
      if (Array.isArray(check?.evidence)) result.evidence = check.evidence.filter(Number.isInteger).slice(0, CSWEET_SCENARIO_LIMITS.evidence);
      if (issue) result.problem = issue;
      else result.passed = csweetCompare(result.comparison, result.expected, result.observed);
      checks.push(result);
    }
    scenarios.push({ id, passed: checks.length > 0 && checks.every(c => c.passed), checks, events });
  }
  const valid = problems.length === 0;
  return { contract: 1, valid, passed: valid && scenarios.length > 0 && scenarios.every(s => s.passed), problems, scenarios };
}

// The machine-readable form kept in an observation record: no timings, events capped, so two runs of the same
// deterministic build produce the same text.
function csweetRecordCapture(evaluation, build) {
  return {
    contract: 1, build, valid: evaluation.valid, passed: evaluation.passed, problems: evaluation.problems,
    scenarios: evaluation.scenarios.map(s => {
      const item = { id: s.id, passed: s.passed,
        checks: s.checks.map(c => {
          const check = { name: c.name, comparison: c.comparison, expected: c.expected, observed: c.observed, evidence: c.evidence, passed: c.passed };
          if (c.criterion !== undefined) check.criterion = c.criterion;
          if (c.problem) check.problem = c.problem;
          return check;
        }),
        events: s.events.slice(0, CSWEET_SCENARIO_LIMITS.recordEvents).map(e => e.data === undefined ? { type: e.type } : { type: e.type, data: e.data }) };
      if (s.events.length > CSWEET_SCENARIO_LIMITS.recordEvents) item.eventsTruncated = s.events.length - CSWEET_SCENARIO_LIMITS.recordEvents;
      return item;
    })
  };
}

function csweetSummary(evaluation) {
  return {
    passed: evaluation.passed, valid: evaluation.valid, problems: evaluation.problems,
    scenarios: evaluation.scenarios.map(s => ({ id: s.id, passed: s.passed, checks: s.checks.length,
      failed: s.checks.filter(c => !c.passed).slice(0, 12).map(c => ({ name: c.name, criterion: c.criterion,
        comparison: c.comparison, expected: c.expected, observed: c.observed, problem: c.problem })) }))
  };
}
