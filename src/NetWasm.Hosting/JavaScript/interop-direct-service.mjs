import { liftScalar, lowerScalar } from "./interop-scalar-codec.mjs";

// A JSImport whose parameters and result are primitive scalars is a direct
// WebAssembly import: the guest passes its arguments as the call's parameters
// and takes the returned value as its result. Each argument's conversion and
// the result's are chosen when the import is bound, and the call is built for
// its arity, so a call converts, calls the service and returns. A thrown value,
// including a result outside its type's range, unwinds into the guest, which
// raises JSException. `activity.active` counts the imports on the stack, as a
// subscription callback reads it to tell reentrant calls from external ones.
export function createInteropDirectService({ descriptor, service, activity }) {
  if (typeof service !== "function" || activity === null || typeof activity !== "object") {
    throw new TypeError("interop direct-service request is invalid");
  }
  const lifts = descriptor.parameters.map(scalarLift);
  const lower = descriptor.result === "void" ? discard : value => lowerScalar(descriptor.result, value);
  switch (lifts.length) {
    case 0:
      return Object.freeze(() => {
        activity.active++;
        try { return lower(service()); } finally { activity.active--; }
      });
    case 1: {
      const [first] = lifts;
      return Object.freeze(a => {
        activity.active++;
        try { return lower(service(first(a))); } finally { activity.active--; }
      });
    }
    case 2: {
      const [first, second] = lifts;
      return Object.freeze((a, b) => {
        activity.active++;
        try { return lower(service(first(a), second(b))); } finally { activity.active--; }
      });
    }
    case 3: {
      const [first, second, third] = lifts;
      return Object.freeze((a, b, c) => {
        activity.active++;
        try { return lower(service(first(a), second(b), third(c))); } finally { activity.active--; }
      });
    }
    default:
      return Object.freeze((...values) => {
        activity.active++;
        try {
          for (let index = 0; index < values.length; index++) values[index] = lifts[index](values[index]);
          return lower(service(...values));
        } finally { activity.active--; }
      });
  }
}

function discard() {
  return undefined;
}

// The guest's i32 is signed and its i64 a signed bigint; each managed type
// keeps its own range and representation.
function scalarLift(type) {
  switch (type) {
    case "i32": case "f64": case "i64": return identity;
    case "u32": return value => value >>> 0;
    case "f32": return value => value;
    default: return value => liftScalar(type, value);
  }
}

function identity(value) {
  return value;
}
