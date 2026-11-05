import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { chmod, mkdir, mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { createServer as createHttpServer } from "node:http";
import { createServer } from "node:net";
import { tmpdir } from "node:os";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

import {
  createSession, createSiteServer, launch, liveSession, parentOf, processHost, readSite, reloadPages, run, serveSession,
  sessionOwner, sessionStatePath,
} from "./netwasm-dev-server.mjs";

const command = fileURLToPath(new URL("./netwasm-serve.mjs", import.meta.url));
const token = "0123456789abcdef0123456789abcdef";
const reloadScript =
  "<script type=\"module\">new EventSource(\"/_netwasm/events\").addEventListener(\"reload\", () => location.reload());</script>";

// A site: its files where the build left them, and the map that names them.
async function withSite(action) {
  const root = await mkdtemp(path.join(tmpdir(), "netwasm-serve-"));
  try {
    await mkdir(path.join(root, "obj", "nested"), { recursive: true });
    await mkdir(path.join(root, "obj", "folder"), { recursive: true });
    await writeFile(path.join(root, "page.html"), "<!doctype html><p>page</p>");
    await writeFile(path.join(root, "obj", "Calc.mjs"), "export const value = 42;");
    await writeFile(path.join(root, "obj", "app.wasm"), Uint8Array.of(0, 0x61, 0x73, 0x6d));
    await writeFile(path.join(root, "obj", "data.BIN"), "bytes");
    await writeFile(path.join(root, "obj", "nested", "page.html"), "<p>nested</p>");
    const site = path.join(root, "site.json");
    await writeMap(site, {
      "index.html": path.join(root, "page.html"),
      "browser/Calc.mjs": path.join(root, "obj", "Calc.mjs"),
      "browser/app.wasm": path.join(root, "obj", "app.wasm"),
      "browser/data.BIN": path.join(root, "obj", "data.BIN"),
      "browser/nested/index.html": path.join(root, "obj", "nested", "page.html"),
      "browser/odd/index.html": path.join(root, "obj", "folder"),
      "browser/gone.js": path.join(root, "obj", "gone.js"),
    });
    return await action(site, root);
  } finally {
    await rm(root, { recursive: true, force: true });
  }
}

const writeMap = (site, files) => writeFile(site, JSON.stringify({ version: 1, files }));

async function started(server) {
  await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
  return `http://127.0.0.1:${server.address().port}`;
}

const closed = server => new Promise(resolve => server.close(resolve));

async function until(condition, what, timeout = 5000) {
  for (const deadline = Date.now() + timeout; !(await condition());) {
    if (Date.now() > deadline) throw new Error(`timed out waiting for ${what}`);
    await new Promise(resolve => setTimeout(resolve, 20));
  }
}

const delay = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));

// A process that owns a session until it is killed.
function owner() {
  return spawn(process.execPath, ["-e", "setInterval(() => {}, 1 << 30)"], { stdio: "ignore" });
}

async function killed(child) {
  if (child.exitCode !== null || child.signalCode !== null) return;
  const exited = new Promise(resolve => child.once("exit", resolve));
  child.kill("SIGKILL");
  await exited;
}

// A page's reload events, as they arrive.
function listen(response) {
  const page = { text: "" };
  (async () => {
    const decoder = new TextDecoder();
    for await (const chunk of response.body) page.text += decoder.decode(chunk, { stream: true });
  })().catch(() => {});
  return page;
}

const reloads = page => page.text.split("event: reload").length - 1;

const state = site => readFile(sessionStatePath(site), "utf8").then(JSON.parse, () => null);

const siteOf = async site => ({ path: path.resolve(site), files: await readSite(site) });

