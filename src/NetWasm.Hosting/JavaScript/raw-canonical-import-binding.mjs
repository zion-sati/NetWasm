import {
  bindRawInstanceBoundary,
  createRawInstanceBoundary,
  readRawInstanceMemory,
  readRawInstanceReallocator,
  releaseRawExportedResource,
  requireRawInstanceFunction,
} from "./raw-instance-boundary.mjs";
import {
  createRawCanonicalMemoryAllocator,
  createRawCanonicalMemoryDeallocator,
} from "./raw-canonical-memory-allocator.mjs";
import {
  createRawCanonicalFlatValueLifter,
  createRawCanonicalMemoryValueReader,
} from "./raw-canonical-value-lifter.mjs";
import {
  createRawCanonicalFlatValueLowerer,
  createRawCanonicalMemoryValueWriter,
} from "./raw-canonical-value-lowerer.mjs";
import { createRawCanonicalCallableBinder } from "./raw-canonical-callable-binder.mjs";
import { createRawCanonicalResourceBinder } from "./raw-canonical-resource-binder.mjs";
import { projectRawCanonicalMemoryRange } from "./raw-canonical-memory-range-projector.mjs";
import { createRawPhysicalReactorBinder } from "./raw-physical-reactor-binder.mjs";
import { createRawProviderCallableBinder } from "./raw-provider-callable-binder.mjs";
import { releaseRawProviderResource } from "./raw-provider-resource-releaser.mjs";
import {
  borrowRawResource,
  closeRawResourceStore,
  createRawResourceStore,
  dropRawResource,
  registerRawResource,
  transferRawResource,
} from "./raw-resource-store.mjs";

const states = new WeakMap();
const requestKeys = ["adapter", "providers", "reactor"];
const bindKeys = ["binding", "instance"];
const closeKeys = ["binding"];
const metadataKeys = [
  "abiVersion",
  "bindingIdentities",
  "requiredCapabilities",
  "target",
  "witSourceFingerprint",
];
const identityKeys = ["module", "name"];
const productKeys = ["imports", "metadata"];
const capabilityOrder = Object.freeze(["bindCallable", "bindReactor", "bindResource"]);

export function createRawCanonicalImportBinding(request = {}) {
  assertExactObject(request, requestKeys, "raw canonical-import binding factory");
  const adapter = readAdapter(request.adapter);
  const metadata = readMetadata(adapter.metadata);
  const boundary = createRawInstanceBoundary({ target: metadata.target });
  const store = createRawResourceStore();
  const lifecycle = { boundary, closed: false, store };
  const readOpenMemory = () => readRawInstanceMemory({ boundary: readOpen(lifecycle).boundary });
  const readOpenStore = () => {
    const open = readOpen(lifecycle);
    readRawInstanceMemory({ boundary: open.boundary });
    return open.store;
  };
  const reallocate = (...values) => readRawInstanceReallocator({
    boundary: readOpen(lifecycle).boundary,
  })(...values);
  const allocateMemory = createRawCanonicalMemoryAllocator({ reallocate });
  const deallocateMemory = createRawCanonicalMemoryDeallocator({ reallocate });
  const liftActions = { borrowResource: borrowRawResource, transferResource: transferRawResource };
  const lowerActions = {
    allocateMemory,
    deallocateMemory,
    dropResource: dropRawResource,
    registerResource: registerRawResource,
    releaseResource: releaseRawProviderResource,
  };
  const callable = createRawCanonicalCallableBinder({
    bindProvider: createRawProviderCallableBinder({ providers: request.providers }),
    liftFlatValue: createRawCanonicalFlatValueLifter(liftActions),
    lowerFlatValue: createRawCanonicalFlatValueLowerer(lowerActions),
    projectMemoryRange: projectRawCanonicalMemoryRange,
    readMemory: readOpenMemory,
    readMemoryValue: createRawCanonicalMemoryValueReader(liftActions),
    readStore: readOpenStore,
    writeMemoryValue: createRawCanonicalMemoryValueWriter(lowerActions),
  });
  const resource = createRawCanonicalResourceBinder({
    borrowResource: borrowRawResource,
    dropResource: dropRawResource,
    readStore: readOpenStore,
    registerResource: registerRawResource,
    releaseExportedResource: request => releaseRawExportedResource({ boundary, ...request }),
    requireInstanceFunction: name => requireRawInstanceFunction({ boundary, name }),
  });
  const requiresReactor = metadata.requiredCapabilities.includes("bindReactor");
  if (!requiresReactor && request.reactor !== null) {
    throw new TypeError("raw canonical imports mix an undeclared reactor binding");
  }
  const reactor = requiresReactor
    ? createRawPhysicalReactorBinder({
        providers: request.providers,
        reactor: request.reactor,
        readStore: readOpenStore,
        releasePollable: releaseRawProviderResource,
        transferPollable: transferRawResource,
      })
    : null;
  const adapterRequest = Object.create(null);
  adapterRequest.metadata = metadata.value;
  for (const capability of metadata.requiredCapabilities) {
    adapterRequest[capability] = capability === "bindCallable"
      ? callable
      : capability === "bindReactor" ? reactor : resource;
  }
  const product = adapter.create(adapterRequest);
  const imports = readProduct(product, metadata);
  const binding = Object.freeze({ imports, target: metadata.target });
  states.set(binding, lifecycle);
  return binding;
}

