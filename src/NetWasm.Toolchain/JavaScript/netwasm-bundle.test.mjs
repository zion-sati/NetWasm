import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdir, mkdtemp, readdir, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const command = fileURLToPath(new URL("./netwasm-bundle.mjs", import.meta.url));

function run(...arguments_) {
  return spawnSync(process.execPath, [command, ...arguments_], { encoding: "utf8" });
}

async function withDirectory(action) {
  const directory = await mkdtemp(path.join(tmpdir(), "netwasm-bundle-"));
  try {
    return await action(directory);
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
}

// Two facades in a directory, each importing shared and own sources by
// absolute path, as the SDK writes them.
async function writeFacades(directory, sources) {
  await mkdir(directory, { recursive: true });
  await writeFile(path.join(directory, "page.mjs"),
    `export { shared } from ${JSON.stringify(path.join(sources, "shared.mjs"))};\n`
    + `export { page } from ${JSON.stringify(path.join(sources, "page-only.mjs"))};\n`);
  await writeFile(path.join(directory, "worker.mjs"),
    `export { shared } from ${JSON.stringify(path.join(sources, "shared.mjs"))};\n`
    + `export { worker } from ${JSON.stringify(path.join(sources, "worker-only.mjs"))};\n`);
}

async function writeSources(sources) {
  await mkdir(sources, { recursive: true });
  await writeFile(path.join(sources, "shared.mjs"),
    "export function shared(value) { return `shared:${value}`; }\n");
  await writeFile(path.join(sources, "page-only.mjs"),
    "import { shared } from \"./shared.mjs\";\nexport function page() { return shared(\"page\"); }\n");
  await writeFile(path.join(sources, "worker-only.mjs"),
    "import { shared } from \"./shared.mjs\";\nexport function worker() { return shared(\"worker\"); }\n");
}

async function readOutput(directory) {
  const files = (await readdir(directory)).sort();
  return Object.fromEntries(await Promise.all(files.map(async name =>
    [name, await readFile(path.join(directory, name), "utf8")])));
}

// Issue #128: a page and its workers load one runtime, so every project of a
// release must write the same runtime files wherever its facades live.
test("bundles the runtime facades into the same files from any directory", async () => {
  await withDirectory(async directory => {
    const sources = path.join(directory, "package");
    await writeSources(sources);
    const outputs = [];
    for (const facades of [path.join(directory, "a"), path.join(directory, "b", "c", "d")]) {
      await writeFacades(facades, sources);
      for (const minify of ["false", "true"]) {
        const output = path.join(facades, "out-" + minify);
        const result = run("bundle-runtime", facades, "--output", output, "--minify", minify);
        assert.equal(result.status, 0, result.stderr);
        outputs.push(await readOutput(output));
      }
    }
    assert.deepEqual(outputs[2], outputs[0]);
    assert.deepEqual(outputs[3], outputs[1]);
    assert.notDeepEqual(outputs[1], outputs[0]);

    const files = outputs[0];
    const runtime = JSON.parse(files["runtime.json"]);
    assert.deepEqual(Object.keys(runtime.modules), ["page", "worker"]);
    assert.equal(runtime.schemaVersion, 1);
    const shared = runtime.modules.page.files.filter(file => file !== runtime.modules.page.file);
    assert.equal(shared.length, 1);
    assert.match(shared[0], /^netwasm\.runtime\.[\w-]+\.mjs$/u);
    assert.deepEqual(runtime.modules.worker.files.filter(file => file !== runtime.modules.worker.file), shared);
    assert.match(runtime.modules.page.file, /^netwasm\.runtime\.page\.[\w-]+\.mjs$/u);
    assert.ok(runtime.modules.page.files.includes(runtime.modules.page.file));
    assert.ok(!files[runtime.modules.page.file].includes("worker-only"));
    for (const text of Object.values(files)) assert.ok(!text.includes(directory));
    assert.ok(files[shared[0]].includes("shared:"));
  });
});

test("leaves runtime imports external to an entry and reaches them by URL", async () => {
  await withDirectory(async directory => {
    const entry = path.join(directory, "entry.mjs");
    await writeFile(path.join(directory, "local.mjs"), "export const local = 42;\n");
    await writeFile(entry,
      "import { page } from \"netwasm:runtime/page\";\n"
      + `import { local } from ${JSON.stringify(path.join(directory, "local.mjs"))};\n`
      + "export const value = page() + local;\n");
    const imports = path.join(directory, "runtime-imports.json");
    await writeFile(imports, JSON.stringify({ "netwasm:runtime/page": "../../netwasm.page.abc.mjs" }));
    const output = path.join(directory, "out", "entry.mjs");

    const result = run("bundle", entry, "--output", output, "--platform", "browser", "--minify", "false",
      "--runtime", imports);

    assert.equal(result.status, 0, result.stderr);
    const text = await readFile(output, "utf8");
    assert.match(text, /from "\.\.\/\.\.\/netwasm\.page\.abc\.mjs"/u);
    assert.ok(!text.includes("netwasm:runtime"));
    assert.ok(text.includes("42"));

    // A runtime import without a map is not bundled from anywhere.
    assert.equal(run("bundle", entry, "--output", output, "--platform", "browser", "--minify", "false").status, 1);
    for (const value of [[], { "app:other": "./x.mjs" }, { "netwasm:runtime/page": "https://cdn/x.mjs" },
      { "netwasm:runtime/page": 1 }]) {
      await writeFile(imports, JSON.stringify(value));
      const rejected = run("bundle", entry, "--output", output, "--platform", "browser", "--minify", "false",
        "--runtime", imports);
      assert.equal(rejected.status, 1);
      assert.match(rejected.stderr, /NW-BUNDLE-002/u);
    }
  });
});

test("rejects invalid invocations before bundling", async () => {
  await withDirectory(async directory => {
    for (const arguments_ of [
      [],
      ["bundle-runtime", "relative", "--output", directory, "--minify", "false"],
      ["bundle-runtime", directory, "--output", directory, "--minify", "maybe"],
      ["bundle-runtime", directory, "--output", directory],
      ["bundle", path.join(directory, "entry.mjs"), "--output", path.join(directory, "out.mjs"),
        "--platform", "browser", "--minify", "false", "--runtime", "relative.json"],
      ["bundle", path.join(directory, "entry.mjs"), "--output", path.join(directory, "out.mjs"),
        "--platform", "deno", "--minify", "false"],
    ]) {
      const result = run(...arguments_);
      assert.equal(result.status, 2);
      assert.match(result.stderr, /NW-BUNDLE-001/u);
    }
    // A directory with no facades has no runtime to bundle.
    const empty = run("bundle-runtime", directory, "--output", path.join(directory, "out"), "--minify", "false");
    assert.equal(empty.status, 1);
  });
});
