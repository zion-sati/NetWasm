import { readFile, realpath, writeFile } from "node:fs/promises";
import { isAbsolute, relative, resolve } from "node:path";

const [ninjaFile, sourceRoot, buildRoot] = process.argv.slice(2);
if (!ninjaFile || !sourceRoot || !buildRoot
    || !isAbsolute(ninjaFile) || !isAbsolute(sourceRoot) || !isAbsolute(buildRoot)) {
  throw new TypeError(
    "usage: relativize-ninja-source-root.mjs <build.ninja> <source-root> <build-root>",
  );
}

const canonicalSourceRoot = await realpath(sourceRoot);
const canonicalBuildRoot = await realpath(buildRoot);
const relativeSourceRoot = relative(canonicalBuildRoot, canonicalSourceRoot).replaceAll("\\", "/");
if (!relativeSourceRoot || isAbsolute(relativeSourceRoot)) {
  throw new TypeError("The source root could not be made relative to the build root.");
}

const original = await readFile(ninjaFile, "utf8");
const sourceForms = [...new Set([
  sourceRoot,
  resolve(sourceRoot),
  canonicalSourceRoot,
  sourceRoot.replaceAll("\\", "/"),
  resolve(sourceRoot).replaceAll("\\", "/"),
  canonicalSourceRoot.replaceAll("\\", "/"),
])].sort((left, right) => right.length - left.length);
let rewritten = original;
for (const sourceForm of sourceForms) {
  rewritten = rewritten.replaceAll(sourceForm, relativeSourceRoot);
}

if (rewritten === original || sourceForms.some(sourceForm => rewritten.includes(sourceForm))) {
  throw new Error("The generated Ninja file did not contain a replaceable source root.");
}

await writeFile(ninjaFile, rewritten);
