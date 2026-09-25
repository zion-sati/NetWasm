import { createReadStream, existsSync, readFileSync, statSync } from 'node:fs';
import { createServer } from 'node:http';
import { extname, join, normalize, resolve } from 'node:path';
import { chromium } from 'playwright';

const [packageDirectory, desktopOraclePath] = process.argv.slice(2);
const expected = 0x0123_4567_89ab_cdefn;
const expectedTrace = (2n << 16n) | (1n << 8n) | 0xffn | (1n << 24n);
const expectedMetrics = (13n << 32n) | 13n;
const expectedBoundary = 0x07n;
const shimDirectory = resolve(process.cwd(), 'node_modules/@bytecodealliance/preview2-shim');
const importMapNonce = 'diagnostics-import-map';
const contentSecurityPolicy = `default-src 'none'; script-src 'self' 'nonce-${importMapNonce}' 'wasm-unsafe-eval'; connect-src 'self'`;
if (!packageDirectory) {
  throw new Error('usage: node run-jco-diagnostics-browser.mjs <package-directory> <desktop-oracle-json>');
}
if (!desktopOraclePath) {
  throw new Error('desktop oracle identity is required');
}
const desktopOracle = JSON.parse(readFileSync(desktopOraclePath, 'utf8'));
if (desktopOracle.status !== 'passed' || Number(desktopOracle.contract) !== 0x3f) {
  throw new Error('desktop oracle identity is not executable');
}

const types = new Map([
  ['.html', 'text/html; charset=utf-8'],
  ['.js', 'text/javascript; charset=utf-8'],
  ['.wasm', 'application/wasm'],
]);
const server = createServer((request, response) => {
  const requestPath = new URL(request.url, 'http://localhost').pathname;
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
        '@bytecodealliance/preview2-shim/filesystem': '/preview2-shim/dist/browser/filesystem.js',
      },
    })}</script>`);
    return;
  }
  const relative = normalize(requestPath).replace(/^[/\\]+/, '');
  const root = relative.startsWith('preview2-shim/') ? shimDirectory : packageDirectory;
  const path = join(root, relative.startsWith('preview2-shim/')
    ? relative.slice('preview2-shim/'.length)
    : relative || 'index.html');
  if (!path.startsWith(normalize(root)) || !existsSync(path) ||
      !statSync(path).isFile()) {
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

await new Promise((resolve, reject) => {
  server.once('error', reject);
  server.listen(0, '127.0.0.1', resolve);
});

const browser = await chromium.launch({ headless: true });
try {
  const port = server.address().port;
  const page = await browser.newPage();
  await page.goto(`http://127.0.0.1:${port}/bootstrap.html`);
  const result = await page.evaluate(async expectedValue => {
    const module = await import('./diagnostics.js');
    const { WASIShim } = await import('@bytecodealliance/preview2-shim/instantiation');
    const getCoreModule = async path => WebAssembly.compile(
      await (await fetch(new URL(path, location.href))).arrayBuffer());
    const bytes = length => new Uint8Array(Number(length)).fill(0x5a);
    const makeImports = () => {
      let instance;
      const reactorHost = {
        watch(_pollable, token) {
          queueMicrotask(() => instance?.reactorGuest.wake(token));
        },
        cancel() {},
      };
      const random = {
        random: { getRandomU64: () => expectedValue, getRandomBytes: bytes },
        insecure: { getInsecureRandomU64: () => expectedValue, getInsecureRandomBytes: bytes },
        insecureSeed: { insecureSeed: () => [expectedValue, expectedValue] },
      };
      const shim = new WASIShim({
        random,
        sandbox: { preopens: {}, env: {}, args: [], enableNetwork: false },
      });
      return {
        imports: {
          ...shim.getImportObject(),
          ...shim.getImportObject({ asVersion: '0.2' }),
          ...shim.getImportObject({ asVersion: '0.2.11' }),
          'netwasm:runtime/reactor-host': reactorHost,
        },
        setInstance(value) {
          instance = value;
        },
      };
    };
    const firstImports = makeImports();
    const first = await module.instantiate(getCoreModule, firstImports.imports);
    firstImports.setInstance(first);
    const secondImports = makeImports();
    const second = await module.instantiate(getCoreModule, secondImports.imports);
    secondImports.setInstance(second);
    const observe = instance => ({
      sample: instance.acceptance.sample(),
      activity: instance.acceptance.traceActivity(),
      diagnostic: instance.acceptance.traceDiagnostic(),
      current: instance.acceptance.traceCurrent(),
      trace: instance.acceptance.trace(),
      metricsSynchronous: instance.acceptance.metricsSynchronous(),
      metricsListener: instance.acceptance.metricsListener(),
      metricsCounter: instance.acceptance.metricsCounter(),
      metricsGauge: instance.acceptance.metricsGauge(),
      metricsUpDown: instance.acceptance.metricsUpDown(),
      metricsHistogram: instance.acceptance.metricsHistogram(),
      metricsObservableValue: instance.acceptance.metricsObservableValue(),
      metricsObservableMeasurement: instance.acceptance.metricsObservableMeasurement(),
      metricsObservableSequence: instance.acceptance.metricsObservableSequence(),
      metrics: instance.acceptance.metrics(),
      boundary: instance.acceptance.boundary(),
      contract: instance.acceptance.qualificationContract(),
    });
    let denied = false;
    try {
      await module.instantiate(getCoreModule, {});
    } catch {
      denied = true;
    }
    return {
      first: observe(first),
      second: observe(second),
      denied,
    };
  }, expected);
  const valid = observation => observation.sample === expected &&
    observation.activity === 1n && observation.diagnostic === 1n &&
    observation.current === 1n &&
    observation.metricsListener === 0n &&
    observation.metricsCounter === ((1n << 32n) | 1n) &&
    observation.metricsGauge === ((1n << 32n) | 1n) &&
    observation.metricsUpDown === ((1n << 32n) | 1n) &&
    observation.metricsHistogram === ((1n << 32n) | 1n) &&
    observation.metricsSynchronous === ((4n << 32n) | 4n) &&
    observation.metricsObservableValue === ((3n << 32n) | 3n) &&
    observation.metricsObservableMeasurement === ((3n << 32n) | 3n) &&
    observation.metricsObservableSequence === ((3n << 32n) | 3n) &&
    observation.trace === expectedTrace && observation.metrics === expectedMetrics &&
    observation.contract === 0x3fn &&
    observation.boundary === expectedBoundary;
  if (!valid(result.first) || !valid(result.second) || !result.denied) {
    throw new Error('diagnostics browser observation mismatch');
  }
  console.log('diagnostics jco browser PASS contract=0x3f');
} finally {
  await browser.close();
  await new Promise(resolve => server.close(resolve));
}
