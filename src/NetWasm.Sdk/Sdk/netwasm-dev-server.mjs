// dotnet run and dotnet watch on a browser project: a development server that serves the
// project's site on the loopback interface for the whole session, as an npm dev server does.
// The site is the map the build writes: every file publish would copy, at the path it is served
// at, served from where it is. The run command is a launcher. The first one starts the server,
// detached from itself, so dotnet watch, which restarts the run command after every rebuild,
// never stops it; later ones join the session. The server exits when every process that joined
// it has exited, and reloads the pages it served whenever the build writes the map again. A
// development server only: files are read on every request and never cached.

import { execFile, spawn } from "node:child_process";
import { randomBytes } from "node:crypto";
import { createReadStream, unwatchFile, watchFile } from "node:fs";
import { readFile, rename, rm, stat, writeFile } from "node:fs/promises";
import { createServer } from "node:http";
import { extname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const defaultPort = 5080;
const command = fileURLToPath(new URL("./netwasm-serve.mjs", import.meta.url));
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

// A page the session serves listens for the server's reloads.
const reloadScript =
  "<script type=\"module\">new EventSource(\"/_netwasm/events\").addEventListener(\"reload\", () => location.reload());</script>";

// Where a session over a site keeps its state: beside the site's map.
export const sessionStatePath = site => resolve(site) + ".server.json";

// The site's map: each path, and the file served there.
export async function readSite(path) {
  const value = JSON.parse(await readFile(path, "utf8"));
  const files = value?.files;
  if (typeof files !== "object" || files === null || Array.isArray(files)
      || Object.values(files).some(file => typeof file !== "string")) {
    throw new Error(`${path} is not a site map`);
  }
  return new Map(Object.entries(files));
}

// A session: its token, the processes that joined it, and the pages listening for reloads.
export function createSession(token) {
  return { token, owners: new Set(), pages: new Set() };
}

export function reloadPages(session) {
  for (const page of session.pages) page.write("event: reload\ndata: \n\n");
}

// Serves the files a site's map names; `site.files` is the map, which the session replaces when
// the build writes it again. A path that names a folder serves the folder's index.html.
export function createSiteServer(site, session = null) {
  return createServer(async (request, response) => {
    if (session !== null && request.url.startsWith("/_netwasm/")) {
      await answerSession(request, response, session, site);
      return;
    }
    if (request.method !== "GET" && request.method !== "HEAD") {
      response.writeHead(405, { allow: "GET, HEAD" }).end();
      return;
    }
    let path;
    try {
      path = decodeURIComponent(new URL(request.url, "http://localhost").pathname).slice(1);
    } catch {
      response.writeHead(400).end();
      return;
    }
    const file = site.files.get(path === "" || path.endsWith("/") ? path + "index.html" : path);
    if (file === undefined) {
      // A page in a folder resolves its relative URLs against the folder.
      if (path !== "" && !path.endsWith("/") && [...site.files.keys()].some(key => key.startsWith(path + "/"))) {
        response.writeHead(301, { location: "/" + path + "/" }).end();
        return;
      }
      notFound(response);
      return;
    }
    try {
      const info = await stat(file);
      if (!info.isFile()) throw new Error("not a file");
      const type = types.get(extname(file).toLowerCase()) ?? "application/octet-stream";
      if (session !== null && type.startsWith("text/html")) {
        const page = withReload(await readFile(file, "utf8"));
        response.writeHead(200, { "cache-control": "no-store", "content-length": Buffer.byteLength(page), "content-type": type });
        response.end(request.method === "HEAD" ? undefined : page);
        return;
      }
      response.writeHead(200, { "cache-control": "no-store", "content-length": info.size, "content-type": type });
      if (request.method === "HEAD") response.end();
      else createReadStream(file).pipe(response);
    } catch {
      notFound(response);
    }
  });
}

function withReload(page) {
  const head = page.search(/<\/head>/iu);
  return head < 0 ? page + reloadScript : page.slice(0, head) + reloadScript + page.slice(head);
}

// The session's own requests: who it is, a process joining it, and a page's reloads.
async function answerSession(request, response, session, site) {
  const route = `${request.method} ${request.url}`;
  if (route === "GET /_netwasm/session") {
    response.writeHead(200, { "cache-control": "no-store", "content-type": "application/json" })
      .end(JSON.stringify({ token: session.token, site: site.path }));
  } else if (route === "POST /_netwasm/owners") {
    let body = "";
    for await (const chunk of request) body += chunk;
    if (!/^\d{1,10}$/u.test(body) || Number(body) === 0) {
      response.writeHead(400).end();
      return;
    }
    session.owners.add(Number(body));
    response.writeHead(204).end();
  } else if (route === "GET /_netwasm/events") {
    response.writeHead(200, { "cache-control": "no-store", "content-type": "text/event-stream" });
    response.write(": connected\n\n");
    session.pages.add(response);
    request.once("close", () => session.pages.delete(response));
  } else {
    notFound(response);
  }
}

function notFound(response) {
  response.writeHead(404, { "content-type": "text/plain; charset=utf-8" }).end("Not found\n");
}

// The processes the launcher runs: the server, detached from it, and the commands that name a
// process's parent.
export const processHost = {
  environment: process.env,
  parent: process.ppid,
  platform: process.platform,
  start: (file, arguments_) => spawn(file, arguments_, { detached: true, stdio: "ignore" }).unref(),
  run: (file, arguments_) => new Promise((resolveOutput, reject) =>
    execFile(file, arguments_, (error, output) => (error ? reject(error) : resolveOutput(output)))),
  readText: path => readFile(path, "utf8"),
  // How long a launcher waits for the server it started to listen.
  startup: 15000,
  // A process exists while it can be signalled, or refuses to be.
  alive: pid => {
    try {
      process.kill(pid, 0);
      return true;
    } catch (error) {
      return error.code === "EPERM";
    }
  },
};

// Usage: netwasm-serve.mjs --site <file> [--port <port>]. --site names the map the build wrote.
// Without --port the server takes 5080, or a free port when 5080 is in use. The launcher returns
// true: it waits until it is stopped. Its --detach and --server forms are the session's own.
export async function run(arguments_, log, host = processHost) {
  const options = readArguments(arguments_);
  if (options.mode === "detach") {
    host.start(process.execPath, [command, "--server", ...arguments_.slice(1)]);
    return false;
  }
  if (options.mode === "server") {
    await serveSession(options, host);
    return false;
  }
  const state = await launch(options, host);
  log(`Now listening on: http://localhost:${state.port}/`);
  log(`Serving ${options.site}. Press Ctrl+C to stop.`);
  return true;
}

// The launcher: joins the session over the site, or starts one.
export async function launch(options, host) {
  const owner = await sessionOwner(host);
  const files = await readSite(options.site).catch(error => {
    if (error.code === "ENOENT") {
      throw new Error(`${options.site} is missing: dotnet run serves the site the project's build maps, so build the project`);
    }
    throw error;
  });
  if (!files.has("index.html")) {
    throw new Error("NWSDK052: dotnet run serves the page this project publishes, but it publishes no index.html. Add the "
      + "page, such as <Content Include=\"index.html\" CopyToPublishDirectory=\"PreserveNewest\" />, or run the project "
      + "whose page uses this one.");
  }
  const state = await liveSession(options.site) ?? await startSession(options, owner, host);
  const joined = await fetch(`http://127.0.0.1:${state.port}/_netwasm/owners`, { method: "POST", body: String(owner) });
  if (!joined.ok) throw new Error(`the development server on port ${state.port} refused to join the session`);
  return state;
}

// The process whose session this is: dotnet run, which starts the launcher, or dotnet watch,
// which starts dotnet run again after every rebuild.
export async function sessionOwner(host) {
  if (host.environment.DOTNET_WATCH !== "1") return host.parent;
  try {
    return await parentOf(host.parent, host);
  } catch {
    return host.parent;
  }
}

export async function parentOf(pid, host) {
  let parent;
  if (host.platform === "linux") {
    // pid (comm) state ppid ...: the command name may hold spaces and parentheses.
    const status = await host.readText(`/proc/${pid}/stat`);
    parent = status.slice(status.lastIndexOf(")") + 2).split(" ")[1];
  } else if (host.platform === "win32") {
    parent = await host.run("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command",
      `(Get-CimInstance Win32_Process -Filter "ProcessId=${pid}").ParentProcessId`]);
  } else {
    parent = await host.run("ps", ["-o", "ppid=", "-p", String(pid)]);
  }
  const value = Number(String(parent).trim());
  if (!Number.isSafeInteger(value) || value <= 0) throw new Error(`process ${pid} has no parent`);
  return value;
}

