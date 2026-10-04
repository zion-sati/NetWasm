const MAP_MEDIA_TYPE = "application/vnd.netwasm.exception-types+json;version=2";

export function createManagedExceptionReporter(options) {
  const {
    getMemory,
    stringDataOffset,
  } = options;

  if (typeof getMemory !== "function") throw new TypeError("getMemory must be a function");
  if (!Number.isSafeInteger(stringDataOffset) || stringDataOffset < 0) throw new RangeError("stringDataOffset must be a non-negative safe integer");
  const reporter = createManagedExceptionEventReporter(options);

  function reportTerminalException(
    typeId,
    messageReference,
    messageLength,
    stackTraceReference = 0,
    stackTraceLength = 0) {
    reporter.reportTerminalEvent(
      typeId,
      copyManagedString(getMemory(), messageReference, messageLength, stringDataOffset),
      copyManagedString(getMemory(), stackTraceReference, stackTraceLength, stringDataOffset));
  }

  function reportTerminalExceptionV1(typeId, messageReference, messageLength) {
    reportTerminalException(typeId, messageReference, messageLength);
  }

  return Object.freeze({
    importObject: Object.freeze({
      report_terminal_exception_v1: reportTerminalExceptionV1,
      report_terminal_exception_v2: reportTerminalException,
      raise_terminal_exception() {
        // Raw modules execute the emitter's immediately following `unreachable`.
        // Throwing from JavaScript here would create a catchable foreign
        // exception instead of preserving the terminal Wasm trap contract.
      },
    }),
    reportTerminalException,
    reportTerminalEvent: reporter.reportTerminalEvent,
    consumeTerminalEvent: reporter.consumeTerminalEvent,
    drain: reporter.drain,
  });
}

export function createManagedExceptionEventReporter(options) {
  const {
    reportImmediate,
    reportEnriched,
    loadArtifacts,
    stackTraceSymbols = [],
    maximumReportedMessageLength = Number.POSITIVE_INFINITY,
    crypto: cryptoProvider = globalThis.crypto,
  } = options;

  if (typeof reportImmediate !== "function") throw new TypeError("reportImmediate must be a function");
  if (typeof reportEnriched !== "function") throw new TypeError("reportEnriched must be a function");
  if (typeof loadArtifacts !== "function") throw new TypeError("loadArtifacts must be a function");
  if (maximumReportedMessageLength !== Number.POSITIVE_INFINITY &&
      (!Number.isSafeInteger(maximumReportedMessageLength) || maximumReportedMessageLength < 0))
    throw new RangeError("maximumReportedMessageLength must be a non-negative safe integer or Infinity");
  const stackTraceSymbolNames = readStackTraceSymbolNames(stackTraceSymbols);

  let nextEventId = 1;
  let artifactsPromise;
  let enrichmentFailure;
  const pendingEnrichments = new Set();
  const terminalEvents = [];

  function reportTerminalEvent(typeId, storedMessage, stackTrace = null) {
    if (!Number.isSafeInteger(typeId) || typeId <= 0 || typeId > 0x7fffffff)
      throw new RangeError("Managed exception type ID must be a positive i32 value.");
    if (storedMessage !== null && typeof storedMessage !== "string") {
      throw new TypeError("Managed exception message must be a string or null.");
    }
    if (stackTrace !== null && typeof stackTrace !== "string") {
      throw new TypeError("Managed exception stack trace must be a string or null.");
    }

    const eventId = nextEventId++;
    const resolvedStackTrace = resolveStackTrace(stackTrace, stackTraceSymbolNames);
    const messageTruncated = typeof storedMessage === "string"
      && storedMessage.length > maximumReportedMessageLength;
    const message = messageTruncated
      ? storedMessage.slice(0, maximumReportedMessageLength)
      : storedMessage;
    const terminalEvent = Object.freeze({
      eventId,
      typeId,
      message,
      messageTruncated,
      stackTrace: resolvedStackTrace,
    });
    terminalEvents.push(terminalEvent);
    reportImmediate(terminalEvent);

    artifactsPromise ??= loadAndValidateArtifacts(loadArtifacts, cryptoProvider);
    let enrichment;
    enrichment = artifactsPromise.then(
      artifacts => {
        const entry = artifacts.types.get(typeId);
        reportEnriched({
          eventId,
          typeId,
          typeName: entry?.displayName ?? `<unknown exception type #${typeId}>`,
          canonicalIdentity: entry?.canonicalIdentity,
          assemblyIdentity: entry?.assemblyIdentity,
          message,
          stackTrace: resolvedStackTrace,
          buildId: artifacts.manifest.buildId,
        });
      },
      error => {
        reportEnriched({ eventId, typeId, typeName: `<exception type #${typeId} unavailable>`, message, stackTrace: resolvedStackTrace, artifactError: String(error) });
      },
    ).catch(error => {
      enrichmentFailure ??= error;
    }).finally(() => pendingEnrichments.delete(enrichment));
    pendingEnrichments.add(enrichment);
  }

  return Object.freeze({
    reportTerminalEvent,
    consumeTerminalEvent() {
      return terminalEvents.pop();
    },
    async drain() {
      while (pendingEnrichments.size !== 0) {
        await Promise.all([...pendingEnrichments]);
      }
      if (enrichmentFailure !== undefined) throw enrichmentFailure;
    },
  });
}

