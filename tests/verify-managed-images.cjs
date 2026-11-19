// Test the optional upload route without a network connection or other JS packages.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const source = fs.readFileSync(path.join(__dirname, '../src/Azunt.Editor/wwwroot/js/azunt-editor.js'), 'utf8');
const start = source.indexOf('  async function imageUrl(');
assert.ok(start >= 0, 'imageUrl implementation exists');
const end = source.indexOf('\n  // External drag-and-drop', start);
assert.ok(end > start);
const code = source.slice(start, end);
const calls = [];
const doc = { baseURI: 'https://editor.example.test/editor/blazor' };
const context = {
  URL, FormData, Blob,
  document: doc,
  location: { origin: 'https://editor.example.test' },
  maxInlineImageBytes: 8 * 1024 * 1024,
  countInlineBytes: () => 0,
  asDataUrl: async () => 'data:image/png;base64,QUJD',
  safeResourceUrl: url => url,
  fetch: async (url, options) => {
    calls.push({ url, options });
    if (String(url).endsWith('/token'))
      return { ok: true, json: async () => ({ requestToken: 'valid-token' }) };
    assert.equal(options.headers.RequestVerificationToken, 'valid-token');
    assert.ok(options.body instanceof FormData);
    return { ok: true, json: async () => ({ url: '/editor/demo-images/' + 'a'.repeat(32) }) };
  }
};
vm.runInNewContext(code, context);
const test = vm.runInNewContext('imageUrl', context);
const file = new Blob([new Uint8Array([137, 80, 78, 71])], { type: 'image/png' });
file.name = 'test.png';
(async () => {
  const instance = { uploadUrl: '/editor/demo-images', element: { closest: () => null } };
  const returned = await test(instance, file);
  assert.equal(returned, '/editor/demo-images/' + 'a'.repeat(32));
  assert.equal(calls.length, 2);
  assert.equal(calls[0].url, 'https://editor.example.test/editor/demo-images/token');
  assert.equal(calls[1].url, 'https://editor.example.test/editor/demo-images');
  assert.equal(instance.uploadToken, 'valid-token');
  const inline = await test({ uploadUrl: null }, file);
  assert.ok(inline.startsWith('data:image/'));
  await assert.rejects(() => test({ uploadUrl: 'https://evil.example/upload', element: { closest: () => null } }, file),
    /same origin/);
  assert.equal(calls.length, 2, 'cross-origin upload must not even request a token');
  console.log('PASS: optional managed image upload, antiforgery token, inline fallback, origin rejection');
})().catch(err => { console.error(err); process.exitCode = 1; });
