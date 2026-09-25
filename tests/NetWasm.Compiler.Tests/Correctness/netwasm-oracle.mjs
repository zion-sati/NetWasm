import { readFile } from "node:fs/promises";
import { readExceptionClause } from "./exception-metadata.mjs";
import { createReactorOracleImports } from "./reactor-oracle-imports.mjs";
import { createRefreshableDataView } from "./refreshable-data-view.mjs";
import { createDeclaredOracleImports } from "./declared-oracle-imports.mjs";

var [modulePath, inputText, target = "wasm32", staticDataEndOrConfiguration,
  explicitHostConfigurationPath] =
  process.argv.slice(2);
var batched = inputText?.startsWith("@") ?? false;
var inputs = batched
  ? JSON.parse(await readFile(inputText.slice(1), "utf8"))
  : [Number.parseInt(inputText, 10)];
if (!modulePath || inputText === undefined) {
  throw new Error("usage: netwasm-oracle.mjs <module.wasm> <input> [wasm32|wasm64]");
}
var memory64 = target === "wasm64";
var hasExplicitStaticDataEnd = /^\d+$/.test(staticDataEndOrConfiguration ?? "");
var staticDataEndText = hasExplicitStaticDataEnd
  ? staticDataEndOrConfiguration
  : "0";
var hostConfigurationPath = hasExplicitStaticDataEnd
  ? explicitHostConfigurationPath
  : staticDataEndOrConfiguration;
var staticDataEnd = Number.parseInt(staticDataEndText, 10);
if (!Number.isSafeInteger(staticDataEnd) || staticDataEnd < 0) {
  throw new Error(`invalid static data end '${staticDataEndText}'`);
}
var initialMemoryPages = Math.max(1, Math.ceil(staticDataEnd / 65536));
var createTargetMemory = () => new WebAssembly.Memory(memory64
  ? { initial: BigInt(initialMemoryPages), address: "i64" }
  : { initial: initialMemoryPages });
var hostConfiguration = hostConfigurationPath
  ? JSON.parse(await readFile(hostConfigurationPath, "utf8"))
  : {};
var stackTraceSymbolDocument = hostConfiguration.stackTraceSymbolsPath
  ? JSON.parse(await readFile(hostConfiguration.stackTraceSymbolsPath, "utf8"))
  : null;
if (stackTraceSymbolDocument &&
    (stackTraceSymbolDocument.schemaVersion !== 1 ||
      !Array.isArray(stackTraceSymbolDocument.methods))) {
  throw new Error("invalid stack-trace symbol sidecar");
}

var memory = createTargetMemory();
var heap = 16;
var runtimeInitialized = false;
var lastException = 0;
var terminalExceptionTypeId = null;
var departedExceptionFrame = null;
var filterSearchFloor = 0;
var application;
var wasiEnvironmentReads = 0;
var wasiPreopenReads = 0;
var wasiEnvironmentResult = null;
var wasiEnvironmentLength = null;
var wasiOpenCalls = 0;
var wasiReadCalls = 0;
var watchedTokens = [];
var canceledTokens = new Set();
var reactorPhase = "initialization";
var typeBases = new Map();
var typeSizes = new Map();
var typeAssignable = new Map();
var typeInterfaces = new Set();
var valueTypeSizes = new Map();
var valueTypePayloadOffsets = new Map();
var typeObjects = new Map();
var exceptionFrames = [];
var stackTraceFrames = [];
var stackTraceSymbols = new Map();
var stackTraceExceptionFieldOffset = null;
var stackTraceStringTypeId = 0;
var weakHandles = [];
var strongHandles = [];
var gcHandles = [null];
var zero = () => 0;
var zeroAddress = () => memory64 ? 0n : 0;
var asAddress = value => memory64 ? BigInt(value) : value;
var asNumber = value => Number(value);
var plus = (value, offset) => memory64
  ? value + BigInt(offset)
  : value + offset;