// Issue #140: dotnet run on a page project serves what it publishes.
test("serves the site's files with browser content types and no caching", async () => {
  await withSite(async site => {
    const server = createSiteServer(await siteOf(site));
    const origin = await started(server);
    try {
      const page = await fetch(origin + "/");
      assert.equal(page.status, 200);
      assert.equal(page.headers.get("content-type"), "text/html; charset=utf-8");
      assert.equal(page.headers.get("cache-control"), "no-store");
      assert.equal(await page.text(), "<!doctype html><p>page</p>");
      const module = await fetch(origin + "/browser/Calc.mjs");
      assert.equal(module.headers.get("content-type"), "text/javascript; charset=utf-8");
      assert.equal(await module.text(), "export const value = 42;");
      const wasm = await fetch(origin + "/browser/app.wasm");
      assert.equal(wasm.headers.get("content-type"), "application/wasm");
      assert.deepEqual(new Uint8Array(await wasm.arrayBuffer()), Uint8Array.of(0, 0x61, 0x73, 0x6d));
      assert.equal((await fetch(origin + "/browser/data.BIN")).headers.get("content-type"), "application/octet-stream");
      const head = await fetch(origin + "/browser/Calc.mjs", { method: "HEAD" });
      assert.equal(head.status, 200);
      assert.equal(head.headers.get("content-length"), "24");
      assert.equal(await head.text(), "");

      const directory = await fetch(origin + "/browser/nested", { redirect: "manual" });
      assert.equal(directory.status, 301);
      assert.equal(directory.headers.get("location"), "/browser/nested/");
      assert.equal(await (await fetch(origin + "/browser/nested/")).text(), "<p>nested</p>");
      assert.equal((await fetch(origin + "/_netwasm/session")).status, 404);
    } finally {
      await closed(server);
    }
  });
});

test("serves only what the map names, and only reads", async () => {
  await withSite(async site => {
    const server = createSiteServer(await siteOf(site));
    const origin = await started(server);
    try {
      for (const url of ["/missing.mjs", "/browser/", "/browser/odd/", "/browser/gone.js", "/..%2fsite.json",
        "/%2e%2e/page.html", "/browser%00.mjs", "/index"]) {
        assert.equal((await fetch(origin + url)).status, 404, url);
      }
      assert.equal((await fetch(origin + "/%E0%A4%A")).status, 400);
      const post = await fetch(origin + "/", { method: "POST", body: "x" });
      assert.equal(post.status, 405);
      assert.equal(post.headers.get("allow"), "GET, HEAD");
    } finally {
      await closed(server);
    }
  });
});

test("reads a site's map", async () => {
  await withSite(async (site, root) => {
    assert.equal((await readSite(site)).get("browser/Calc.mjs"), path.join(root, "obj", "Calc.mjs"));
    for (const value of [null, {}, { files: null }, { files: [] }, { files: { a: 1 } }]) {
      await writeFile(site, JSON.stringify(value));
      await assert.rejects(readSite(site), /is not a site map/u, JSON.stringify(value));
    }
  });
});

test("a session's pages listen for reloads and processes join it", async () => {
  await withSite(async (site, root) => {
    await writeFile(path.join(root, "head.html"), "<html><HEAD><title>t</title></HEAD><body></body></html>");
    await writeMap(site, { "index.html": path.join(root, "page.html"), "head.html": path.join(root, "head.html"),
      "browser/Calc.mjs": path.join(root, "obj", "Calc.mjs") });
    const session = createSession(token);
    const served = await siteOf(site);
    const server = createSiteServer(served, session);
    const origin = await started(server);
    try {
      assert.equal(await (await fetch(origin + "/")).text(), "<!doctype html><p>page</p>" + reloadScript);
      assert.equal(await (await fetch(origin + "/head.html")).text(),
        `<html><HEAD><title>t</title>${reloadScript}</HEAD><body></body></html>`);
      const head = await fetch(origin + "/", { method: "HEAD" });
      assert.equal(head.headers.get("content-length"), String(Buffer.byteLength("<!doctype html><p>page</p>" + reloadScript)));
      assert.equal(head.headers.get("cache-control"), "no-store");
      assert.equal(await head.text(), "");
      assert.equal(await (await fetch(origin + "/browser/Calc.mjs")).text(), "export const value = 42;");

      assert.deepEqual(await (await fetch(origin + "/_netwasm/session")).json(), { token, site: served.path });
      assert.equal((await fetch(origin + "/_netwasm/owners", { method: "POST", body: "123" })).status, 204);
      for (const body of ["x", "0", "12345678901", ""]) {
        assert.equal((await fetch(origin + "/_netwasm/owners", { method: "POST", body })).status, 400, body);
      }
      assert.deepEqual([...session.owners], [123]);
      for (const [method, url] of [["GET", "/_netwasm/owners"], ["POST", "/_netwasm/session"], ["GET", "/_netwasm/other"]]) {
        assert.equal((await fetch(origin + url, { method })).status, 404, url);
      }

      const leave = new AbortController();
      const events = await fetch(origin + "/_netwasm/events", { signal: leave.signal });
      assert.equal(events.headers.get("content-type"), "text/event-stream");
      const page = listen(events);
      await until(() => page.text.includes(": connected"), "the page to connect");
      reloadPages(session);
      await until(() => reloads(page) === 1, "a reload");
      leave.abort();
      await until(() => session.pages.size === 0, "the page to leave");
    } finally {
      server.closeAllConnections();
      await closed(server);
    }
  });
});

