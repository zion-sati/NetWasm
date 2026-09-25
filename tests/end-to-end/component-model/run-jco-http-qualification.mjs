import { createReadStream, existsSync, statSync } from 'node:fs';
import { createServer } from 'node:http';
import { extname, join, normalize, resolve } from 'node:path';
import { chromium } from 'playwright';

const [packageDirectory] = process.argv.slice(2);
if (!packageDirectory) {
  throw new Error('usage: node run-jco-http-qualification.mjs <package-directory>');
}

const shimDirectory = resolve(process.cwd(), 'node_modules/@bytecodealliance/preview2-shim');
const hostingDirectory = resolve(process.cwd(), 'src/NetWasm.Hosting/JavaScript');
const importMapNonce = 'http-qualification-import-map';
const contentSecurityPolicy =
  `default-src 'none'; script-src 'self' 'nonce-${importMapNonce}' 'wasm-unsafe-eval'; connect-src 'self'`;
const types = new Map([
  ['.html', 'text/html; charset=utf-8'],
  ['.js', 'text/javascript; charset=utf-8'],
  ['.mjs', 'text/javascript; charset=utf-8'],
  ['.wasm', 'application/wasm'],
]);
const qualificationRequests = new Map();

const server = createServer((request, response) => {
  const requestUrl = new URL(request.url, 'http://localhost');
  const requestPath = requestUrl.pathname;
  if (requestPath.startsWith('/http-qualification/')) {
    if (requestUrl.searchParams.get('failure') === '1') {
      request.socket.destroy();
      return;
    }
    if (request.headers['x-qualification'] !== 'accepted') {
      response.writeHead(400, { 'content-length': '0' });
      response.end();
      return;
    }

    const requestCount = (qualificationRequests.get(requestPath) ?? 0) + 1;
    qualificationRequests.set(requestPath, requestCount);
    const writeResponse = () => {
      if (response.destroyed) return;
      response.writeHead(200, {
        'content-type': 'text/plain; charset=utf-8',
        'x-delayed': 'true',
        'cache-control': 'no-store',
      });
      response.flushHeaders();
      const chunks = ['header|', 'chunk-a|', 'chunk-b'];
      const writeChunk = index => {
        if (response.destroyed) return;
        if (index === chunks.length) {
          response.end();
          return;
        }
        response.write(chunks[index]);
        setTimeout(() => writeChunk(index + 1), 50);
      };
      setTimeout(() => writeChunk(0), 50);
    };
    setTimeout(writeResponse, requestCount === 1 ? 2000 : 0);
    return;
  }

  if (requestPath === '/bootstrap.html') {
    response.writeHead(200, {
      'content-type': 'text/html; charset=utf-8',
      'content-security-policy': contentSecurityPolicy,
      'cache-control': 'no-store',
    });
    response.end(`<!doctype html><script type="importmap" nonce="${importMapNonce}">${JSON.stringify({
      imports: {
        '@bytecodealliance/preview2-shim': '/preview2-shim/dist/browser/index.js',
        '@bytecodealliance/preview2-shim/': '/preview2-shim/dist/browser/',
        '@bytecodealliance/preview2-shim/instantiation': '/preview2-shim/dist/common/instantiation.js',
      },
    })}</script>`);
    return;
  }

  const relative = normalize(requestPath).replace(/^[/\\]+/, '');
  const isShim = relative.startsWith('preview2-shim/');
  const isHosting = relative.startsWith('netwasm-hosting/');
  const root = isShim ? shimDirectory : isHosting ? hostingDirectory : packageDirectory;
  let path = join(root, isShim
    ? relative.slice('preview2-shim/'.length)
    : isHosting
      ? relative.slice('netwasm-hosting/'.length)
      : relative || 'index.html');
  if (root === shimDirectory && !existsSync(path) && !extname(path)) path += '.js';
  if (!path.startsWith(normalize(root)) || !existsSync(path) || !statSync(path).isFile()) {
    response.writeHead(404).end();
    return;
  }
  response.writeHead(200, {
    'content-type': types.get(extname(path)) ?? 'application/octet-stream',
    'content-security-policy': contentSecurityPolicy,
    'cache-control': 'no-store',
  });
  createReadStream(path).pipe(response);
});

await new Promise((resolvePromise, reject) => {
  server.once('error', reject);
  server.listen(0, '127.0.0.1', resolvePromise);
});

