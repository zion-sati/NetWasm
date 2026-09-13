import { createHash } from "node:crypto";
import { readFile, realpath, rm, writeFile } from "node:fs/promises";
import { isAbsolute } from "node:path";

import { createExecutionDescriptorReader } from "./execution-descriptor-reader.mjs";
import {
  createExecutionDescriptorValidator,
} from "./execution-descriptor-validator.mjs";
import { createExecutionPackageVerifier } from "./execution-package-verifier.mjs";
import { createExecutionRequestReader } from "./execution-request-reader.mjs";
import {
  appendExecutionArguments,
  createExecutionRequestValidator,
} from "./execution-request-validator.mjs";
import { createLocalLauncher } from "./local-launcher.mjs";
import { createLocalLauncherCommand } from "./local-launcher-command.mjs";
import { createPreview2PlatformLoader } from "./preview2-platform-loader.mjs";
import { selectProviderKind } from "./provider-kind-selector.mjs";
import { parseStrictJson } from "./strict-json-reader.mjs";

export async function runLocalNetWasmLauncher(createExecution, launcherPath) {
  if (typeof createExecution !== "function"
      || typeof launcherPath !== "string" || !isAbsolute(launcherPath)) {
    throw new TypeError("local launcher entry contract is invalid");
  }
  const hashBytes = bytes => createHash("sha256").update(bytes).digest("hex");
  const readDescriptor = createExecutionDescriptorReader({
    parseJson: parseStrictJson,
    validate: createExecutionDescriptorValidator({ isAbsoluteHostPath: isAbsolute }),
  });
  const readRequest = createExecutionRequestReader({
    parseJson: parseStrictJson,
    validate: createExecutionRequestValidator({
      isAbsoluteHostPath: isAbsolute,
      selectProviderKind,
    }),
  });
  const verifyPackage = createExecutionPackageVerifier({
    hashBytes,
    readFile,
    realPath: realpath,
  });
  const loadPlatform = createPreview2PlatformLoader({
    hashBytes,
    importModule: value => import(value),
    readFile,
    realPath: realpath,
  });
  const launch = createLocalLauncher({
    appendArguments: appendExecutionArguments,
    createExecution,
    loadPlatform,
    readDescriptor,
    readRequest,
    realPath: realpath,
    verifyPackage,
  });
  const run = createLocalLauncherCommand({
    deleteResult: path => rm(path, { force: true }),
    launch,
    readFileText: path => readFile(path, "utf8"),
    readStdinText,
    writeResultText: (path, text) => writeFile(path, text, {
      encoding: "utf8",
      flag: "wx",
    }),
  });
  const stdout = Object.freeze({ write: bytes => process.stdout.write(bytes) });
  const stderr = Object.freeze({ write: bytes => process.stderr.write(bytes) });
  const controller = new AbortController();
  const cancel = () => controller.abort();
  process.once("SIGINT", cancel);
  process.once("SIGTERM", cancel);

  try {
    await run(Object.freeze({
      arguments: process.argv.slice(2),
      hostExecutablePath: process.execPath,
      launcherPath,
      signal: controller.signal,
      stderr,
      stdout,
    }));
  } catch {
    process.stderr.write("NetWasm launcher failed before completing its result channel.\n");
    process.exitCode = 1;
  } finally {
    process.off("SIGINT", cancel);
    process.off("SIGTERM", cancel);
  }
}

async function readStdinText() {
  process.stdin.setEncoding("utf8");
  let text = "";
  for await (const chunk of process.stdin) text += chunk;
  return text;
}