test("a session serves until every process that joined has exited, and reloads its pages when the build maps the site again", async () => {
  await withSite(async (site, root) => {
    const first = owner();
    try {
      const handle = await serveSession({ site, port: 0, owner: first.pid, token }, processHost);
      assert.deepEqual(await state(site), { token, port: handle.port, pid: process.pid });
      assert.deepEqual(await liveSession(site), await state(site));
      const origin = `http://127.0.0.1:${handle.port}`;
      const page = listen(await fetch(origin + "/_netwasm/events"));
      await until(() => page.text.includes(": connected"), "the page to connect");

      // The build maps the site again: the new map is served and the pages reload.
      await writeFile(path.join(root, "late.js"), "late");
      await writeMap(site, { "index.html": path.join(root, "page.html"), "late.js": path.join(root, "late.js") });
      await until(() => reloads(page) === 1, "a reload after the build");
      assert.equal(await (await fetch(origin + "/late.js")).text(), "late");
      assert.equal((await fetch(origin + "/browser/Calc.mjs")).status, 404);
      // A map that does not read, one whose time stays, or none, reloads nothing.
      await writeFile(site, "{");
      await delay(400);
      await chmod(site, 0o600);
      await delay(400);
      await rm(site);
      await delay(400);
      assert.equal(reloads(page), 1);
      assert.equal(await (await fetch(origin + "/late.js")).text(), "late");

      // A second process joins; the session lasts until both have exited.
      const second = owner();
      try {
        assert.equal((await fetch(origin + "/_netwasm/owners", { method: "POST", body: String(second.pid) })).status, 204);
        await killed(first);
        await delay(600);
        assert.equal((await fetch(origin + "/_netwasm/session")).status, 200);
      } finally {
        await killed(second);
      }
      await handle.done;
      assert.equal(await state(site), null);
      await assert.rejects(fetch(origin + "/"));
      assert.equal(await liveSession(site), null);
    } finally {
      await killed(first);
    }

    // A session leaves state that another session wrote, and none.
    await writeMap(site, { "index.html": path.join(root, "page.html") });
    for (const replace of [file => writeFile(file, JSON.stringify({ token: "f".repeat(32), port: 1 })), file => rm(file)]) {
      const process_ = owner();
      const handle = await serveSession({ site, port: 0, owner: process_.pid, token }, processHost);
      await replace(sessionStatePath(site));
      const kept = await state(site);
      await killed(process_);
      await handle.done;
      assert.deepEqual(await state(site), kept);
    }
    await rm(sessionStatePath(site), { force: true });
  });
});

test("a session listens on the requested port, 5080 by default or a free one when it is taken", async () => {
  await withSite(async site => {
    const process_ = owner();
    const blocker = createServer();
    await new Promise(resolve => blocker.listen(0, "127.0.0.1", resolve));
    const taken = blocker.address().port;
    try {
      await assert.rejects(serveSession({ site, port: taken, owner: process_.pid, token }, processHost), { code: "EADDRINUSE" });
      const failed = await state(site);
      assert.equal(failed.token, token);
      assert.match(failed.error, /^the development server cannot listen: .*EADDRINUSE/u);

      const fallback = await serveSession({ site, port: null, owner: process_.pid, token }, processHost);
      const second = await serveSession({ site, port: null, owner: process_.pid, token }, processHost);
      assert.notEqual(second.port, fallback.port);
      assert.equal(second.server.address().address, "127.0.0.1");
      await second.stop();
      await fallback.stop();
    } finally {
      await closed(blocker);
      await killed(process_);
    }
  });
});

// The launcher runs the session's detach and server steps through the host; here they run in
// this process.
function inProcessHost(parent, overrides = {}) {
  const host = {
    environment: {},
    parent,
    platform: process.platform,
    alive: processHost.alive,
    startup: 15000,
    started: [],
    start: (file, arguments_) => {
      host.started.push(arguments_[1]);
      run(arguments_.slice(1), () => {}, host).catch(() => {});
    },
    ...overrides,
  };
  return host;
}

