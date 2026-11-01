import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { mkdir, mkdtemp, rm, writeFile } from "node:fs/promises";
import { createServer } from "node:net";
import { tmpdir } from "node:os";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

import { createDeploymentServer, serve } from "./netwasm-dev-server.mjs";

const command = fileURLToPath(new URL("./netwasm-serve.mjs", import.meta.url));

async function withSite(action) {
  const root = await mkdtemp(path.join(tmpdir(), "netwasm-serve-"));
  try {
    await mkdir(path.join(root, "browser", "nested"), { recursive: true });
    await writeFile(path.join(root, "index.html"), "<!doctype html><p>page</p>");
    await writeFile(path.join(root, "browser", "Calc.mjs"), "export const value = 42;");
    await writeFile(path.join(root, "browser", "app.wasm"), Uint8Array.of(0, 0x61, 0x73, 0x6d));
    await writeFile(path.join(root, "browser", "data.BIN"), "bytes");
    await writeFile(path.join(root, "browser", "nested", "index.html"), "<p>nested</p>");
    await mkdir(path.join(root, "browser", "odd", "index.html"), { recursive: true });
    await writeFile(path.join(root, "..", path.basename(root) + "-secret.txt"), "outside");
    return await action(root);
  } finally {
    await rm(path.join(root, "..", path.basename(root) + "-secret.txt"), { force: true });
    await rm(root, { recursive: true, force: true });
  }
}

async function started(server) {
  await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
  return `http://127.0.0.1:${server.address().port}`;
}

const closed = server => new Promise(resolve => server.close(resolve));

// Issue #140: dotnet run on a page project serves its publish output.
test("serves the deployment with browser content types and no caching", async () => {
  await withSite(async root => {
    const server = createDeploymentServer(root);
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
    } finally {
      await closed(server);
    }
  });
});

test("serves nothing outside the deployment and only reads", async () => {
  await withSite(async root => {
    const server = createDeploymentServer(root);
    const origin = await started(server);
    try {
      for (const url of ["/missing.mjs", "/browser/", "/browser/odd/", `/..%2f${path.basename(root)}-secret.txt`,
        `/%2e%2e/${path.basename(root)}-secret.txt`, "/browser%00.mjs"]) {
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

test("listens on the requested port, 5080 by default or a free one when it is taken", async () => {
  await withSite(async root => {
    const lines = [];
    const blocker = createServer();
    await new Promise(resolve => blocker.listen(0, "127.0.0.1", resolve));
    const taken = blocker.address().port;
    try {
      const explicit = await serve(["--root", root, "--port", "0"], line => lines.push(line));
      const port = explicit.address().port;
      assert.deepEqual(lines, [`Now listening on: http://localhost:${port}/`, `Serving ${root}. Press Ctrl+C to stop.`]);
      assert.equal(explicit.address().address, "127.0.0.1");
      await closed(explicit);

      await assert.rejects(serve(["--root", root, "--port", String(taken)], () => {}), { code: "EADDRINUSE" });

      // The default port, or another when something else listens there.
      const fallback = await serve(["--root", root], () => {});
      const defaultPort = fallback.address().port;
      const second = await serve(["--root", root], () => {});
      assert.notEqual(second.address().port, defaultPort);
      await closed(second);
      await closed(fallback);
    } finally {
      await closed(blocker);
    }
    for (const arguments_ of [[], ["--root"], ["--port", "80"], ["--root", root, "--port", "x"],
      ["--root", root, "--port", "70000"], ["--root", root, "--root", root], ["--root", root, "--extra", "1"]]) {
      await assert.rejects(serve(arguments_, () => {}), /usage: netwasm-serve\.mjs --root/u, arguments_.join(" "));
    }
    await assert.rejects(serve(["--root", path.join(root, "index.html")], () => {}), /is not a directory/u);
    await assert.rejects(serve(["--root", path.join(root, "missing")], () => {}), /is not a directory/u);
  });
});

// The SDK stages the site with dotnet publish before serving it.
test("runs the staging command first and stops when it fails", async () => {
  await withSite(async root => {
    const site = path.join(root, "staged");
    const stagePath = path.join(root, "stage.json");
    const write = (command, arguments_) => writeFile(stagePath, JSON.stringify({ command, arguments: arguments_ }));
    await write(process.execPath, ["-e",
      `require("node:fs").mkdirSync(${JSON.stringify(site)}); require("node:fs").writeFileSync(${JSON.stringify(path.join(site, "index.html"))}, "staged")`]);
    const server = await serve(["--root", site, "--stage", stagePath, "--port", "0"], () => {});
    try {
      assert.equal(await (await fetch(`http://127.0.0.1:${server.address().port}/`)).text(), "staged");
    } finally {
      await closed(server);
    }
    await write(process.execPath, ["-e", "process.exit(3)"]);
    await assert.rejects(serve(["--root", site, "--stage", stagePath], () => {}), /failed with exit code 3/u);
    await write(path.join(root, "missing-command"), []);
    await assert.rejects(serve(["--root", site, "--stage", stagePath], () => {}), { code: "ENOENT" });
    for (const value of [null, {}, { command: 1, arguments: [] }, { command: "dotnet" },
      { command: "dotnet", arguments: [1] }]) {
      await writeFile(stagePath, JSON.stringify(value));
      await assert.rejects(serve(["--root", site, "--stage", stagePath], () => {}), /is not a staging command/u);
    }
    await assert.rejects(serve(["--root", site, "--stage", stagePath, "--stage", stagePath], () => {}), /usage/u);
  });
});

test("runs as a command and reports invalid arguments", async () => {
  await withSite(async root => {
    const child = spawn(process.execPath, [command, "--root", root, "--port", "0"], { stdio: ["ignore", "pipe", "pipe"] });
    try {
      const line = await new Promise((resolve, reject) => {
        let output = "";
        child.stdout.on("data", chunk => {
          output += chunk;
          if (output.includes("\n")) resolve(output.split("\n")[0]);
        });
        child.once("exit", code => reject(new Error(`exited with ${code}`)));
      });
      const url = /^Now listening on: (http:\/\/localhost:\d+\/)$/u.exec(line)[1];
      assert.equal(await (await fetch(url.replace("localhost", "127.0.0.1"))).text(), "<!doctype html><p>page</p>");
    } finally {
      child.kill();
    }
    const failed = spawn(process.execPath, [command], { stdio: ["ignore", "ignore", "pipe"] });
    let stderr = "";
    failed.stderr.on("data", chunk => { stderr += chunk; });
    const code = await new Promise(resolve => failed.once("exit", resolve));
    assert.equal(code, 1);
    assert.match(stderr, /^netwasm-serve: usage:/u);
  });
});