var raw = size => {
  size = Number(size);
  var pointer = heap;
  heap = (heap + size + 3) & ~3;
  var pages = Math.ceil(heap / 65536);
  var current = memory.buffer.byteLength / 65536;
  if (pages > current) memory.grow(memory64
    ? BigInt(pages - current)
    : pages - current);
  return asAddress(pointer);
};
var valueEnter = size => raw(Math.max(memory64 ? 8 : 4, Number(size)));
var rootEnter = count => {
  var slotSize = memory64 ? 8 : 4;
  var frame = raw(slotSize + count * slotSize);
  var view = createRefreshableDataView(memory);
  view.setInt32(asNumber(frame), 0, true);
  for (var index = 0; index < count; index++) {
    var slot = plus(frame, (memory64 ? 8 : 4) + index * slotSize);
    if (memory64) view.setBigInt64(asNumber(slot), 0n, true);
    else view.setInt32(asNumber(slot), 0, true);
  }
  return plus(frame, memory64 ? 8 : 4);
};
var componentReallocate = (oldAddress, oldSize, alignment, newSize) => {
  oldAddress = asNumber(oldAddress);
  oldSize = Number(oldSize);
  alignment = Number(alignment);
  newSize = Number(newSize);
  if (newSize === 0) return zeroAddress();
  if (alignment <= 0 || (alignment & (alignment - 1)) !== 0) {
    throw new Error("invalid component alignment");
  }
  var allocated = asNumber(raw(newSize + alignment - 1));
  var aligned = (allocated + alignment - 1) & ~(alignment - 1);
  if (oldAddress) {
    var count = Math.min(oldSize, newSize);
    new Uint8Array(memory.buffer, aligned, count).set(
      new Uint8Array(memory.buffer, oldAddress, count));
  }
  return asAddress(aligned);
};
var randomByte = 0;
var wasiRandom = {
  "get-random-bytes"(length, result) {
    var count = Number(length);
    var address = componentReallocate(0, 0, 1, count);
    var bytes = new Uint8Array(memory.buffer, asNumber(address), count);
    for (var index = 0; index < count; index++) bytes[index] = (++randomByte) & 255;
    var view = createRefreshableDataView(memory);
    writeAddress(view, result, 0, address);
    writeAddress(view, result, memory64 ? 8 : 4, count);
  },
};
var writeAddress = (view, address, offset, value) => {
  if (memory64) view.setBigUint64(asNumber(plus(address, offset)), BigInt(value), true);
  else view.setUint32(asNumber(plus(address, offset)), Number(value), true);
};
var lowerBytes = bytes => {
  if (bytes.length === 0) return asAddress(1);
  var address = raw(bytes.length);
  new Uint8Array(memory.buffer, asNumber(address), bytes.length).set(bytes);
  return address;
};
var lowerText = value => lowerBytes(new TextEncoder().encode(value));
var environmentEntries = () => Object.entries(hostConfiguration.environment ?? {});
var wasiEnvironment = {
  "get-environment"(result) {
    wasiEnvironmentReads++;
    wasiEnvironmentResult = result === undefined ? "undefined" : String(result);
    var entries = environmentEntries();
    var addressSize = memory64 ? 8 : 4;
    var stride = addressSize * 4;
    var elements = entries.length === 0
      ? asAddress(addressSize)
      : raw(entries.length * stride);
    var view = createRefreshableDataView(memory);
    for (var index = 0; index < entries.length; index++) {
      var element = plus(elements, index * stride);
      var name = new TextEncoder().encode(entries[index][0]);
      var value = new TextEncoder().encode(entries[index][1]);
      writeAddress(view, element, 0, lowerBytes(name));
      writeAddress(view, element, addressSize, name.length);
      writeAddress(view, element, addressSize * 2, lowerBytes(value));
      writeAddress(view, element, addressSize * 3, value.length);
    }
    writeAddress(view, result, 0, elements);
    writeAddress(view, result, addressSize, entries.length);
    wasiEnvironmentLength = entries.length;
  },
};
var timeZoneAsset = hostConfiguration.timeZoneAssetPath
  ? await readFile(hostConfiguration.timeZoneAssetPath)
  : null;
var drainReactor = hostConfiguration.drainReactor === true;
var observeExportName = hostConfiguration.observeExportName ?? "run";
var entryExportName = hostConfiguration.entryExportName ?? "run";
var entryInvocationCount = hostConfiguration.entryInvocationCount ?? 1;
if (!Number.isInteger(entryInvocationCount) || entryInvocationCount < 1) {
  throw new Error("entry invocation count must be a positive integer");
}
var observeAsyncProcess = hostConfiguration.observeAsyncProcess === true;
var descriptors = new Map();
var nextDescriptor = 2;
var wasiPreopens = {
  "get-directories"(result) {
    wasiPreopenReads++;
    var addressSize = memory64 ? 8 : 4;
    var view = createRefreshableDataView(memory);
    if (!timeZoneAsset) {
      writeAddress(view, result, 0, asAddress(addressSize));
      writeAddress(view, result, addressSize, 0);
      return;
    }
    var elements = raw(addressSize * 3);
    var path = new TextEncoder().encode("/netwasm-timezones");
    view.setInt32(asNumber(elements), 1, true);
    writeAddress(view, elements, addressSize, lowerBytes(path));
    writeAddress(view, elements, addressSize * 2, path.length);
    writeAddress(view, result, 0, elements);
    writeAddress(view, result, addressSize, 1);
  },
};
var wasiFilesystem = {
  "[method]descriptor.open-at"(
    directoryHandle,
    pathFlags,
    pathAddress,
    pathLength,
    openFlags,
    descriptorFlags,
    result) {
    wasiOpenCalls++;
    var path = new TextDecoder().decode(new Uint8Array(
      memory.buffer,
      asNumber(pathAddress),
      Number(pathLength)));
    var view = createRefreshableDataView(memory);
    if (directoryHandle !== 1 || path !== "netwasm-timezones.nwtz" || !timeZoneAsset) {
      view.setUint8(asNumber(result), 1);
      view.setInt32(asNumber(plus(result, 4)), 44, true);
      return;
    }
    var descriptor = nextDescriptor++;
    descriptors.set(descriptor, timeZoneAsset);
    view.setUint8(asNumber(result), 0);
    view.setInt32(asNumber(plus(result, 4)), descriptor, true);
  },
  "[method]descriptor.read"(handle, length, offset, result) {
    wasiReadCalls++;
    var addressSize = memory64 ? 8 : 4;
    var payloadOffset = addressSize;
    var view = createRefreshableDataView(memory);
    var bytes = descriptors.get(handle);
    if (!bytes) {
      view.setUint8(asNumber(result), 1);
      view.setInt32(asNumber(plus(result, payloadOffset)), 8, true);
      return;
    }
    var start = Number(offset);
    var count = Math.min(Number(length), Math.max(0, bytes.length - start));
    var chunk = bytes.subarray(start, start + count);
    view.setUint8(asNumber(result), 0);
    writeAddress(view, result, payloadOffset, lowerBytes(chunk));
    writeAddress(view, result, payloadOffset + addressSize, chunk.length);
    view.setUint8(
      asNumber(plus(result, payloadOffset + addressSize * 2)),
      start + count >= bytes.length ? 1 : 0);
  },
  "[resource-drop]descriptor"(handle) {
    descriptors.delete(handle);
  },
};
wasiFilesystem.descriptor_open_at =
  wasiFilesystem["[method]descriptor.open-at"];
