#!/usr/bin/env node

import { mkdir, writeFile } from "node:fs/promises";
import { dirname, isAbsolute } from "node:path";

import { rolldown } from "rolldown";

const arguments_ = process.argv.slice(2);
if (arguments_.length !== 8
    || arguments_[0] !== "bundle"
    || arguments_[2] !== "--output"
    || arguments_[4] !== "--platform"
    || !["browser", "node"].includes(arguments_[5])
    || arguments_[6] !== "--minify"
    || !["true", "false"].includes(arguments_[7])
    || !isAbsolute(arguments_[1])
    || !isAbsolute(arguments_[3])) {
  process.stderr.write("NW-BUNDLE-001: invalid bundler invocation\n");
  process.exitCode = 2;
} else {
  try {
    const input = arguments_[1];
    const output = arguments_[3];
    const platform = arguments_[5];
    const build = await rolldown({
      external: platform === "node" ? /^node:/u : undefined,
      input,
      platform,
      treeshake: true,
    });
    try {
      const generated = await build.generate({
        codeSplitting: false,
        format: "esm",
        minify: arguments_[7] === "true",
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
  } catch {
    process.stderr.write("NW-BUNDLE-002: bundling failed\n");
    process.exitCode = 1;
  }
}
