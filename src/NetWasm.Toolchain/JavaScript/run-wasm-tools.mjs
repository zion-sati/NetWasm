import { existsSync, lstatSync, readdirSync, realpathSync, statSync } from "node:fs";
import { readFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { WASI } from "node:wasi";

const preopens = { ".": process.cwd() };
const mountedRoots = new Map();

function mapAbsolutePath(argument) {
  if (!path.isAbsolute(argument)) {
    return argument;
  }

  const hostRoot = findExistingRoot(argument);
  const key = process.platform === "win32" ? hostRoot.toLowerCase() : hostRoot;
  let guestRoot = mountedRoots.get(key);
  if (!guestRoot) {
    guestRoot = `/netwasm/paths/${mountedRoots.size}`;
    mountedRoots.set(key, guestRoot);
    preopens[guestRoot] = hostRoot;
  }

  const relativePath = path.relative(hostRoot, argument)
    .split(path.sep)
    .join("/");
  return relativePath.length === 0 ? guestRoot : `${guestRoot}/${relativePath}`;
}

function findExistingRoot(absolutePath) {
  let candidate = existsSync(absolutePath) && statSync(absolutePath).isDirectory()
    ? absolutePath
    : path.dirname(absolutePath);
  while (!existsSync(candidate)) {
    const parent = path.dirname(candidate);
    if (parent === candidate) {
      throw new Error(`No existing host directory contains '${absolutePath}'.`);
    }
    candidate = parent;
  }
  return candidate;
}

const errno = Object.freeze({ success: 0, badf: 8, io: 29 });
const fileTypes = Object.freeze({ unknown: 0, directory: 3, regularFile: 4, symbolicLink: 7 });
const direntSize = 24;

// Node's WASI cannot list a directory on Windows: uvwasi's fd_readdir returns
// ENOSYS there, and wasm-tools reads a WIT package by listing its directory.
// Track which host directory each descriptor names and list it here instead,
// on every platform, writing preview1 dirents in sorted order without "." and
// "..", as uvwasi does on POSIX.
export function withDirectoryListing(imports, preopenedPaths, getMemory) {
  const preview1 = imports.wasi_snapshot_preview1;
  const hostPaths = new Map(preopenedPaths.map((hostPath, index) => [3 + index, hostPath]));
  const memory = () => new DataView(getMemory().buffer);
  return {
    ...imports,
    wasi_snapshot_preview1: {
      ...preview1,
      path_open(fd, dirflags, pathPointer, pathLength, oflags, rightsBase, rightsInheriting, fdflags, openedPointer) {
        const result = preview1.path_open(fd, dirflags, pathPointer, pathLength, oflags, rightsBase,
          rightsInheriting, fdflags, openedPointer);
        const parent = hostPaths.get(fd);
        if (result === errno.success && parent !== undefined) {
          const guestPath = new TextDecoder().decode(
            new Uint8Array(getMemory().buffer, pathPointer, pathLength));
          hostPaths.set(memory().getUint32(openedPointer, true), path.join(parent, guestPath));
        }
        return result;
      },
      fd_close(fd) {
        hostPaths.delete(fd);
        return preview1.fd_close(fd);
      },
      fd_readdir(fd, bufferPointer, bufferLength, cookie, usedPointer) {
        const directory = hostPaths.get(fd);
        if (directory === undefined) return errno.badf;
        let entries;
        try {
          entries = readdirSync(directory).sort();
        } catch {
          return errno.io;
        }
        const bytes = new Uint8Array(getMemory().buffer, bufferPointer, bufferLength);
        let used = 0;
        for (let index = Number(cookie); index < entries.length && used < bufferLength; index++) {
          const name = new TextEncoder().encode(entries[index]);
          const record = new Uint8Array(direntSize + name.length);
          const view = new DataView(record.buffer);
          const status = readStatus(path.join(directory, entries[index]));
          view.setBigUint64(0, BigInt(index + 1), true);
          view.setBigUint64(8, status.inode, true);
          view.setUint32(16, name.length, true);
          view.setUint8(20, status.type);
          record.set(name, direntSize);
          // A record that does not fit is written in part, and the full buffer
          // tells the caller to read again with a larger one.
          const length = Math.min(record.length, bufferLength - used);
          bytes.set(record.subarray(0, length), used);
          used += length;
        }
        memory().setUint32(usedPointer, used, true);
        return errno.success;
      },
    },
  };
}

// wasm-tools reports the guest paths that arguments were mounted at, such as
// /netwasm/paths/0/world.wit, so its diagnostics name the host directories again.
export function restoreHostPaths(text, mounts) {
  return text.replace(/\/netwasm\/paths\/(\d+)(?![\d])/g, (guestRoot) => mounts[guestRoot] ?? guestRoot);
}

// Collects what the guest writes to standard error, so that its paths can be
// restored before it reaches the host.
export function withCapturedStandardError(imports, getMemory, chunks) {
  const preview1 = imports.wasi_snapshot_preview1;
  return {
    ...imports,
    wasi_snapshot_preview1: {
      ...preview1,
      fd_write(fd, iovs, iovsLength, writtenPointer) {
        if (fd !== 2) return preview1.fd_write(fd, iovs, iovsLength, writtenPointer);
        const view = new DataView(getMemory().buffer);
        let written = 0;
        for (let index = 0; index < iovsLength; index++) {
          const base = view.getUint32(iovs + index * 8, true);
          const length = view.getUint32(iovs + index * 8 + 4, true);
          chunks.push(new Uint8Array(getMemory().buffer, base, length).slice());
          written += length;
        }
        view.setUint32(writtenPointer, written, true);
        return errno.success;
      },
    },
  };
}

function readStatus(entryPath) {
  try {
    const status = lstatSync(entryPath, { bigint: true });
    const type = status.isDirectory() ? fileTypes.directory
      : status.isFile() ? fileTypes.regularFile
        : status.isSymbolicLink() ? fileTypes.symbolicLink
          : fileTypes.unknown;
    return { inode: status.ino, type };
  } catch {
    return { inode: 0n, type: fileTypes.unknown };
  }
}

async function main([modulePath, ...requestedArguments]) {
  if (!modulePath) {
    throw new Error("The NetWasm wasm-tools runner requires a module path.");
  }
  const argumentsForWasi = requestedArguments.map(mapAbsolutePath);
  const wasi = new WASI({
    version: "preview1",
    returnOnExit: true,
    args: ["wasm-tools", ...argumentsForWasi],
    env: {},
    preopens,
  });
  const module = await WebAssembly.compile(await readFile(modulePath));
  let instance;
  const standardError = [];
  const imports = withCapturedStandardError(
    withDirectoryListing(wasi.getImportObject(), Object.values(preopens), () => instance.exports.memory),
    () => instance.exports.memory,
    standardError);
  instance = await WebAssembly.instantiate(module, imports);
  let exitCode;
  try {
    exitCode = wasi.start(instance);
  } finally {
    if (standardError.length !== 0) {
      process.stderr.write(restoreHostPaths(Buffer.concat(standardError).toString("utf8"), preopens));
    }
  }
  if (!Number.isSafeInteger(exitCode) || exitCode < 0 || exitCode > 255) {
    throw new Error("The NetWasm wasm-tools runner received an invalid exit code.");
  }
  process.exitCode = exitCode;
}

// Tests import withDirectoryListing; only a direct invocation runs wasm-tools.
if (process.argv[1] !== undefined
    && realpathSync(process.argv[1]) === realpathSync(fileURLToPath(import.meta.url))) {
  await main(process.argv.slice(2));
}