wasiFilesystem.descriptor_read =
  wasiFilesystem["[method]descriptor.read"];
wasiFilesystem.descriptor_drop =
  wasiFilesystem["[resource-drop]descriptor"];
var wasiWallClock = {
  now(result) {
    var view = createRefreshableDataView(memory);
    view.setBigUint64(asNumber(result), 0n, true);
    view.setUint32(asNumber(plus(result, 8)), 123000000, true);
  },
  resolution(result) {
    var view = createRefreshableDataView(memory);
    view.setBigUint64(asNumber(result), 0n, true);
    view.setUint32(asNumber(plus(result, 8)), 1, true);
  },
};
var nativeSizes = new Map();
var nativeAlloc = size => {
  var count = Math.max(1, Number(size));
  var pointer = raw(count);
  nativeSizes.set(pointer, count);
  return pointer;
};
var nativeRealloc = (oldAddress, size) => {
  var pointer = nativeAlloc(size);
  if (oldAddress !== zeroAddress()) {
    var count = Math.min(nativeSizes.get(oldAddress) ?? 0, Number(size));
    new Uint8Array(memory.buffer, asNumber(pointer), count).set(
      new Uint8Array(memory.buffer, asNumber(oldAddress), count));
    nativeSizes.delete(oldAddress);
  }
  return pointer;
};
var nativeAlignedAlloc = (size, alignment) => {
  var value = Number(alignment);
  if (value <= 0 || (value & (value - 1)) !== 0) return zeroAddress();
  var count = Math.max(1, Number(size));
  var allocation = asNumber(raw(count + value - 1));
  var aligned = (allocation + value - 1) & ~(value - 1);
  var pointer = asAddress(aligned);
  nativeSizes.set(pointer, count);
  return pointer;
};
var nativeAlignedRealloc = (oldAddress, size, alignment) => {
  var pointer = nativeAlignedAlloc(size, alignment);
  if (pointer === zeroAddress()) return pointer;
  if (oldAddress !== zeroAddress()) {
    var count = Math.min(nativeSizes.get(oldAddress) ?? 0, Number(size));
    new Uint8Array(memory.buffer, asNumber(pointer), count).set(
      new Uint8Array(memory.buffer, asNumber(oldAddress), count));
    nativeSizes.delete(oldAddress);
  }
  return pointer;
};
var allocate = (size, type) => {
  if (!typeBases.has(type)) {
    throw new Error(`allocation used unregistered type ${type}`);
  }
  var pointer = raw(size);
  createRefreshableDataView(memory).setInt32(asNumber(pointer), type, true);
  return pointer;
};
var allocateString = (value, length, type) => {
  if (length < 0 || length > 0x7ffffffb) return zeroAddress();
  var header = memory64 ? 12 : 8;
  var pointer = allocate(header + length * 2, type);
  var view = createRefreshableDataView(memory);
  view.setInt32(asNumber(plus(pointer, memory64 ? 8 : 4)), length, true);
  for (var index = 0; index < length; index++) {
    view.setUint16(asNumber(plus(pointer, header + index * 2)), value, true);
  }
  return pointer;
};
var allocateText = (text, type) => {
  var pointer = allocateString(0, text.length, type);
  var header = memory64 ? 12 : 8;
  var view = createRefreshableDataView(memory);
  for (var index = 0; index < text.length; index++) {
    view.setUint16(asNumber(plus(pointer, header + index * 2)),
      text.charCodeAt(index), true);
  }
  return pointer;
};
var getTypeObject = (semantic, type) => {
  if (typeObjects.has(semantic)) return typeObjects.get(semantic);
  var pointer = allocate(memory64 ? 16 : 8, type);
  createRefreshableDataView(memory).setInt32(
    asNumber(plus(pointer, memory64 ? 8 : 4)), semantic, true);
  typeObjects.set(semantic, pointer);
  return pointer;
};
var allocateArray = (
  length,
  type,
  elementType,
  size = memory64 ? 8 : 4) => {
  var data = raw(Math.max(4, length * size));
  var pointer = allocate(memory64 ? 32 : 16, type);
  var view = createRefreshableDataView(memory);
  view.setInt32(asNumber(plus(pointer, memory64 ? 8 : 4)), length, true);
  if (memory64) {
    view.setBigInt64(asNumber(plus(pointer, 16)), data, true);
    view.setInt32(asNumber(plus(pointer, 24)), elementType, true);
  } else {
    view.setInt32(asNumber(plus(pointer, 8)), data, true);
    view.setInt32(asNumber(plus(pointer, 12)), elementType, true);
  }
  return pointer;
};
var allocateRectangularArray = (rank, dimensions, type, element, size, references, bounded = false) => {
  if (rank <= 0 || rank > 32) return zeroAddress();
  const view = createRefreshableDataView(memory);
  const dimensionAddress = asNumber(dimensions);
  const lengths = [];
  let total = 1;
  let stride = 1;
  for (let dimension = rank - 1; dimension >= 0; dimension--) {
    const length = view.getInt32(dimensionAddress + dimension * 4, true);
    lengths[dimension] = length;
    if (length < 0 || length > 1073741823 ||
        (length !== 0 && total > Math.floor(1073741823 / length))) return zeroAddress();
    total *= length;
  }
  const shape = raw(rank * (bounded ? 12 : 8));
  const elements = total === 0
    ? zeroAddress()
    : raw(total * (references ? (memory64 ? 8 : 4) : size));
  const array = allocate(memory64 ? 40 : 24, type);
  const output = createRefreshableDataView(memory);
  for (let dimension = rank - 1; dimension >= 0; dimension--) {
    const length = lengths[dimension];
    output.setInt32(asNumber(plus(shape, dimension * 8)), length, true);
    output.setInt32(asNumber(plus(shape, dimension * 8 + 4)), stride, true);
    stride *= length;
    if (bounded) output.setInt32(asNumber(plus(shape, rank * 8 + dimension * 4)),
      view.getInt32(dimensionAddress + (rank + dimension) * 4, true), true);
  }
  output.setInt32(asNumber(plus(array, memory64 ? 8 : 4)), total, true);
  if (memory64) {
    output.setBigInt64(asNumber(plus(array, 16)), elements, true);
    output.setInt32(asNumber(plus(array, 24)), element, true);
    output.setInt32(asNumber(plus(array, 28)), bounded ? rank | 0x80000000 : rank, true);
    output.setBigInt64(asNumber(plus(array, 32)), shape, true);
  } else {
    output.setInt32(asNumber(plus(array, 8)), asNumber(elements), true);
    output.setInt32(asNumber(plus(array, 12)), element, true);
    output.setInt32(asNumber(plus(array, 16)), bounded ? rank | 0x80000000 : rank, true);
    output.setInt32(asNumber(plus(array, 20)), asNumber(shape), true);
  }
  return array;
};
var isTypeAssignable = (type, target) => {
  while (type) {
    if (type === target || typeAssignable.get(type)?.has(target)) return 1;
    type = typeBases.get(type) || 0;
  }
  return 0;
};
var isAssignable = (object, target) => {
  if (!object) return 0;
  const type = createRefreshableDataView(memory).getInt32(asNumber(object), true);
  return isTypeAssignable(type, target);
};
var copyArray = (source, sourceIndex, destination, destinationIndex, length) => {
  const view = createRefreshableDataView(memory);
  const sourceAddress = asNumber(source);
  const destinationAddress = asNumber(destination);
  const dataOffset = memory64 ? 16 : 8;
  const elementTypeOffset = memory64 ? 24 : 12;
  const sourceElement = view.getInt32(sourceAddress + elementTypeOffset, true);
  const destinationElement = view.getInt32(destinationAddress + elementTypeOffset, true);
  const sourceValueSize = valueTypeSizes.get(sourceElement);
  const destinationValueSize = valueTypeSizes.get(destinationElement);
  const sourceIsValue = sourceValueSize !== undefined;
  const destinationIsValue = destinationValueSize !== undefined;
  const sourceType = view.getInt32(sourceAddress, true);
  const destinationType = view.getInt32(destinationAddress, true);
  if (!isTypeAssignable(sourceType, destinationType) &&
      !isTypeAssignable(destinationType, sourceType) &&
      !typeInterfaces.has(sourceElement) &&
      !typeInterfaces.has(destinationElement)) return 1;
  if (sourceIsValue !== destinationIsValue) return 1;
  const sourceData = memory64
    ? view.getBigUint64(sourceAddress + dataOffset, true)
    : view.getUint32(sourceAddress + dataOffset, true);
  const destinationData = memory64
    ? view.getBigUint64(destinationAddress + dataOffset, true)
    : view.getUint32(destinationAddress + dataOffset, true);
  if (sourceIsValue) {
    if (sourceValueSize !== destinationValueSize) return 1;
    const byteCount = length * sourceValueSize;
    const sourceStart = asNumber(plus(sourceData, sourceIndex * sourceValueSize));
    const destinationStart = asNumber(
      plus(destinationData, destinationIndex * destinationValueSize));
    const copy = new Uint8Array(memory.buffer, sourceStart, byteCount).slice();
    new Uint8Array(memory.buffer, destinationStart, byteCount).set(copy);
    return 0;
  }
  const referenceSize = memory64 ? 8 : 4;
  const read = address => memory64
    ? view.getBigUint64(address, true)
    : view.getUint32(address, true);
  const write = (address, value) => memory64
    ? view.setBigUint64(address, value, true)
    : view.setUint32(address, value, true);
  const copyBackward = source === destination &&
    destinationIndex > sourceIndex && destinationIndex < sourceIndex + length;
  for (let step = 0; step < length; step++) {
    const offset = copyBackward ? length - step - 1 : step;
    const value = read(asNumber(plus(
      sourceData,
      (sourceIndex + offset) * referenceSize)));
    if (value && !isAssignable(value, destinationElement)) return 2;
    write(
      asNumber(plus(destinationData, (destinationIndex + offset) * referenceSize)),
      value);
  }
  return 0;
};
var cloneArray = source => {
  const view = createRefreshableDataView(memory);
  const address = asNumber(source);
  const type = view.getInt32(address, true);
  const length = view.getInt32(address + (memory64 ? 8 : 4), true);
  const element = view.getInt32(address + (memory64 ? 24 : 12), true);
  const valueSize = valueTypeSizes.get(element);
  let clone;
  if (typeSizes.get(type) === (memory64 ? 40 : 24)) {
    const rankFlags = view.getInt32(address + (memory64 ? 28 : 16), true);
    const rank = rankFlags & 0x7fffffff;
    const shape = memory64
      ? view.getBigUint64(address + 32, true)
      : view.getUint32(address + 20, true);
    const dimensions = raw(rank * (rankFlags < 0 ? 8 : 4));
    const output = createRefreshableDataView(memory);
    for (let dimension = 0; dimension < rank; dimension++) {
      output.setInt32(
        asNumber(plus(dimensions, dimension * 4)),
        output.getInt32(asNumber(plus(shape, dimension * 8)), true),
        true);
      if (rankFlags < 0) output.setInt32(asNumber(plus(dimensions, (rank + dimension) * 4)),
        output.getInt32(asNumber(plus(shape, rank * 8 + dimension * 4)), true), true);
    }
    clone = allocateRectangularArray(
      rank,
      dimensions,
      type,
      element,
      valueSize ?? 0,
      valueSize === undefined ? 1 : 0,
      rankFlags < 0);
  } else {
    clone = allocateArray(
      length,
      type,
      element,
      valueSize ?? (memory64 ? 8 : 4));
  }
  return copyArray(source, 0, clone, 0, length) === 0
    ? clone
    : zeroAddress();
};
var clearArray = (array, index, length) => {
  const view = createRefreshableDataView(memory);
  const address = asNumber(array);
  const dataOffset = memory64 ? 16 : 8;
  const elementTypeOffset = memory64 ? 24 : 12;
  const element = view.getInt32(address + elementTypeOffset, true);
  const elementSize = valueTypeSizes.get(element) ?? (memory64 ? 8 : 4);
  const data = memory64
    ? view.getBigUint64(address + dataOffset, true)
    : view.getUint32(address + dataOffset, true);
  new Uint8Array(
    memory.buffer,
    asNumber(plus(data, index * elementSize)),
    length * elementSize).fill(0);
};
var captureStackTrace = exception => {
  if (stackTraceExceptionFieldOffset === null || !exception ||
      stackTraceFrames.length === 0) return;
  var trace = formatStackTrace(stackTraceFrames);
  var traceAddress = allocateText(trace, stackTraceStringTypeId);
  var slot = plus(exception, stackTraceExceptionFieldOffset);
  var view = createRefreshableDataView(memory);
  if (memory64) view.setBigUint64(asNumber(slot), traceAddress, true);
  else view.setUint32(asNumber(slot), traceAddress, true);
};
var dispatchException = exception => {
  lastException = exception;
  var view = createRefreshableDataView(memory);
  for (var frame = exceptionFrames.length; frame > filterSearchFloor; frame--) {
    var active = exceptionFrames[frame - 1];
    active.targetClause = 0;
    var rawCount = active.count >>> 0;
    var filtered = (rawCount >>> 31) !== 0;
    var count = rawCount & 0x7fffffff;
    for (var clause = 0; clause < count; clause++) {
      var accepted = 0;
      if (filtered) {
        var entry = readExceptionClause(
          view,
          active.metadata,
          clause,
          plus,
          asNumber);
        var kind = entry.kind;
        if (kind === 0) {
          accepted = isAssignable(exception, entry.value);
        } else {
          var saved = [lastException, filterSearchFloor];
          filterSearchFloor = frame;
          accepted = application.exports["netwasm.filter"](
            entry.value,
            exception,
            active.environment);
          [lastException, filterSearchFloor] = saved;
        }
      } else {
        accepted = isAssignable(
          exception,
          view.getInt32(asNumber(plus(active.metadata, clause * 4)), true));
      }
      if (accepted) {
        active.targetClause = clause + 1;
        return;
      }
    }
  }
};
var beginThrow = exception => {
  captureStackTrace(exception);
  dispatchException(exception);
};
var runtimeValues = {
  memory,
  initialize(staticEnd) {
    if (runtimeInitialized) return;
    heap = (Number(staticEnd) + 7) & ~7;
    runtimeInitialized = true;
  },
  allocate,
  // This compiler-only oracle retains its allocations. Collection is explicitly
  // simulated; it must not qualify reclamation, weak clearing, or finalization.
  collect: zero,
  component_realloc: componentReallocate,
  component_free: zero,
  native_alloc: nativeAlloc,
  native_realloc: nativeRealloc,
  native_free(address) { nativeSizes.delete(address); },
  native_aligned_alloc: nativeAlignedAlloc,
  native_aligned_realloc: nativeAlignedRealloc,
  native_aligned_free(address) { nativeSizes.delete(address); },
  register_type(type, base, size, bitmap, bitmapBits, assignableTypes,
      assignableTypeCount, hasFinalizer, isInterface) {
    typeBases.set(type, base);
    typeSizes.set(type, Number(size));
    const ids = new Set();
    const view = createRefreshableDataView(memory);
    for (var index = 0; index < assignableTypeCount; index++) {
      ids.add(view.getInt32(asNumber(plus(assignableTypes, index * 4)), true));
    }
    typeAssignable.set(type, ids);
    if (isInterface) typeInterfaces.add(type);
  },
  register_value_type(type, size, boxedPayloadOffset) {
    valueTypeSizes.set(type, Number(size));
    valueTypePayloadOffsets.set(type, Number(boxedPayloadOffset));
  },
  register_static_root: zero,
  root_frame_enter: rootEnter,
  root_frame_leave: zero,
  value_frame_enter: valueEnter,
  value_frame_leave: zero,
  handle_new(target) {
    strongHandles.push(target);
    return strongHandles.length - 1;
  },
  handle_get(handle) { return strongHandles[handle] ?? zeroAddress(); },
  handle_release(handle) { strongHandles[handle] = zeroAddress(); },
  begin_throw: beginThrow,
  begin_rethrow: dispatchException,
  stack_trace_initialize(exceptionFieldOffset, stringTypeId) {
    if (stackTraceExceptionFieldOffset !== null &&
        (stackTraceExceptionFieldOffset !== exceptionFieldOffset ||
          stackTraceStringTypeId !== stringTypeId)) {
      throw new Error("stack trace initialized with conflicting layout");
    }
    stackTraceExceptionFieldOffset = exceptionFieldOffset;
    stackTraceStringTypeId = stringTypeId;
  },
  stack_trace_register_symbol(methodId, characters, length) {
    var view = createRefreshableDataView(memory);
    var name = "";
    for (var index = 0; index < length; index++) {
      name += String.fromCharCode(view.getUint16(
        asNumber(plus(characters, index * 2)), true));
    }
    stackTraceSymbols.set(methodId, name);
  },
  stack_trace_frame_enter(methodId) {
    stackTraceFrames.push(methodId);
  },
  stack_trace_frame_leave(methodId) {
    if (stackTraceFrames.at(-1) !== methodId) {
      throw new Error(
        `unbalanced stack-trace frame: leave=${stackTraceSymbols.get(methodId) ?? methodId}, ` +
        `frames=${stackTraceFrames.map(id => stackTraceSymbols.get(id) ?? id).join(",")}`);
    }
    stackTraceFrames.pop();
  },
  allocate_reference_array: allocateArray,
  allocate_value_array: allocateArray,
  allocate_rectangular_array: allocateRectangularArray,
  allocate_bounded_rectangular_array: (...args) => allocateRectangularArray(...args, true),
  array_rank(array) {
    const view = createRefreshableDataView(memory);
    const address = asNumber(array);
    const type = view.getInt32(address, true);
    if (typeSizes.get(type) !== (memory64 ? 40 : 24)) return 1;
    return view.getInt32(address + (memory64 ? 28 : 16), true) & 0x7fffffff;
  },
  array_get_length(array, dimension) {
    const view = createRefreshableDataView(memory);
    const address = asNumber(array);
    const type = view.getInt32(address, true);
    if (typeSizes.get(type) !== (memory64 ? 40 : 24)) {
      return dimension === 0
        ? view.getInt32(address + (memory64 ? 8 : 4), true)
        : -1;
    }
    const rank = view.getInt32(address + (memory64 ? 28 : 16), true) & 0x7fffffff;
    if (dimension < 0 || dimension >= rank) return -1;
    const shape = memory64
      ? view.getBigInt64(address + 32, true)
      : view.getInt32(address + 20, true);
    return view.getInt32(asNumber(plus(shape, dimension * 8)), true);
  },
  array_get_lower_bound(array, dimension) {
    const view = createRefreshableDataView(memory);
    const address = asNumber(array);
    const type = view.getInt32(address, true);
    if (typeSizes.get(type) !== (memory64 ? 40 : 24)) return 0;
    const rankFlags = view.getInt32(address + (memory64 ? 28 : 16), true);
    if (rankFlags >= 0) return 0;
    const rank = rankFlags & 0x7fffffff;
    const shape = memory64 ? view.getBigInt64(address + 32, true) : view.getInt32(address + 20, true);
    return view.getInt32(asNumber(plus(shape, rank * 8 + dimension * 4)), true);
  },
  array_get_value(array, index) {
    const view = createRefreshableDataView(memory);
    const address = asNumber(array);
    const length = view.getInt32(address + (memory64 ? 8 : 4), true);
    if (index < 0 || index >= length) throw new Error("array index out of range");
    const data = memory64
      ? view.getBigUint64(address + 16, true)
      : view.getUint32(address + 8, true);
    const element = view.getInt32(address + (memory64 ? 24 : 12), true);
    const valueSize = valueTypeSizes.get(element);
    if (valueSize === undefined) {
      return memory64
        ? view.getBigUint64(asNumber(plus(data, index * 8)), true)
        : view.getUint32(asNumber(plus(data, index * 4)), true);
    }
    const boxed = allocate(typeSizes.get(element), element);
    const payload = valueTypePayloadOffsets.get(element);
    new Uint8Array(memory.buffer, asNumber(plus(boxed, payload)), valueSize).set(
      new Uint8Array(memory.buffer, asNumber(plus(data, index * valueSize)), valueSize));
    return boxed;
  },
  array_copy: copyArray,
  array_clear: clearArray,
  array_clone: cloneArray,
  allocate_string: allocateString,
  get_type_object: getTypeObject,
  suppress_finalize: zero,
  weak_handle_new(target) {
    weakHandles.push(target);
    return weakHandles.length - 1;
  },
  weak_handle_get(handle) { return weakHandles[handle] ?? zeroAddress(); },
  weak_handle_set(handle, target) { weakHandles[handle] = target; },
  weak_handle_release(handle) { weakHandles[handle] = zeroAddress(); },
  gc_handle_new(target, kind) {
    gcHandles.push({ target, kind });
    return ((gcHandles.length - 1) << 2) | kind;
  },
  gc_handle_get(handle) { return gcHandles[handle >> 2]?.target ?? zeroAddress(); },
  gc_handle_set(handle, target) {
    const entry = gcHandles[handle >> 2];
    if (entry) entry.target = target;
  },
  gc_handle_release(handle) { gcHandles[handle >> 2] = null; },
  gc_handle_address(handle) {
    return gcHandles[handle >> 2]?.target ?? zeroAddress();
  },
  reregister_for_finalize: zero,
  gc_get_metric() { return 0n; },
  gc_metric_is_supported(metric) { return metric !== 4; },
  gc_wait_for_pending_finalizers: zero,
  object_identity_hash(value) { return Number(value) | 0; },
  report_unobserved_task_exception: zero,
  is_assignable: isAssignable,
  end_catch() { lastException = 0; },
  exception_frame_enter(metadata, count) {
    exceptionFrames.push({
      metadata,
      count,
      environment: 0,
      targetClause: 0,
    });
    return exceptionFrames.length;
  },
  exception_frame_set_environment(token, environment) {
    exceptionFrames[token - 1].environment = environment;
  },
  exception_frame_leave(token) {
    if (token !== exceptionFrames.length) throw new Error("unbalanced EH frame");
    departedExceptionFrame = exceptionFrames.pop();
  },
  exception_frame_target_clause(token) {
    if (token !== exceptionFrames.length + 1) throw new Error("unbalanced EH query");
    return departedExceptionFrame.targetClause;
  },
  finalizer_safepoint: zeroAddress,
};
var runtime = createDeclaredOracleImports("netwasm.runtime.v1", runtimeValues);
var host = createDeclaredOracleImports("netwasm.host.v1", {
  write_i32: zero,
  // Report through the oracle observation, not an ignored host callback.
  report_terminal_exception_v1(typeId) { terminalExceptionTypeId = typeId; },
});

