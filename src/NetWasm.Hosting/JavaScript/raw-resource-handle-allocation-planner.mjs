const maximumHandle = 0xffff_ffff;

export function planRawResourceHandleAllocation(nextHandle) {
  if (nextHandle === null) {
    throw new RangeError("raw resource handle space is exhausted");
  }
  if (!Number.isSafeInteger(nextHandle) || nextHandle <= 0 || nextHandle > maximumHandle) {
    throw new TypeError("raw resource next handle is invalid");
  }
  return Object.freeze({
    handle: nextHandle,
    nextHandle: nextHandle === maximumHandle ? null : nextHandle + 1,
  });
}