test("a launcher starts a session over the site, or joins the live one", async () => {
  await withSite(async (site, root) => {
    const first = owner();
    const second = owner();
    try {
      const lines = [];
      const host = inProcessHost(first.pid);
      assert.equal(await run(["--site", site, "--port", "0"], line => lines.push(line), host), true);
      assert.deepEqual(host.started, ["--detach", "--server"]);
      const launched = await state(site);
      assert.deepEqual(lines, [`Now listening on: http://localhost:${launched.port}/`, `Serving ${site}. Press Ctrl+C to stop.`]);
      assert.equal(await (await fetch(`http://127.0.0.1:${launched.port}/`)).text(), "<!doctype html><p>page</p>" + reloadScript);

      // The live session: the launcher joins it.
      const joining = inProcessHost(second.pid);
      assert.deepEqual(await launch({ site, port: 0 }, joining), launched);
      assert.deepEqual(joining.started, []);
      await killed(first);
      await delay(600);
      assert.deepEqual(await liveSession(site), launched);
      await killed(second);
      await until(async () => (await state(site)) === null, "the session to end");

      // A site the build has not mapped, a map that does not read, and a map without a page.
      const lone = inProcessHost(process.pid);
      await assert.rejects(launch({ site: path.join(root, "missing.json"), port: 0 }, lone), /is missing: dotnet run serves/u);
      await assert.rejects(launch({ site: path.join(root, "obj"), port: 0 }, lone), { code: "EISDIR" });
      await writeMap(site, { "browser/app.wasm": path.join(root, "obj", "app.wasm") });
      await assert.rejects(launch({ site, port: 0 }, lone), /NWSDK052: dotnet run serves the page/u);
      assert.deepEqual(lone.started, []);
    } finally {
      await killed(first);
      await killed(second);
    }
  });
});

test("a launcher reports a session that cannot start or join", async () => {
  await withSite(async (site, root) => {
    // The server reports why it cannot listen.
    const failing = inProcessHost(process.pid, {
      start: (file, arguments_) => void writeFile(sessionStatePath(site),
        JSON.stringify({ token: arguments_[arguments_.indexOf("--token") + 1], error: "the development server cannot listen" })),
    });
    await assert.rejects(launch({ site, port: null }, failing), /cannot listen/u);
    // A server that never starts.
    await assert.rejects(launch({ site, port: 0 }, inProcessHost(process.pid, { start: () => {}, startup: 200 })),
      /did not start/u);

    // A server for another site, or another session's, is not this site's session; one that
    // refuses the launcher cannot be joined.
    const other = path.join(root, "other.json");
    await writeMap(other, {});
    const otherServer = createSiteServer(await siteOf(other), createSession(token));
    await started(otherServer);
    const refusing = createHttpServer((request, response) => {
      if (request.url === "/_netwasm/session") response.end(JSON.stringify({ token, site: path.resolve(site) }));
      else response.writeHead(500).end();
    });
    await started(refusing);
    try {
      await writeFile(sessionStatePath(site), JSON.stringify({ token, port: otherServer.address().port }));
      assert.equal(await liveSession(site), null);
      await writeFile(sessionStatePath(site), JSON.stringify({ token: "f".repeat(32), port: refusing.address().port }));
      assert.equal(await liveSession(site), null);
      await writeFile(sessionStatePath(site), JSON.stringify({ token, port: refusing.address().port }));
      await assert.rejects(launch({ site, port: 0 }, inProcessHost(process.pid)), /refused to join the session/u);
    } finally {
      await closed(otherServer);
      await closed(refusing);
    }
  });
});