export function readStackTraceSymbolNames(symbols) {
  if (!Array.isArray(symbols)) {
    throw new TypeError("Managed stack-trace symbols must be an array.");
  }
  const names = new Map();
  for (const symbol of symbols) {
    if (symbol === null || typeof symbol !== "object"
        || !Number.isSafeInteger(symbol.id) || symbol.id <= 0
        || typeof symbol.name !== "string" || symbol.name.length === 0
        || names.has(symbol.id)) {
      throw new TypeError("Managed stack-trace symbol is invalid.");
    }
    names.set(symbol.id, symbol.name);
  }
  return names;
}

export function resolveStackTrace(stackTrace, symbols) {
  if (stackTrace === null || symbols.size === 0) return stackTrace;
  return stackTrace.split("\n").map(frame => {
    const match = /^at method#([1-9][0-9]*)$/u.exec(frame);
    if (match === null) return frame;
    const name = symbols.get(Number(match[1]));
    return name === undefined ? frame : `at ${name}`;
  }).join("\n");
}

export function copyManagedString(memory, reference, length, stringDataOffset) {
  if (!(memory instanceof WebAssembly.Memory)) return "<message unavailable>";
  if (!Number.isSafeInteger(length) || length < 0) return "<message unavailable>";
  if (reference === 0 || reference === 0n) return length === 0 ? null : "<message unavailable>";
  if (typeof reference !== "bigint" && (!Number.isSafeInteger(reference) || reference < 0))
    return "<message unavailable>";

  const numericReference = typeof reference === "bigint" ? reference : BigInt(reference);
  const start = numericReference + BigInt(stringDataOffset);
  const byteLength = BigInt(length) * 2n;
  const end = start + byteLength;
  const memoryLength = BigInt(memory.buffer.byteLength);
  if (start < 0n || end > memoryLength) return "<message unavailable>";

  if ((start & 1n) !== 0n) return "<message unavailable>";
  const units = new Uint16Array(memory.buffer, Number(start), length);
  let result = "";
  for (const unit of units) result += String.fromCharCode(unit);
  return result;
}

export async function loadAndValidateArtifacts(loadArtifacts, cryptoProvider) {
  const artifacts = await loadArtifacts();
  const { manifest, mapBytes, wasmBytes } = artifacts;
  if (!manifest || manifest.schemaVersion !== 1 || typeof manifest.buildId !== "string") throw new Error("invalid diagnostic artifact manifest");
  if (!(mapBytes instanceof Uint8Array) || !(wasmBytes instanceof Uint8Array)) throw new Error("diagnostic artifacts must be byte arrays");
  if (!cryptoProvider?.subtle) throw new Error("cryptographic digest support is unavailable");

  const [mapDigest, wasmDigest] = await Promise.all([
    sha256(mapBytes, cryptoProvider),
    sha256(wasmBytes, cryptoProvider),
  ]);
  if (mapDigest !== manifest.exceptionTypeMapSha256 || wasmDigest !== manifest.wasmSha256) throw new Error("diagnostic artifact digest mismatch");

  const map = JSON.parse(new TextDecoder().decode(mapBytes));
  if (map.schemaVersion !== 2 || map.buildId !== manifest.buildId || !Array.isArray(map.entries)) throw new Error("invalid exception type map");
  if (artifacts.mapMediaType !== undefined && artifacts.mapMediaType !== MAP_MEDIA_TYPE) throw new Error("unsupported exception type map media type");
  const types = new Map();
  let previousTypeId = 0;
  for (const entry of map.entries) {
    if (entry.typeId <= previousTypeId)
      throw new Error("Exception type map entries must be ordered by ascending type ID.");

    if (!Number.isSafeInteger(entry.typeId) ||
        typeof entry.displayName !== "string" || entry.displayName.length === 0 ||
        typeof entry.canonicalIdentity !== "string" || entry.canonicalIdentity.length === 0 ||
        typeof entry.assemblyIdentity !== "string" || entry.assemblyIdentity.length === 0)
      throw new Error("invalid exception type map entry");
    types.set(entry.typeId, entry);
    previousTypeId = entry.typeId;
  }
  return { manifest, types };
}

async function sha256(bytes, cryptoProvider) {
  const digest = new Uint8Array(await cryptoProvider.subtle.digest("SHA-256", bytes));
  return Array.from(digest, value => value.toString(16).padStart(2, "0")).join("");
}