export function bindRawCanonicalImportInstance(request = {}) {
  assertExactObject(request, bindKeys, "raw canonical-import instance binding");
  const state = readOpenBinding(request.binding);
  bindRawInstanceBoundary({ boundary: state.boundary, instance: request.instance });
}

export function closeRawCanonicalImportBinding(request = {}) {
  assertExactObject(request, closeKeys, "raw canonical-import binding close");
  const state = readOpenBinding(request.binding);
  state.closed = true;
  closeRawResourceStore({ store: state.store });
}

function readAdapter(value) {
  if (value === null || typeof value !== "object") {
    throw new TypeError("generated raw adapter namespace is invalid");
  }
  const metadata = readDataProperty(value, "rawAdapterMetadata", "generated raw adapter metadata");
  const create = readDataProperty(value, "createAdapter", "generated raw adapter factory");
  if (typeof create !== "function") {
    throw new TypeError("generated raw adapter factory is unavailable");
  }
  return Object.freeze({ create: create.bind(value), metadata });
}

function readMetadata(value) {
  assertExactObject(value, metadataKeys, "generated raw adapter metadata");
  if (value.abiVersion !== 1 || value.target !== "wasm32" && value.target !== "wasm64"
      || typeof value.witSourceFingerprint !== "string"
      || !/^sha256:[0-9a-f]{64}$/u.test(value.witSourceFingerprint)) {
    throw new TypeError("generated raw adapter metadata is unsupported");
  }
  const identities = readIdentities(value.bindingIdentities);
  const requiredCapabilities = readCapabilities(value.requiredCapabilities);
  return Object.freeze({ identities, requiredCapabilities, target: value.target, value });
}

function readIdentities(value) {
  if (!Array.isArray(value)) throw new TypeError("generated raw binding identities are invalid");
  const seen = new Set();
  return Object.freeze(value.map(identity => {
    assertExactObject(identity, identityKeys, "generated raw binding identity");
    if (typeof identity.module !== "string" || identity.module.length === 0
        || typeof identity.name !== "string" || identity.name.length === 0) {
      throw new TypeError("generated raw binding identity is invalid");
    }
    const key = identityKey(identity.module, identity.name);
    if (seen.has(key)) throw new TypeError("generated raw binding identity is duplicated");
    seen.add(key);
    return Object.freeze({ module: identity.module, name: identity.name });
  }));
}

function readCapabilities(value) {
  if (!Array.isArray(value) || value.length > capabilityOrder.length) {
    throw new TypeError("generated raw adapter capabilities are invalid");
  }
  let previous = -1;
  for (const capability of value) {
    const index = capabilityOrder.indexOf(capability);
    if (index <= previous) throw new TypeError("generated raw adapter capabilities are invalid");
    previous = index;
  }
  return Object.freeze([...value]);
}

function readProduct(value, metadata) {
  assertExactObject(value, productKeys, "generated raw adapter product");
  if (value.metadata !== metadata.value) {
    throw new TypeError("generated raw adapter product metadata does not match");
  }
  assertPlainDataObject(value.imports, "generated raw adapter imports");
  const expected = new Map();
  for (const identity of metadata.identities) {
    let names = expected.get(identity.module);
    if (names === undefined) {
      names = new Set();
      expected.set(identity.module, names);
    }
    names.add(identity.name);
  }
  const modules = Object.keys(value.imports);
  if (modules.length !== expected.size) throw new TypeError("generated raw adapter import modules disagree");
  for (const [moduleName, names] of expected) {
    const module = readDataProperty(value.imports, moduleName, "generated raw adapter import module");
    assertPlainDataObject(module, "generated raw adapter import module");
    const members = Object.keys(module);
    if (members.length !== names.size || members.some(name => !names.has(name))) {
      throw new TypeError("generated raw adapter import members disagree");
    }
    for (const name of names) {
      if (typeof readDataProperty(module, name, "generated raw adapter import") !== "function") {
        throw new TypeError("generated raw adapter import is invalid");
      }
    }
  }
  return value.imports;
}

function readOpenBinding(binding) {
  const state = states.get(binding);
  if (state === undefined) throw new TypeError("raw canonical-import binding is invalid");
  return readOpen(state);
}

function readOpen(state) {
  if (state.closed) throw new TypeError("raw canonical-import binding is closed");
  return state;
}

function identityKey(module, name) {
  return `${module}\u0000${name}`;
}

function readDataProperty(value, name, label) {
  const descriptor = Object.getOwnPropertyDescriptor(value, name);
  if (descriptor === undefined) return undefined;
  if (!("value" in descriptor)) throw new TypeError(`${label} must be a data property`);
  return descriptor.value;
}

function assertPlainDataObject(value, label) {
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || Object.getOwnPropertySymbols(value).length !== 0
      || Object.values(Object.getOwnPropertyDescriptors(value))
        .some(descriptor => !("value" in descriptor))) {
    throw new TypeError(`${label} is invalid`);
  }
}

function assertExactObject(value, keys, label) {
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || Object.getOwnPropertySymbols(value).length !== 0) {
    throw new TypeError(`${label} is invalid`);
  }
  const descriptors = Object.getOwnPropertyDescriptors(value);
  const actualKeys = Object.keys(descriptors).sort();
  if (actualKeys.length !== keys.length
      || actualKeys.some((key, index) => key !== keys[index])
      || Object.values(descriptors).some(descriptor => !descriptor.enumerable
        || !("value" in descriptor))) {
    throw new TypeError(`${label} shape is invalid`);
  }
}
