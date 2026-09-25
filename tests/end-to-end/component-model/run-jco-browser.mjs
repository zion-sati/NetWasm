import { createReadStream, existsSync, statSync } from 'node:fs';
import { createServer } from 'node:http';
import { extname, join, normalize } from 'node:path';
import { chromium } from 'playwright';

const [packageDirectory] = process.argv.slice(2);
if (!packageDirectory) {
  throw new Error('usage: node run-jco-browser.mjs <package-directory>');
}

const types = new Map([
  ['.js', 'text/javascript; charset=utf-8'],
  ['.wasm', 'application/wasm'],
]);
const server = createServer((request, response) => {
  const requestPath = new URL(request.url, 'http://localhost').pathname;
  const relative = normalize(requestPath).replace(/^[/\\]+/, '');
  const path = join(packageDirectory, relative || 'index.html');
  if (!path.startsWith(normalize(packageDirectory)) || !existsSync(path) ||
      !statSync(path).isFile()) {
    response.writeHead(404).end();
    return;
  }
  response.writeHead(200, {
    'content-type': types.get(extname(path)) ?? 'application/octet-stream',
    'content-security-policy': "default-src 'none'; script-src 'self' 'wasm-unsafe-eval'; connect-src 'self'",
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
  await page.goto(`http://127.0.0.1:${port}/scalar.js`);
  const result = await page.evaluate(async () => {
    const module = await import('./scalar.js');
    const getCoreModule = async path => WebAssembly.compile(
      await (await fetch(new URL(path, location.href))).arrayBuffer());
    const first = await module.instantiate(getCoreModule, {
      increment: { default: value => value + 1 },
    });
    const second = await module.instantiate(getCoreModule, {
      increment: { default: value => value + 2 },
    });
    let denied = false;
    try {
      await module.instantiate(getCoreModule, {});
    } catch {
      denied = true;
    }
    return {
      first: first.run(41),
      second: second.run(40),
      repeated: first.run(9),
      denied,
    };
  });
  if (result.first !== 42 || result.second !== 42 || result.repeated !== 10 ||
      !result.denied) {
    throw new Error(`unexpected jco browser result ${JSON.stringify(result)}`);
  }
  console.log('jco Chromium component PASS');
} finally {
  await browser.close();
  await new Promise(resolve => server.close(resolve));
}
