// dotnet run on a browser project: serves the project's staged publish output
// on the loopback interface until it is stopped. A development server only:
// files are read on every request and never cached.

import { spawn } from "node:child_process";
import { createReadStream } from "node:fs";
import { readFile, stat } from "node:fs/promises";
import { createServer } from "node:http";
import { extname, isAbsolute, join, relative, resolve } from "node:path";

const defaultPort = 5080;
const types = new Map(Object.entries({
  ".css": "text/css; charset=utf-8",
  ".gif": "image/gif",
  ".html": "text/html; charset=utf-8",
  ".ico": "image/x-icon",
  ".jpeg": "image/jpeg",
  ".jpg": "image/jpeg",
  ".js": "text/javascript; charset=utf-8",
  ".json": "application/json",
  ".map": "application/json",
  ".mjs": "text/javascript; charset=utf-8",
  ".png": "image/png",
  ".svg": "image/svg+xml",
  ".txt": "text/plain; charset=utf-8",
  ".wasm": "application/wasm",
  ".webp": "image/webp",
  ".woff2": "font/woff2",
}));

export function createDeploymentServer(root) {
  const base = resolve(root);
  return createServer(async (request, response) => {
    if (request.method !== "GET" && request.method !== "HEAD") {
      response.writeHead(405, { allow: "GET, HEAD" }).end();
      return;
    }
    let path;
    try {
      path = decodeURIComponent(new URL(request.url, "http://localhost").pathname);
    } catch {
      response.writeHead(400).end();
      return;
    }
    const target = resolve(base, "." + path);
    const inside = relative(base, target);
    if (inside.startsWith("..") || isAbsolute(inside)) {
      notFound(response);
      return;
    }
    try {
      let file = target;
      let info = await stat(file);
      if (info.isDirectory()) {
        // A page in a directory resolves its relative URLs against the directory.
        if (!path.endsWith("/")) {
          response.writeHead(301, { location: path + "/" }).end();
          return;
        }
        file = join(target, "index.html");
        info = await stat(file);
      }
      if (!info.isFile()) throw new Error("not a file");
      response.writeHead(200, {
        "cache-control": "no-store",
        "content-length": info.size,
        "content-type": types.get(extname(file).toLowerCase()) ?? "application/octet-stream",
      });
      if (request.method === "HEAD") response.end();
      else createReadStream(file).pipe(response);
    } catch {
      notFound(response);
    }
  });
}

function notFound(response) {
  response.writeHead(404, { "content-type": "text/plain; charset=utf-8" }).end("Not found\n");
}

// Usage: netwasm-serve.mjs --root <directory> [--stage <file>] [--port <port>].
// --stage names the command that publishes the site, which runs first.
// Without --port it takes 5080, or a free port when 5080 is in use.
export async function serve(arguments_, log) {
  const options = readArguments(arguments_);
  if (options.stage !== null) await stage(options.stage);
  const info = await stat(options.root).catch(() => null);
  if (info === null || !info.isDirectory()) throw new Error(`${options.root} is not a directory`);
  const server = createDeploymentServer(options.root);
  const port = await listen(server, options.port ?? defaultPort).catch(error => {
    if (options.port === null && error.code === "EADDRINUSE") return listen(server, 0);
    throw error;
  });
  log(`Now listening on: http://localhost:${port}/`);
  log(`Serving ${options.root}. Press Ctrl+C to stop.`);
  return server;
}

const usage = "usage: netwasm-serve.mjs --root <directory> [--stage <file>] [--port <port>]";

function readArguments(values) {
  let root = null;
  let stagePath = null;
  let port = null;
  for (let index = 0; index < values.length; index += 2) {
    const [name, value] = [values[index], values[index + 1]];
    if (name === "--root" && value !== undefined && root === null) root = resolve(value);
    else if (name === "--stage" && value !== undefined && stagePath === null) stagePath = resolve(value);
    else if (name === "--port" && /^\d{1,5}$/u.test(value ?? "") && Number(value) <= 65535 && port === null) port = Number(value);
    else throw new Error(usage);
  }
  if (root === null) throw new Error(usage);
  return { root, stage: stagePath, port };
}

// The staging command, which the SDK writes as { command, arguments }, runs
// with this process's console, so its diagnostics reach the developer.
async function stage(path) {
  const value = JSON.parse(await readFile(path, "utf8"));
  if (typeof value?.command !== "string" || !Array.isArray(value.arguments)
      || value.arguments.some(argument => typeof argument !== "string")) {
    throw new Error(`${path} is not a staging command`);
  }
  const code = await new Promise((resolveCode, reject) => {
    const child = spawn(value.command, value.arguments, { stdio: "inherit" });
    child.once("error", reject);
    child.once("exit", resolveCode);
  });
  if (code !== 0) throw new Error(`publishing the site failed with exit code ${code}`);
}

function listen(server, port) {
  return new Promise((resolvePort, reject) => {
    const failed = error => reject(error);
    server.once("error", failed);
    server.listen(port, "127.0.0.1", () => {
      server.off("error", failed);
      resolvePort(server.address().port);
    });
  });
}