test("the session belongs to dotnet run, or to the dotnet watch that runs it", async () => {
  const linux = status => ({ platform: "linux", readText: async file => {
    assert.equal(file, "/proc/41/stat");
    return status;
  } });
  assert.equal(await sessionOwner({ environment: {}, parent: 41 }), 41);
  assert.equal(await sessionOwner({ environment: { DOTNET_WATCH: "1" }, parent: 41, ...linux("41 (dotnet) S 7 41 41 0") }), 7);
  assert.equal(await sessionOwner({ environment: { DOTNET_WATCH: "1" }, parent: 41, ...linux("41 (x) S 0 1") }), 41);
  assert.equal(await parentOf(41, linux("41 (a ) b) S 9 1 1")), 9);
  const ran = [];
  const runner = (platform, output) => ({ platform, run: async (file, arguments_) => {
    ran.push([file, ...arguments_].join(" "));
    return output;
  } });
  assert.equal(await parentOf(41, runner("win32", "12\r\n")), 12);
  assert.equal(await parentOf(41, runner("darwin", "   13\n")), 13);
  await assert.rejects(parentOf(41, runner("darwin", "")), /process 41 has no parent/u);
  assert.deepEqual(ran, [
    "powershell.exe -NoProfile -NonInteractive -Command (Get-CimInstance Win32_Process -Filter \"ProcessId=41\").ParentProcessId",
    "ps -o ppid= -p 41",
    "ps -o ppid= -p 41",
  ]);
});

test("the process host starts detached processes and reads processes", async () => {
  const marker = path.join(await mkdtemp(path.join(tmpdir(), "netwasm-host-")), "started");
  try {
    processHost.start(process.execPath, ["-e", `require("node:fs").writeFileSync(${JSON.stringify(marker)}, "yes")`]);
    await until(async () => (await readFile(marker, "utf8").catch(() => "")) === "yes", "the detached process");
  } finally {
    await rm(path.dirname(marker), { recursive: true, force: true });
  }
  assert.equal(await processHost.run(process.execPath, ["-e", "process.stdout.write('5')"]), "5");
  await assert.rejects(processHost.run(path.join(tmpdir(), "netwasm-missing-command"), []), { code: "ENOENT" });
  assert.match(await processHost.readText(fileURLToPath(import.meta.url)), /^import assert/u);
  assert.equal(processHost.alive(process.pid), true);
  const ended = spawn(process.execPath, ["-e", "0"]);
  await new Promise(resolve => ended.once("exit", resolve));
  assert.equal(processHost.alive(ended.pid), false);
  if (process.platform === "linux") assert.equal(await parentOf(process.pid, processHost), process.ppid);
});

test("rejects invalid arguments", async () => {
  await withSite(async site => {
    for (const arguments_ of [[], ["--site"], ["--port", "80"], ["--site", site, "--port", "x"],
      ["--site", site, "--port", "70000"], ["--site", site, "--site", site], ["--site", site, "--extra", "1"],
      ["--site", site, "--owner", "1"], ["--site", site, "--token", token], ["--server", "--site", site],
      ["--server", "--site", site, "--owner", "1"], ["--server", "--site", site, "--token", token],
      ["--server", "--site", site, "--owner"], ["--server", "--site", site, "--owner", "1", "--token"],
      ["--server", "--site", site, "--owner", "1", "--token", "abc"]]) {
      await assert.rejects(run(arguments_, () => {}), /usage: netwasm-serve\.mjs --site/u, arguments_.join(" "));
    }
  });
});

test("runs as a command and its session ends with the process that ran it", async () => {
  await withSite(async site => {
    // The launcher's parent owns the session, as dotnet run does.
    const parent = spawn(process.execPath, ["-e", `require("node:child_process").spawn(process.execPath,
      [${JSON.stringify(command)}, "--site", ${JSON.stringify(site)}, "--port", "0"], { stdio: "inherit" });
      setInterval(() => {}, 1 << 30);`], { stdio: ["ignore", "pipe", "pipe"], detached: true });
    try {
      const line = await new Promise((resolve, reject) => {
        let output = "";
        parent.stdout.on("data", chunk => {
          output += chunk;
          if (output.includes("\n")) resolve(output.split("\n")[0]);
        });
        parent.once("exit", code => reject(new Error(`exited with ${code}`)));
      });
      const url = /^Now listening on: (http:\/\/localhost:\d+\/)$/u.exec(line)[1];
      assert.equal(await (await fetch(url.replace("localhost", "127.0.0.1"))).text(), "<!doctype html><p>page</p>" + reloadScript);
    } finally {
      process.kill(-parent.pid, "SIGKILL");
    }
    await until(async () => (await state(site)) === null, "the session to end with its owner");

    const failed = spawn(process.execPath, [command], { stdio: ["ignore", "ignore", "pipe"] });
    let stderr = "";
    failed.stderr.on("data", chunk => { stderr += chunk; });
    const code = await new Promise(resolve => failed.once("exit", resolve));
    assert.equal(code, 1);
    assert.match(stderr, /^netwasm-serve: usage:/u);
  });
});