try {
  var bytes = await readFile(modulePath);
  var observations = [];
  for (var inputIndex = 0; inputIndex < inputs.length; inputIndex++) {
        var inputDescriptor = inputs[inputIndex];
        var input = inputDescriptor !== null
            && typeof inputDescriptor === "object"
            && typeof inputDescriptor.i64 === "string"
            ? BigInt(inputDescriptor.i64)
            : inputDescriptor;
    resetRuntimeState();
    try {
      const reactorImports = createReactorOracleImports(target, {
        watch: (_pollable, token) => watchedTokens.push(token),
        cancel: token => canceledTokens.add(token),
      });
      var instantiated = await WebAssembly.instantiate(bytes, {
        "netwasm.runtime.v1": runtime,
        "netwasm.host.v1": host,
        ...reactorImports,
        [`${memory64 ? "cm64p2" : "cm32p2"}|wasi:cli/environment@0.2`]:
          wasiEnvironment,
        [`${memory64 ? "cm64p2" : "cm32p2"}|wasi:filesystem/preopens@0.2`]:
          wasiPreopens,
        [`${memory64 ? "cm64p2" : "cm32p2"}|wasi:filesystem/types@0.2`]:
          wasiFilesystem,
        [`${memory64 ? "cm64p2" : "cm32p2"}|wasi:clocks/wall-clock@0.2`]:
          wasiWallClock,
        [`${memory64 ? "cm64p2" : "cm32p2"}|wasi:random/random@0.2`]:
          wasiRandom,
        });
      application = instantiated.instance;
      var initialize = application.exports[
        memory64 ? "cm64p2_initialize" : "cm32p2_initialize"];
      reactorPhase = "initialization";
      if (typeof initialize === "function") initialize();
      reactorPhase = "entry";
      var value;
      for (var invocationIndex = 0;
           invocationIndex < entryInvocationCount;
           invocationIndex++) {
        value = application.exports[entryExportName](input);
      }
      if (drainReactor) {
        var canonicalPrefix = memory64 ? "cm64p2" : "cm32p2";
        var wake = application.exports[
          `${canonicalPrefix}|netwasm:runtime/reactor-guest@1|wake`];
        if (typeof wake !== "function") {
          throw new Error("reactor drain requested but wake export is missing");
        }
        while (watchedTokens.length) {
          var token = watchedTokens.shift();
          if (!canceledTokens.has(token)) {
            reactorPhase = "wake";
            wake(token);
          }
        }
        reactorPhase = "observe";
        if (observeAsyncProcess) {
          var status = application.exports["netwasm.process.status"];
          var result = application.exports["netwasm.process.result"];
          var complete = application.exports["netwasm.process.complete"];
          if (typeof status !== "function" || typeof result !== "function" ||
              typeof complete !== "function") {
            throw new Error("asynchronous process observation exports are missing");
          }
          var handle = value;
          if (status(handle) !== 1) {
            throw new Error("asynchronous process did not complete successfully");
          }
          value = result(handle);
          complete(handle);
        } else {
          var observe = application.exports[observeExportName];
          if (typeof observe !== "function") {
            throw new Error(
              `reactor drain requested but ${observeExportName} export is missing`);
          }
          value = observe(input);
        }
      }
      reactorPhase = "completed";
      observations.push({
        kind: "value",
        value,
        exceptionTypeId: null,
        trace: readTrace(),
        traceRecords: readTraceRecords(),
        wasiEnvironmentReads,
        wasiPreopenReads,
        wasiEnvironmentResult,
        wasiEnvironmentLength,
      });
    } catch (error) {
      observations.push(createException(error));
    }
    process.stderr.write(`NETWASM_PROGRESS ${inputIndex + 1}/${inputs.length}\n`);
  }
  emit(batched ? observations : observations[0]);
} catch (error) {
  var observation = createException(error);
  emit(batched ? inputs.map(() => observation) : observation);
}

