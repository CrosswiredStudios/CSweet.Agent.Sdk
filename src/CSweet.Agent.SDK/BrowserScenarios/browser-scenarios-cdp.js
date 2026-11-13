// Author-side launcher: real Chromium over Node's built-in CDP client. No npm dependency or browser download.
// Usage: node .csweet/browser-scenarios.cjs <build directory> [--record <path.md>]...
const fs = require('node:fs/promises');
const path = require('node:path');
const os = require('node:os');
const http = require('node:http');
const { spawn } = require('node:child_process');
const { createHash } = require('node:crypto');

function csweetRelative(value) {
  return typeof value === 'string' && value.length > 0 && value.length <= 240 && !value.startsWith('-') && !value.startsWith('/') &&
    value.split('/').every(part => part.length > 0 && part !== '..' && /^[A-Za-z0-9._-]+$/.test(part));
}

async function csweetBuildDigest(root) {
  const files = [];
  async function walk(directory) {
    for (const entry of (await fs.readdir(directory, { withFileTypes: true })).sort((a, b) => a.name < b.name ? -1 : a.name > b.name ? 1 : 0)) {
      const full = path.join(directory, entry.name);
      if (entry.isDirectory()) await walk(full);
      else if (entry.isFile()) files.push(path.relative(root, full).split(path.sep).join('/') + '\0' +
        createHash('sha256').update(await fs.readFile(full)).digest('hex'));
    }
  }
  await walk(root);
  return createHash('sha256').update(files.join('\n')).digest('hex');
}