// The session the state beside the site names, when its server answers for this site.
export async function liveSession(site) {
  try {
    const state = JSON.parse(await readFile(sessionStatePath(site), "utf8"));
    const answer = await fetch(`http://127.0.0.1:${state.port}/_netwasm/session`, { signal: AbortSignal.timeout(2000) });
    const session = await answer.json();
    return session.token === state.token && session.site === resolve(site) ? state : null;
  } catch {
    return null;
  }
}

async function startSession(options, owner, host) {
  const token = randomBytes(16).toString("hex");
  await rm(sessionStatePath(options.site), { force: true });
  host.start(process.execPath, [command, "--detach", "--site", options.site, "--owner", String(owner), "--token", token,
    ...(options.port === null ? [] : ["--port", String(options.port)])]);
  for (const deadline = Date.now() + host.startup; Date.now() < deadline;) {
    const state = await readFile(sessionStatePath(options.site), "utf8").then(JSON.parse, () => null);
    if (state?.token === token) {
      if (typeof state.error === "string") throw new Error(state.error);
      return state;
    }
    await new Promise(resolveDelay => setTimeout(resolveDelay, 20));
  }
  throw new Error("the development server did not start");
}

// The server: serves the site until every process that joined the session has exited, and
// reloads its pages whenever the build writes the site's map again. It returns the session and
// how to stop it.
export async function serveSession(options, host) {
  const session = createSession(options.token);
  session.owners.add(options.owner);
  const site = { path: resolve(options.site), files: await readSite(options.site) };
  const server = createSiteServer(site, session);
  let port;
  try {
    port = await listen(server, options.port ?? defaultPort).catch(error => {
      if (options.port === null && error.code === "EADDRINUSE") return listen(server, 0);
      throw error;
    });
  } catch (error) {
    await writeState(options.site, { token: options.token, error: `the development server cannot listen: ${error.message}` });
    throw error;
  }
  // A map that does not read yet is being written; the next change reads it.
  watchFile(site.path, { interval: 100 }, (current, previous) => {
    if (current.mtimeMs === 0 || current.mtimeMs === previous.mtimeMs) return;
    readSite(site.path).then(files => {
      site.files = files;
      reloadPages(session);
    }, () => {});
  });
  let stopped;
  const done = new Promise(resolveStopped => { stopped = resolveStopped; });
  const monitor = setInterval(() => {
    for (const owner of session.owners) {
      if (!host.alive(owner)) session.owners.delete(owner);
    }
    if (session.owners.size === 0) void stop();
  }, 250);
  const stop = async () => {
    clearInterval(monitor);
    unwatchFile(site.path);
    server.closeAllConnections();
    await new Promise(resolveClosed => server.close(resolveClosed));
    const state = await readFile(sessionStatePath(options.site), "utf8").then(JSON.parse, () => null);
    if (state?.token === options.token) await rm(sessionStatePath(options.site), { force: true });
    stopped();
  };
  await writeState(options.site, { token: options.token, port, pid: process.pid });
  return { session, server, port, stop, done };
}

