import assert from 'node:assert/strict';
import test from 'node:test';
import { createBinaryenHost } from './binaryen-host.mjs';
const encoder = new TextEncoder();
const pathSource = 'module.exports = { resolve: name => process.cwd() + name };';
function pinnedSource(syntaxFailure = false) {
  const start = 'if(!ENVIRONMENT_IS_NODE){throw new Error("NODERAWFS is currently only supported on Node.js environment.")}var nodeTTY=require("node:tty");';
  const end = 'for(const[key,value]of Object.entries(NODERAWFS_stream_funcs)){FS[key]=_wrapNodeStreamFunc(value,FS[key])}';
  const block = start + '/*' + 'x'.repeat(698 - start.length - end.length - 4) + '*/' + end;
  return `var nodePath=require("node:path");var count=0;var EXITSTATUS=0;var wasmMemory={buffer:new ArrayBuffer(16)};
    var entries=new Map();var FS={writeFile:(name,bytes)=>entries.set(name,bytes),stat:name=>({size:entries.get(name).length}),readFile:name=>entries.get(name),unlink:name=>entries.delete(name)};
    var callMain=args=>{if(nodePath.resolve('x')!=='/x')throw Error('Path adapter');if(args[0]==='bad'){EXITSTATUS=1;throw Error('CLI failure');}entries.set('out.wasm',Uint8Array.of(++count));return 0;};
    ${block}{if(Module["noExitRuntime"]){} }${syntaxFailure ? 'invalid @ syntax;' : ''}Module.onRuntimeInitialized();`;
}
function moduleUrls(t) {
  const created = [], revoked = [];
  // Node does not import blob: URLs. A data: URL preserves ES module semantics
  // while allowing the test to observe the browser URL lifecycle.
  t.mock.method(globalThis, 'Blob', class { constructor(parts) { this.source = parts.join(''); } });
  t.mock.method(URL, 'createObjectURL', blob => { const url = 'data:text/javascript;base64,' + Buffer.from(blob.source).toString('base64'); created.push(url); return url; });
  t.mock.method(URL, 'revokeObjectURL', url => revoked.push(url));
  return { created, revoked };
}
const request = args => ({ args, files: {}, outputs: ['out.wasm'] });

test('imports verified tool and path factories once, revokes URLs and creates fresh runtimes', async t => {
  const urls = moduleUrls(t), loads = [];
  const host = createBinaryenHost({ loadAsset: async name => { loads.push(name); return encoder.encode(name === 'path-browserify.js' ? pathSource : pinnedSource()); } });
  assert.deepEqual(Array.from((await host.run('wasm-merge', request([]))).files['out.wasm']), [1]);
  const failed = await host.run('wasm-merge', request(['bad']));
  assert.equal(failed.exitCode, 1);assert.deepEqual(Object.keys(failed.files), []);
  assert.deepEqual(Array.from((await host.run('wasm-merge', request([]))).files['out.wasm']), [1]);
  assert.equal((await host.run('wasm-opt', request([]))).exitCode, 0);
  assert.deepEqual(loads, ['wasm-merge.js', 'path-browserify.js', 'wasm-opt.js']);
  assert.equal(urls.created.length, 3);assert.deepEqual(urls.revoked, urls.created);
});

test('failed tool module evaluation revokes its URL and allows loader retry', async t => {
  const urls = moduleUrls(t);let tools = 0;
  const host = createBinaryenHost({ loadAsset: async name => encoder.encode(name === 'path-browserify.js' ? pathSource : pinnedSource(++tools === 1)) });
  await assert.rejects(host.run('wasm-merge', request([])), SyntaxError);
  assert.equal((await host.run('wasm-merge', request([]))).exitCode, 0);
  assert.equal(tools, 2);assert.equal(urls.created.length, 3);assert.deepEqual(urls.revoked, urls.created);
});

test('failed path module import retries without discarding a successful tool factory', async t => {
  const urls = moduleUrls(t);let tools = 0, paths = 0;
  const host = createBinaryenHost({ loadAsset: async name => { if(name === 'path-browserify.js')return encoder.encode(++paths === 1 ? 'invalid @ path;' : pathSource);tools++;return encoder.encode(pinnedSource()); } });
  await assert.rejects(host.run('wasm-opt', request([])), SyntaxError);
  assert.equal((await host.run('wasm-opt', request([]))).exitCode, 0);
  assert.equal(tools, 1);assert.equal(paths, 2);assert.deepEqual(urls.revoked, urls.created);
});

test('changed pinned bootstrap fails before module import and does not poison recovery', async t => {
  const urls = moduleUrls(t);let tools = 0;
  const host = createBinaryenHost({ loadAsset: async name => encoder.encode(name === 'path-browserify.js' ? pathSource : ++tools === 1 ? pinnedSource().replace('NODERAWFS is', 'Changed is') : pinnedSource()) });
  await assert.rejects(host.run('wasm-merge', request([])), /Pinned Binaryen bootstrap shape changed/);
  assert.equal(urls.created.length, 0);
  assert.equal((await host.run('wasm-merge', request([]))).exitCode, 0);
  assert.deepEqual(urls.revoked, urls.created);
});
