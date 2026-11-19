// Node 22 smoke test: checks interop routing without external packages or a .NET runtime.
// This tests the transfer-selection logic, not a live Blazor SignalR circuit.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const script = fs.readFileSync(path.join(__dirname, '../src/Azunt.Editor/wwwroot/js/azunt-editor.js'), 'utf8');
function extractFunction(name) {
  const start = script.indexOf(`  function ${name}(`);
  assert.ok(start >= 0, `Missing ${name}`);
  const brace = script.indexOf('{', start);
  let depth = 0;
  for (let i = brace; i < script.length; i++) {
    if (script[i] === '{') depth++;
    if (script[i] === '}' && --depth === 0) return script.slice(start, i + 1);
  }
  throw Error(`Unclosed ${name}`);
}
const calls = [];
const window = {
  DotNet: { createJSStreamReference: blob => ({ kind: 'stream', blob }) }
};
const context = { window, Blob, console, Error, readHtml: instance => instance.html,
  message: (instance, text) => { instance.status = text; },
  getHtml: () => context.savedHtml };
const code = `const inlineInteropMaxBytes = 16 * 1024;
  const maxDocumentBytes = 16 * 1024 * 1024;
  ${extractFunction('createHtmlBlob')}
  ${extractFunction('createHtmlStream')}
  ${extractFunction('emit')}
  ${extractFunction('getHtmlStream')}`;
vm.runInNewContext(code, context);
const instance = {
  value: { value: '' }, revision: 0,
  dotNetReference: {
    invokeMethodAsync(method, ...args) { calls.push({ method, args }); return Promise.resolve(); }
  }
};
instance.html = '<p>Short Korean 한국어 document</p>';
vm.runInNewContext('emit',context)(instance);
assert.equal(calls.at(-1).method, 'OnHtmlChanged');
assert.equal(calls.at(-1).args[0], instance.html);
instance.html = `<p>With large image</p><img src="data:image/png;base64,${'A'.repeat(50_000)}">`;
vm.runInNewContext('emit',context)(instance);
assert.equal(calls.at(-1).method, 'OnHtmlStreamChanged');
assert.equal(calls.at(-1).args[0].kind, 'stream');
assert.ok(calls.at(-1).args[0].blob.size > 50_000);
context.savedHtml = instance.html;
const saved = vm.runInNewContext('getHtmlStream',context)('any-id');
assert.ok(saved instanceof Blob, 'GetHtmlAsync must receive a raw Blob, not a pre-wrapped stream reference');
assert.ok(saved.size > 50_000);
console.log('PASS: ordinary interop for small edits; wrapped stream for callbacks; raw Blob for InvokeAsync Save');