function resetRuntimeState() {
  memory = createTargetMemory();
  runtimeValues.memory = memory;
  heap = 16;
  runtimeInitialized = false;
  lastException = 0;
  terminalExceptionTypeId = null;
  departedExceptionFrame = null;
  filterSearchFloor = 0;
  typeBases = new Map();
  typeSizes = new Map();
  typeAssignable = new Map();
  typeInterfaces = new Set();
  valueTypeSizes = new Map();
  valueTypePayloadOffsets = new Map();
  typeObjects = new Map();
  exceptionFrames = [];
  stackTraceFrames = [];
  stackTraceSymbols = new Map((stackTraceSymbolDocument?.methods ?? [])
    .map(symbol => [symbol.id, symbol.name]));
  stackTraceExceptionFieldOffset = null;
  stackTraceStringTypeId = 0;
  weakHandles = [zeroAddress()];
  strongHandles = [zeroAddress()];
  gcHandles = [null];
  descriptors = new Map();
  nextDescriptor = 2;
  application = undefined;
  wasiEnvironmentReads = 0;
  wasiPreopenReads = 0;
  wasiEnvironmentResult = null;
  wasiEnvironmentLength = null;
  randomByte = 0;
  wasiOpenCalls = 0;
  wasiReadCalls = 0;
  watchedTokens = [];
  canceledTokens = new Set();
  reactorPhase = "initialization";
}

