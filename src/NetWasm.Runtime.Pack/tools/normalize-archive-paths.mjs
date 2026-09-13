import { readFile, writeFile } from "node:fs/promises";

const prefixes = [];
const archives = [];
for (let index = 2; index < process.argv.length; index += 2) {
  const option = process.argv[index];
  const value = process.argv[index + 1];
  if (!value || (option !== "--prefix" && option !== "--archive")) {
    throw new Error(
      "usage: normalize-archive-paths.mjs --prefix <path> [...] --archive <path> [...]",
    );
  }

  (option === "--prefix" ? prefixes : archives).push(value);
}

if (prefixes.length === 0 || archives.length === 0) {
  throw new Error("at least one path prefix and archive are required");
}

const uniquePrefixes = [...new Set(prefixes)]
  .filter(Boolean)
  .sort((left, right) => right.length - left.length);

for (const archive of archives) {
  const bytes = await readFile(archive);
  for (const prefix of uniquePrefixes) {
    const source = Buffer.from(prefix);
    const replacement = Buffer.from(neutralPrefix(prefix.length));
    let offset = bytes.indexOf(source);
    while (offset >= 0) {
      replacement.copy(bytes, offset);
      offset = bytes.indexOf(source, offset + source.length);
    }
  }

  await writeFile(archive, bytes);
}

function neutralPrefix(length) {
  const stem = "/netwasm/build";
  if (length < stem.length) {
    return `/${"_".repeat(length - 1)}`;
  }

  return stem + "_".repeat(length - stem.length);
}