async function csweetMain() {
  const args = process.argv.slice(2);
  const directory = args[0];
  const records = [];
  if (!csweetRelative(directory) || (args.length - 1) % 2 !== 0) throw Error('Usage: node .csweet/browser-scenarios.cjs <build directory> [--record <path.md>]...');
  for (let i = 1; i < args.length; i += 2) {
    if (args[i] !== '--record' || !csweetRelative(args[i + 1]) || !/\.md$/i.test(args[i + 1])) throw Error('Each record is --record <workspace-relative path.md>.');
    records.push(args[i + 1]);
  }
  const workspace = await fs.realpath(process.cwd());
  const root = await fs.realpath(path.resolve(workspace, directory));
  if (!root.startsWith(workspace + path.sep)) throw Error('The build must be inside this workspace.');
  for (const record of records) {
    const file = path.resolve(workspace, record);
    if (!file.startsWith(workspace + path.sep)) throw Error('Records must be inside this workspace.');
    await fs.access(file).catch(() => { throw Error(`Record ${record} does not exist; create it with its interpretation sections first.`); });
  }
  const executable = process.env.CHROMIUM_PATH || process.env.CHROME_BIN;
  if (!executable) throw Error('Prepared Chromium unavailable; report an infrastructure blocker, not a skipped pass');
  const build = await csweetBuildDigest(root);
  const output = path.join(workspace, 'test-results/browser-scenarios');
  await fs.mkdir(output, { recursive: true });
  const profile = await fs.mkdtemp(path.join(os.tmpdir(), 'csweet-browser-scenarios-'));
  const errors = []; const pending = new Map(); let sequence = 0; let socket; let browser;
  const recordError = value => { if (errors.length < 20) errors.push(String(value).slice(0, 2000)); };
  const types = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json',
    '.wasm': 'application/wasm', '.svg': 'image/svg+xml', '.png': 'image/png', '.ogg': 'audio/ogg', '.mp3': 'audio/mpeg', '.wav': 'audio/wav' };
  const server = http.createServer(async (req, res) => {
    try {
      const pathname = decodeURIComponent(new URL(req.url, 'http://localhost').pathname);
      if (pathname === '/favicon.ico') { res.writeHead(204); res.end(); return; }
      const file = await fs.realpath(path.resolve(root, '.' + (pathname === '/' ? '/index.html' : pathname)));
      if (!file.startsWith(root + path.sep)) throw Error('Outside build');
      res.writeHead(200, { 'Content-Type': types[path.extname(file)] || 'application/octet-stream' });
      res.end(await fs.readFile(file));
    } catch { res.writeHead(404); res.end(); }
  });
  try {
    await new Promise((resolve, reject) => { server.once('error', reject); server.listen(0, '127.0.0.1', resolve); });
    browser = spawn(executable, ['--headless', ...(process.platform === 'linux' ? ['--no-sandbox'] : []), '--disable-dev-shm-usage',
      '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--autoplay-policy=no-user-gesture-required',
      '--remote-debugging-port=0', '--user-data-dir=' + profile, 'about:blank'], { stdio: ['ignore', 'ignore', 'pipe'], windowsHide: true });
    const endpoint = await new Promise((resolve, reject) => {
      const timer = setTimeout(() => reject(Error('Chromium startup timed out')), 15000);
      browser.once('error', error => { clearTimeout(timer); reject(error); });
      browser.once('exit', () => { clearTimeout(timer); reject(Error('Chromium exited before CDP became ready')); });
      let text = ''; browser.stderr.on('data', chunk => { text = (text + chunk).slice(-4096); const match = text.match(/DevTools listening on (ws:\/\/[^\s]+)/); if (match) { clearTimeout(timer); resolve(match[1]); } });
    });
    socket = new WebSocket(endpoint);
    await new Promise((resolve, reject) => { socket.addEventListener('open', resolve, { once: true }); socket.addEventListener('error', reject, { once: true }); });
    socket.addEventListener('message', event => {
      const data = JSON.parse(String(event.data)); const wait = pending.get(data.id);
      if (wait) { pending.delete(data.id); clearTimeout(wait.timer); data.error ? wait.reject(Error(data.error.message)) : wait.resolve(data.result); }
      if (data.method === 'Runtime.exceptionThrown') recordError(data.params.exceptionDetails.exception?.description || data.params.exceptionDetails.text);
      if (data.method === 'Network.loadingFailed' && !data.params.canceled) recordError('Asset request failed: ' + data.params.errorText);
      if (data.method === 'Network.responseReceived' && data.params.response.status >= 400) recordError('Asset HTTP ' + data.params.response.status + ': ' + new URL(data.params.response.url).pathname);
    });
    const send = (method, params = {}, sessionId, timeout = 15000) => new Promise((resolve, reject) => {
      const id = ++sequence; const timer = setTimeout(() => { pending.delete(id); reject(Error('Browser command timed out: ' + method)); }, timeout);
      pending.set(id, { resolve, reject, timer }); socket.send(JSON.stringify({ id, method, params, ...(sessionId ? { sessionId } : {}) }));
    });
    const version = (await send('Browser.getVersion')).product || 'Chromium';
    const { targetId } = await send('Target.createTarget', { url: 'about:blank' });
    const { sessionId } = await send('Target.attachToTarget', { targetId, flatten: true });
    for (const method of ['Runtime.enable', 'Page.enable', 'Network.enable']) await send(method, {}, sessionId);
    await send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 720, deviceScaleFactor: 1, mobile: false }, sessionId);
    const navigation = await send('Page.navigate', { url: 'http://127.0.0.1:' + server.address().port + '/?csweet-scenarios=1' }, sessionId);
    if (navigation.errorText) throw Error('Navigation failed: ' + navigation.errorText);
    let present = false;
    for (let attempt = 0; attempt < 150 && !present; attempt++) {
      const probe = await send('Runtime.evaluate', { expression: "document.readyState==='complete'&&typeof window.csweetScenarios?.run==='function'", returnByValue: true }, sessionId);
      present = probe.result.value === true;
      if (!present) await new Promise(resolve => setTimeout(resolve, 100));
    }
    let evaluation;
    if (!present) evaluation = csweetEvaluateScenarios(null);
    else {
      const run = await send('Runtime.evaluate', { expression: 'Promise.resolve(window.csweetScenarios.run()).then(r=>JSON.stringify(r))',
        awaitPromise: true, returnByValue: true, timeout: 60000 }, sessionId, 65000);
      if (run.exceptionDetails) recordError('csweetScenarios.run() threw: ' + (run.exceptionDetails.exception?.description || run.exceptionDetails.text));
      let report = null;
      try { report = typeof run.result?.value === 'string' ? JSON.parse(run.result.value) : null; } catch { recordError('run() did not resolve to JSON-serializable data.'); }
      evaluation = csweetEvaluateScenarios(report);
    }
    if (!present) evaluation.problems.unshift('window.csweetScenarios.run is not defined once the build loads with ?csweet-scenarios=1.');
    const capture = await send('Page.captureScreenshot', { format: 'png' }, sessionId);
    await fs.writeFile(path.join(output, 'screenshot.png'), Buffer.from(capture.data, 'base64'));
    const command = ['node .csweet/browser-scenarios.cjs', directory, ...records.flatMap(r => ['--record', r])].join(' ');
    await fs.writeFile(path.join(output, 'capture.json'), JSON.stringify({ build, browser: version, errors, evaluation }, null, 2));
    for (const record of records) {
      const file = path.resolve(workspace, record);
      const markdown = await fs.readFile(file, 'utf8');
      const updated = csweetReplaceRecord(markdown, csweetRenderRecord(evaluation, { build, browser: version, command }));
      if (updated !== markdown) await fs.writeFile(file, updated);
    }
    const passed = evaluation.passed && errors.length === 0;
    console.log(JSON.stringify({ ...csweetSummary(evaluation), passed, build, browser: version, errors: [...new Set(errors)],
      records, capture: 'test-results/browser-scenarios/capture.json',
      note: 'Each check is judged here from expected and observed values; a check whose observation cites no emitted event fails.' }));
    process.exitCode = passed ? 0 : 1;
  } finally {
    for (const wait of pending.values()) { clearTimeout(wait.timer); wait.reject(Error('Browser scenarios closed')); }
    socket?.close(); browser?.kill(); await new Promise(resolve => server.close(resolve));
    if (browser && browser.exitCode === null) await Promise.race([new Promise(resolve => browser.once('exit', resolve)), new Promise(resolve => setTimeout(resolve, 3000))]);
    await fs.rm(profile, { recursive: true, force: true, maxRetries: 5, retryDelay: 100 });
  }
}

csweetMain().catch(error => { console.error('BROWSER_SCENARIOS_FAILED: ' + error.message); process.exitCode = 1; });