async function writeState(site, state) {
  const path = sessionStatePath(site);
  await writeFile(path + ".tmp", JSON.stringify(state));
  await rename(path + ".tmp", path);
}

const usage = "usage: netwasm-serve.mjs --site <file> [--port <port>]";

function readArguments(values) {
  const options = { mode: "launch", site: null, port: null, owner: null, token: null };
  let index = 0;
  if (values[0] === "--detach" || values[0] === "--server") {
    options.mode = values[0].slice(2);
    index = 1;
  }
  const own = options.mode !== "launch";
  for (; index < values.length; index += 2) {
    const [name, value] = [values[index], values[index + 1]];
    if (name === "--site" && value !== undefined && options.site === null) options.site = resolve(value);
    else if (name === "--port" && /^\d{1,5}$/u.test(value ?? "") && Number(value) <= 65535 && options.port === null) options.port = Number(value);
    else if (name === "--owner" && /^\d{1,10}$/u.test(value ?? "") && options.owner === null && own) options.owner = Number(value);
    else if (name === "--token" && /^[0-9a-f]{32}$/u.test(value ?? "") && options.token === null && own) options.token = value;
    else throw new Error(usage);
  }
  if (options.site === null || (own && (options.owner === null || options.token === null))) throw new Error(usage);
  return options;
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