function readTrace() {
  return typeof application?.exports.trace === "function"
    ? application.exports.trace()
    : 0;
}

function readTraceRecords() {
  if (typeof application?.exports.trace_count !== "function") return [];
  var records = [];
  var count = application.exports.trace_count();
  for (var index = 0; index < count; index++) {
    records.push({
      kind: application.exports.trace_kind(index),
      eventId: application.exports.trace_event_id(index),
      payloadLow: application.exports.trace_payload_low(index),
      payloadHigh: application.exports.trace_payload_high(index),
    });
  }
  return records;
}

function createException(error) {
  var exceptionTypeId = terminalExceptionTypeId ?? (lastException
    ? createRefreshableDataView(memory).getInt32(asNumber(lastException), true)
    : null);
  return {
    kind: exceptionTypeId !== null ? "exception" : "trap",
    value: null,
    exceptionTypeId,
    trace: readTrace(),
    traceRecords: readTraceRecords(),
    detail: (error instanceof Error ? `${error.name}: ${error.message}` : String(error)) +
      `; reactorPhase=${reactorPhase}`,
    wasiEnvironmentReads,
    wasiPreopenReads,
    wasiOpenCalls,
    wasiReadCalls,
    managedMessage: readManagedExceptionMessage(),
    managedStackTrace: readManagedExceptionStackTrace() ?? readActiveStackTrace(),
  };
}

