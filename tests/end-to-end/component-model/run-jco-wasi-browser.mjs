import { createReadStream, existsSync, statSync } from 'node:fs';
import { createServer } from 'node:http';
import { extname, join, normalize } from 'node:path';
import { chromium } from 'playwright';

const [packageDirectory] = process.argv.slice(2);
if (!packageDirectory) {
  throw new Error('usage: node run-jco-wasi-browser.mjs <package-directory>');
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
  await page.goto(`http://127.0.0.1:${port}/wasi.js`);
  const result = await page.evaluate(async () => {
    const module = await import('./wasi.js');
    const getCoreModule = async path => WebAssembly.compile(
      await (await fetch(new URL(path, location.href))).arrayBuffer());
    const random = { getRandomU64: () => 0x0123_4567_89ab_cdefn };
    const imports = {
      'wasi:random/random': random,
      'wasi:random/random@0.2.11': random,
    };
    const first = await module.instantiate(getCoreModule, imports);
    const second = await module.instantiate(getCoreModule, imports);
    let denied = false;
    try {
      await module.instantiate(getCoreModule, {});
    } catch {
      denied = true;
    }
    return {
      first: first.acceptance.sample(),
      second: second.acceptance.sample(),
      denied,
    };
  });
  if (result.first !== 0x0123_4567_89ab_cdefn ||
      result.second !== 0x0123_4567_89ab_cdefn || !result.denied) {
    throw new Error(
      `unexpected WASI browser result first=${result.first} ` +
      `second=${result.second} denied=${result.denied}`);
  }
  console.log('jco WASI Chromium component PASS');
} finally {
  await browser.close();
  await new Promise(resolve => server.close(resolve));
}