const browser = await chromium.launch({ headless: true });
try {
  const port = server.address().port;
  const page = await browser.newPage();
  await page.goto(`http://127.0.0.1:${port}/bootstrap.html`);
  const result = await page.evaluate(async () => {
    const module = await import('./http.js');
    const { WASIShim } = await import('@bytecodealliance/preview2-shim/instantiation');
    const { platformProviderCatalog } = await import(
      '/netwasm-hosting/platform-provider-catalog.generated.mjs');
    const { createPreview2PlatformProviderRegistrations } = await import(
      '/netwasm-hosting/preview2-platform-provider-registrations.mjs');
    const { projectProviderModuleIdentity } = await import(
      '/netwasm-hosting/provider-module-identity.mjs');
    const getCoreModule = async path => WebAssembly.compile(
      await (await fetch(new URL(path, location.href))).arrayBuffer());
    const registrations = createPreview2PlatformProviderRegistrations({
      catalog: platformProviderCatalog,
      createShim: config => new WASIShim(config),
    });
    const output = Object.freeze({ write() {} });

    const runInstance = async instanceId => {
      let instance;
      const providerRequest = Object.freeze({
        arguments: Object.freeze([]),
        environment: Object.freeze([]),
        filesystem: null,
        grants: Object.freeze({
          clocks: Object.freeze(['monotonic']),
          environment: Object.freeze([]),
          network: 'allowAll',
          preopens: Object.freeze([]),
          randomness: false,
        }),
        signal: null,
        stderr: output,
        stdout: output,
      });
      const imports = Object.create(null);
      for (const registration of registrations) {
        const identity = projectProviderModuleIdentity({
          module: registration.provider.module,
        });
        imports[identity.componentModule] = registration.createSource(providerRequest);
      }
      const pending = new Map();
      let watched = 0;
      let cancelled = 0;
      const reactorHost = {
        watch(pollable, token) {
          watched++;
          let active = true;
          const release = () => {
            if (!active) return;
            active = false;
            pending.delete(token);
            pollable[Symbol.dispose]?.();
          };
          pending.set(token, release);
          pollable.block().then(() => {
            if (!active) return;
            release();
            instance.reactorGuest.wake(token);
          });
        },
        cancel(token) {
          cancelled++;
          const release = pending.get(token);
          if (!release) throw new Error(`unknown reactor token ${token}`);
          release();
        },
      };
      imports['netwasm:runtime/reactor-host'] = reactorHost;
      instance = await module.instantiate(getCoreModule, imports);
      const acceptance = instance.acceptance;
      const waitFor = async predicate => {
        for (let index = 0; index < 500 && !predicate(); index++) {
          await new Promise(resolvePromise => setTimeout(resolvePromise, 5));
        }
        return predicate();
      };
      const endpoint = `${location.origin}/http-qualification/${instanceId}`;

      if (!acceptance.qualificationDispose()) {
        throw new Error('managed disposal probe failed');
      }
      if (!acceptance.qualificationStart(endpoint) || acceptance.qualificationIsCompleted()) {
        throw new Error('qualification did not suspend');
      }
      if (pending.size !== 1 || watched !== 1) throw new Error('missing cancellation pollable');
      if (!acceptance.qualificationCancel() ||
          !(await waitFor(() => acceptance.qualificationIsCompleted()))) {
        throw new Error('qualification cancellation did not complete');
      }
      if (pending.size !== 0 || cancelled !== 1) throw new Error('cancelled pollable leaked');

      if (!acceptance.qualificationStart(endpoint) || acceptance.qualificationIsCompleted()) {
        throw new Error('qualification did not suspend for delayed headers');
      }
      if (pending.size !== 1 || watched !== 2) throw new Error('missing header pollable');
      if (!(await waitFor(() => acceptance.qualificationIsCompleted())) ||
          acceptance.qualificationResult() !== 0x3ffn) {
        throw new Error('qualification result mismatch');
      }
      if (!acceptance.qualificationDispose()) throw new Error('managed disposal probe failed');
      const counters = acceptance.qualificationCounters();
      if (counters !== 0x0101010201n) throw new Error(`counter mismatch ${counters}`);
      if (pending.size !== 0 || cancelled !== 1) throw new Error('completed pollable leaked');
      return { counters, watched, cancelled };
    };

    const first = await runInstance('first');
    const second = await runInstance('second');
    let denied = false;
    try {
      await module.instantiate(getCoreModule, {});
    } catch {
      denied = true;
    }
    return { first, second, denied };
  });

  if (!result.denied || result.first.counters !== 0x0101010201n ||
      result.second.counters !== 0x0101010201n ||
      result.first.watched < 4 || result.second.watched < 4 ||
      result.first.cancelled !== 1 || result.second.cancelled !== 1) {
    throw new Error(`unexpected browser observation ${JSON.stringify(result)}`);
  }
  console.log('http qualification jco Chromium PASS contract=0x3ff counters=0x101010201');
} finally {
  await browser.close();
  await new Promise(resolvePromise => server.close(resolvePromise));
}
