#!/usr/bin/env node

import { mkdir, readdir, readFile, writeFile } from "node:fs/promises";
import { basename, dirname, isAbsolute, join } from "node:path";

import { rolldown } from "rolldown";

// The shared runtime's modules are imported by these specifiers and stay
// external to an entry bundle, which reaches them by URL.
const runtimeSpecifier = /^netwasm:runtime\/[a-z][a-z0-9-]*$/u;

const arguments_ = process.argv.slice(2);
const command = readInvocation(arguments_);
if (command === null) {
  process.stderr.write("NW-BUNDLE-001: invalid bundler invocation\n");
  process.exitCode = 2;
} else {
  try {
    await command();
  } catch {
    process.stderr.write("NW-BUNDLE-002: bundling failed\n");
    process.exitCode = 1;
  }
}

function readInvocation(values) {
  if (values[0] === "bundle"
      && (values.length === 8 || values.length === 10 && values[8] === "--runtime" && isAbsolute(values[9]))
      && values[2] === "--output"
      && values[4] === "--platform"
      && ["browser", "node"].includes(values[5])
      && values[6] === "--minify"
      && ["true", "false"].includes(values[7])
      && isAbsolute(values[1])
      && isAbsolute(values[3])) {
    return () => bundle(values[1], values[3], values[5], values[7] === "true", values[9] ?? null);
  }
  if (values[0] === "bundle-runtime"
      && values.length === 6
      && values[2] === "--output"
      && values[4] === "--minify"
      && ["true", "false"].includes(values[5])
      && isAbsolute(values[1])
      && isAbsolute(values[3])) {
    return () => bundleRuntime(values[1], values[3], values[5] === "true");
  }
  return null;
}

async function bundle(input, output, platform, minify, runtimePath) {
  const runtime = runtimePath === null ? null : await readRuntimeImports(runtimePath);
  const build = await rolldown({
    external: id => {
      if (runtimeSpecifier.test(id)) {
        if (runtime !== null && Object.hasOwn(runtime, id)) return true;
        throw new Error("runtime import has no URL");
      }
      return platform === "node" && id.startsWith("node:");
    },
    input,
    platform,
    treeshake: true,
  });
  try {
    const generated = await build.generate({
      codeSplitting: false,
      format: "esm",
      minify,
      paths: runtime ?? undefined,
      sourcemap: false,
    });
    if (generated.output.length !== 1
        || generated.output[0].type !== "chunk"
        || generated.output[0].isEntry !== true) {
      throw new Error("bundler did not produce one entry chunk");
    }
    await mkdir(dirname(output), { recursive: true });
    await writeFile(output, generated.output[0].code, "utf8");
  } finally {
    await build.close();
  }
}

// A map from each runtime specifier to the URL an entry imports it by.
async function readRuntimeImports(path) {
  const value = JSON.parse(await readFile(path, "utf8"));
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || Object.entries(value).some(([specifier, url]) => !runtimeSpecifier.test(specifier)
        || typeof url !== "string" || !/^\.\.?\//u.test(url))) {
    throw new Error("runtime imports are invalid");
  }
  return value;
}

// Bundles each facade module in a directory as one entry of the shared
// runtime. The facades are the same in every project, and the output carries
// no source paths, so every project of a release writes the same files: a page
// and its workers download them once.
async function bundleRuntime(facades, output, minify) {
  const names = (await readdir(facades)).filter(name => name.endsWith(".mjs")).sort();
  if (names.length === 0) throw new Error("runtime has no facades");
  const build = await rolldown({
    experimental: { attachDebugInfo: "none" },
    input: Object.fromEntries(names.map(name => [basename(name, ".mjs"), join(facades, name)])),
    platform: "browser",
    treeshake: true,
  });
  try {
    const generated = await build.generate({
      chunkFileNames: "netwasm.runtime.[hash].mjs",
      entryFileNames: "netwasm.runtime.[name].[hash].mjs",
      format: "esm",
      minify,
      sourcemap: false,
    });
    const chunks = new Map(generated.output.map(chunk => {
      if (chunk.type !== "chunk") throw new Error("runtime bundle emitted an asset");
      return [chunk.fileName, chunk];
    }));
    await mkdir(output, { recursive: true });
    for (const chunk of chunks.values()) {
      await writeFile(join(output, chunk.fileName), chunk.code, "utf8");
    }
    const modules = {};
    const entries = [...chunks.values()].filter(value => value.isEntry)
      .sort((left, right) => left.name < right.name ? -1 : left.name > right.name ? 1 : 0);
    for (const chunk of entries) {
      modules[chunk.name] = { file: chunk.fileName, files: closure(chunk, chunks) };
    }
    await writeFile(join(output, "runtime.json"), JSON.stringify({ schemaVersion: 1, modules }), "utf8");
  } finally {
    await build.close();
  }
}

// The files a runtime module loads: itself and every chunk it imports.
function closure(entry, chunks) {
  const files = new Set();
  const pending = [entry.fileName];
  while (pending.length !== 0) {
    const fileName = pending.pop();
    if (files.has(fileName)) continue;
    files.add(fileName);
    const chunk = chunks.get(fileName);
    pending.push(...chunk.imports, ...chunk.dynamicImports);
  }
  return [...files].sort();
}
