import { createHash } from "node:crypto";
import {
  closeSync, ftruncateSync, lstatSync, mkdirSync, openSync, readdirSync, readlinkSync, readSync, realpathSync,
  renameSync, rmdirSync, statSync, symlinkSync, unlinkSync, utimesSync, writeSync,
} from "node:fs";
import { mkdtemp, readFile, realpath, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { isAbsolute, join } from "node:path";

import { createExecutionDescriptorReader } from "./execution-descriptor-reader.mjs";
import {
  createExecutionDescriptorValidator,
} from "./execution-descriptor-validator.mjs";
import { createExecutionRequestReader } from "./execution-request-reader.mjs";
import { appendExecutionArguments } from "./execution-request-arguments.mjs";
import { overlayExecutionEnvironment } from "./execution-request-environment.mjs";
import { createExecutionRequestValidator } from "./execution-request-validator.mjs";
import { createLocalDirectoryGrants } from "./local-directory-grants.mjs";
import { createLocalLauncher } from "./local-launcher.mjs";
import { createLocalLauncherCommand } from "./local-launcher-command.mjs";
import { createLocalNodeHttpLoader } from "./local-node-http-loader.mjs";
import { adaptCommandProcessResult } from "./command-process-result.mjs";
import { describeFailureCause } from "./execution-result.mjs";
import { createStandaloneChannels, readStandaloneArguments } from "./local-standalone-channels.mjs";
import { selectProviderKind } from "./provider-kind-selector.mjs";
import { parseStrictJson } from "./strict-json-reader.mjs";
import { createWasiFilesystem } from "./wasi-filesystem.mjs";
import { createWasiPlatform } from "./wasi-platform.mjs";

// dotnet run, dotnet test and NetWasm's test hosts start the launcher with this option and
// their channels; any other invocation runs the deployment beside the launcher.
const executionOption = "--netwasm-execution";

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
    // The launcher reads the working and temporary directory grants and mounts them itself.
    validate: createExecutionRequestValidator({
      hostDirectories: true,
      isAbsoluteHostPath: isAbsolute,
      selectProviderKind,
    }),
  });
  const loadNetwork = createLocalNodeHttpLoader({
    hashBytes,
    importModule: value => import(value),
    readFile,
    realPath: realpath,
  });
  // Files answer synchronously, so a raw host's guest never waits on them.
  const hostFiles = Object.freeze({
    closeSync, ftruncateSync, lstatSync, mkdirSync, openSync, readdirSync, readlinkSync, readSync, realpathSync,
    renameSync, rmdirSync, statSync, symlinkSync, unlinkSync, utimesSync, writeSync,
  });
  const loadPlatform = async ({ initialDirectory, manifestPath }) => Object.freeze({
    createFilesystem: preopens => createWasiFilesystem(Object.freeze({ hostFiles, preopens })),
    createImports: createWasiPlatform(Object.freeze({
      initialDirectory,
      network: await loadNetwork(manifestPath),
    })),
  });
  const grantDirectories = createLocalDirectoryGrants({
    makeTemporaryDirectory: () => mkdtemp(join(tmpdir(), "netwasm-")),
    platform: process.platform,
    removeDirectory: path => rm(path, { force: true, recursive: true }),
    workingDirectory: () => process.cwd(),
  });
  const launch = createLocalLauncher({
    appendArguments: appendExecutionArguments,
    createExecution,
    grantDirectories,
    loadPlatform,
    overlayEnvironment: overlayExecutionEnvironment,
    readDescriptor,
    readRequest,
    realPath: realpath,
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
  const stdin = Object.freeze({ read: () => process.stdin[Symbol.asyncIterator]() });
  const controller = new AbortController();
  const cancel = () => controller.abort();
  process.once("SIGINT", cancel);
  process.once("SIGTERM", cancel);

  const forward = result => {
    const adapted = adaptCommandProcessResult(result);
    if (adapted.diagnostic !== null) process.stderr.write(adapted.diagnostic);
    process.exitCode = adapted.exitCode;
  };

  try {
    const arguments_ = process.argv.slice(2);
    const environment = snapshotProcessEnvironment(process.env);
    if (arguments_[0] !== executionOption) {
      const standalone = readStandaloneArguments(arguments_, process.cwd(), process.platform);
      const readChannels = createStandaloneChannels({ hashBytes, readFile });
      const channels = await readChannels(Object.freeze({
        hostExecutablePath: process.execPath,
        launcherPath,
        preopens: standalone.preopens,
      }));
      forward(await launch(Object.freeze({
        arguments: standalone.arguments,
        descriptorText: channels.descriptorText,
        environment,
        hostExecutablePath: process.execPath,
        launcherPath,
        requestText: channels.requestText,
        signal: controller.signal,
        stderr,
        stdin,
        stdout,
      })));
      return;
    }
    arguments_.shift();
    const forwardCommandResult = arguments_[0] === "--forward-command-result";
    if (forwardCommandResult) arguments_.shift();
    const result = await run(Object.freeze({
      arguments: arguments_,
      environment,
      hostExecutablePath: process.execPath,
      launcherPath,
      signal: controller.signal,
      stderr,
      stdin,
      stdout,
    }));
    if (forwardCommandResult) forward(result);
  } catch (error) {
    process.stderr.write(`NetWasm launcher failed: ${describeFailureCause(error)}\n`);
    process.exitCode = 1;
  } finally {
    process.off("SIGINT", cancel);
    process.off("SIGTERM", cancel);
  }
}

function snapshotProcessEnvironment(environment) {
  return Object.freeze(Object.entries(environment)
    .filter(([, value]) => typeof value === "string")
    .map(([name, value]) => Object.freeze({ name, value })));
}

async function readStdinText() {
  process.stdin.setEncoding("utf8");
  let text = "";
  for await (const chunk of process.stdin) text += chunk;
  return text;
}
