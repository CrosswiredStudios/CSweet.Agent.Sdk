// Independent QA launcher: the body of a QA behavioral script (page, context, origin, check, attach).
await page.goto(origin + '/?csweet-scenarios=1', { waitUntil: 'load', timeout: 10000 });
let present = false;
try { await page.waitForFunction(() => typeof window.csweetScenarios?.run === 'function', null, { timeout: 6000 }); present = true; } catch {}
if (!present) {
  check('scenario contract present', false, 'window.csweetScenarios.run is not defined once the build loads with ?csweet-scenarios=1, so the declared interaction criteria have no standard scenario run.');
  return;
}
const raw = await page.evaluate(async () => JSON.stringify(await window.csweetScenarios.run()));
const evaluation = csweetEvaluateScenarios(JSON.parse(raw));
if (typeof attach === 'function') await attach('scenarios', evaluation);
const all = evaluation.scenarios.flatMap(s => s.checks.map(c => ({ ...c, scenario: s.id })));
check('scenario contract valid', evaluation.valid, evaluation.valid
  ? `${evaluation.scenarios.length} scenarios, ${all.length} checks, judged from expected and observed values.`
  : evaluation.problems.join('; ').slice(0, 1000));
const describe = list => {
  const failed = list.filter(c => !c.passed);
  const text = `${list.length - failed.length} of ${list.length} scenario checks passed.` + (failed.length ? ' Failed: ' + failed.map(c =>
    `${c.scenario}/${c.name}: expected ${csweetCanonical(c.expected)} (${c.comparison}), observed ${csweetCanonical(c.observed)}${c.problem ? ' [' + c.problem + ']' : ''}`).join('; ') : '');
  return text.length > 1000 ? text.slice(0, 997) + '...' : text;
};
const criteria = [...new Set(all.filter(c => c.criterion !== undefined).map(c => c.criterion))].sort((a, b) => a - b).slice(0, 21);
for (const criterion of criteria) {
  const list = all.filter(c => c.criterion === criterion);
  try { check(`criterion ${criterion}: scenario checks`, list.every(c => c.passed), describe(list), { criterion }); }
  catch { check(`criterion ${criterion} (not one of this ticket's criteria): scenario checks`, list.every(c => c.passed), describe(list)); }
}
const other = all.filter(c => c.criterion === undefined || !criteria.includes(c.criterion));
if (other.length) check('scenario checks without a listed criterion', other.every(c => c.passed), describe(other));