function readActiveStackTrace() {
  return stackTraceFrames.length === 0 ? null : formatStackTrace(stackTraceFrames);
}

function formatStackTrace(frames) {
  return frames.slice().reverse().map(methodId =>
    `at ${stackTraceSymbols.get(methodId) ?? `method#${methodId}`}`).join("\n");
}

function readManagedExceptionMessage() {
  if (!lastException) return null;
  var view = createRefreshableDataView(memory);
  var fieldOffset = memory64 ? 8 : 4;
  var message = memory64
    ? view.getBigInt64(asNumber(lastException) + fieldOffset, true)
    : view.getInt32(asNumber(lastException) + fieldOffset, true);
  return readManagedText(message);
}

function readManagedExceptionStackTrace() {
  if (!lastException || stackTraceExceptionFieldOffset === null) return null;
  var view = createRefreshableDataView(memory);
  var trace = memory64
    ? view.getBigInt64(
      asNumber(lastException) + stackTraceExceptionFieldOffset,
      true)
    : view.getInt32(
      asNumber(lastException) + stackTraceExceptionFieldOffset,
      true);
  return readManagedText(trace);
}

function readManagedText(text) {
  if (!text) return null;
  var view = createRefreshableDataView(memory);
  var fieldOffset = memory64 ? 8 : 4;
  var length = view.getInt32(asNumber(text) + fieldOffset, true);
  if (length < 0 || length > 4096) return null;
  var characters = new Array(length);
  var characterAddress = asNumber(text) + fieldOffset + 4;
  for (var index = 0; index < length; index++) {
    characters[index] = view.getUint16(characterAddress + index * 2, true);
  }
  return String.fromCharCode(...characters);
}

function emit(observation) {
  process.stdout.write(JSON.stringify(observation));
}
