const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');

async function check(initialMode) {
  const elements = new Map();
  const element = id => {
    if (!elements.has(id)) elements.set(id, { value: '', textContent: '', addEventListener() {} });
    return elements.get(id);
  };
  const calls = [];
  const messages = [];
  let mode = initialMode;
  const context = vm.createContext({
    document: { getElementById: element }, window: {},
    parent: { postMessage: m => messages.push(m.pluginMessage) },
    performance: { now: () => 0 }, Uint8Array, btoa: s => Buffer.from(s, 'binary').toString('base64'),
    fetch: async (_, request) => {
      const call = JSON.parse(request.body); calls.push(call);
      const result = call.command === 'ferry_status' ? { interfaceFerry: true, project: 'Checks', ...(mode ? { receiveAs: mode } : {}) }
        : call.command === 'ferry_missing_images' ? { missing: [] }
        : call.command === 'ferry_import' ? { ok: true, scene: 'Screen.unity', notes: [] }
        : { removed: 0 };
      return { status: 200, ok: true, json: async () => ({ success: true, result }) };
    }
  });
  const html = fs.readFileSync(path.join(__dirname, '../../FigmaPlugin~/ui.html'), 'utf8');
  vm.runInContext(html.match(/<script>([\s\S]*?)<\/script>/)[1], context);
  element('token').value = '7800:check-token';
  element('scale').value = '2';
  await context.window.onmessage({ data: { pluginMessage: { type: 'selection', count: 1, names: ['Screen'] } } });
  await element('push').onclick();
  assert.equal(messages.at(-1).type, 'collect');
  mode = initialMode === 'UGUI' ? 'UITK' : 'UGUI';
  await context.window.onmessage({ data: { pluginMessage: { type: 'collected', warnings: [], items: [
    { payload: { fileName: 'Checks', nodes: [{ name: 'Screen' }] }, images: [{ imageKey: 'img-check', bytes: new Uint8Array([1]) }], count: 1 }
  ] } } });
  for (const name of ['ferry_missing_images', 'ferry_import']) {
    assert.equal(calls.find(c => c.command === name).parameters.target, initialMode || undefined);
  }
  assert.match(element('status').textContent, /Screen\.unity/);
  assert.equal(calls.filter(c => c.command === 'ferry_stage_image').length, 0);
}

(async () => {
  await check('UGUI'); await check('UITK'); await check(undefined);
  console.log('PASS: receiver is frozen for image queries/import; scene results display; legacy receivers remain supported.');
})().catch(e => { console.error(e); process.exitCode = 1; });
