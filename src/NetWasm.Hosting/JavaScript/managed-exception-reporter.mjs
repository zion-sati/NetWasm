const MAP_MEDIA_TYPE = "application/vnd.netwasm.exception-types+json;version=2";

export function createManagedExceptionReporter(options) {
  const {
    getMemory,
    stringDataOffset,
    reportImmediate,
    reportEnriched,
    loadArtifacts,
    maximumReportedMessageLength = Number.POSITIVE_INFINITY,
    crypto: cryptoProvider = globalThis.crypto,
  } = options;

  if (typeof getMemory !== "function") throw new TypeError("getMemory must be a function");
  if (!Number.isSafeInteger(stringDataOffset) || stringDataOffset < 0) throw new RangeError("stringDataOffset must be a non-negative safe integer");
  if (typeof reportImmediate !== "function") throw new TypeError("reportImmediate must be a function");
  if (typeof reportEnriched !== "function") throw new TypeError("reportEnriched must be a function");
  if (typeof loadArtifacts !== "function") throw new TypeError("loadArtifacts must be a function");
  if (maximumReportedMessageLength !== Number.POSITIVE_INFINITY &&
      (!Number.isSafeInteger(maximumReportedMessageLength) || maximumReportedMessageLength < 0))
    throw new RangeError("maximumReportedMessageLength must be a non-negative safe integer or Infinity");

  let nextEventId = 1;
  let artifactsPromise;
  const terminalEvents = [];

  function reportTerminalException(typeId, messageReference, messageLength) {
    if (!Number.isSafeInteger(typeId) || typeId <= 0 || typeId > 0x7fffffff)
      throw new RangeError("Managed exception type ID must be a positive i32 value.");

    const eventId = nextEventId++;
    const copiedMessage = copyManagedString(getMemory(), messageReference, messageLength, stringDataOffset);
    const messageTruncated = typeof copiedMessage === "string" && copiedMessage.length > maximumReportedMessageLength;
    const message = messageTruncated
      ? copiedMessage.slice(0, maximumReportedMessageLength)
      : copiedMessage;
    const terminalEvent = Object.freeze({ eventId, typeId, message, messageTruncated });
    terminalEvents.push(terminalEvent);
    reportImmediate(terminalEvent);

    artifactsPromise ??= loadAndValidateArtifacts(loadArtifacts, cryptoProvider);
    void artifactsPromise.then(
      artifacts => {
        const entry = artifacts.types.get(typeId);
        reportEnriched({
          eventId,
          typeId,
          typeName: entry?.displayName ?? `<unknown exception type #${typeId}>`,
          canonicalIdentity: entry?.canonicalIdentity,
          assemblyIdentity: entry?.assemblyIdentity,
          message,
          buildId: artifacts.manifest.buildId,
        });
      },
      error => {
        reportEnriched({ eventId, typeId, typeName: `<exception type #${typeId} unavailable>`, message, artifactError: String(error) });
      },
    );
  }

  return Object.freeze({
    importObject: Object.freeze({ report_terminal_exception_v1: reportTerminalException }),
    reportTerminalException,
    consumeTerminalEvent() {
      return terminalEvents.pop();
    },
  });
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

async function loadAndValidateArtifacts(loadArtifacts, cryptoProvider) {
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
