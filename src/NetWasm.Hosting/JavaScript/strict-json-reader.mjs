import { NetWasmHostError } from "./managed-errors.mjs";

export function parseStrictJson(text) {
  if (typeof text !== "string") {
    throw new NetWasmHostError("JSON input must be text");
  }

  let value;
  try {
    value = JSON.parse(text);
  } catch (cause) {
    throw new NetWasmHostError("invalid JSON document", { cause });
  }

  rejectDuplicateProperties(text);
  return value;
}

function rejectDuplicateProperties(text) {
  let index = 0;

  function skipWhitespace() {
    while (/\s/u.test(text[index])) {
      index++;
    }
  }

  function scanString() {
    const start = index++;
    while (text[index] !== "\"") {
      if (text[index] === "\\") {
        index++;
      }
      index++;
    }
    index++;
    return JSON.parse(text.slice(start, index));
  }

  function scanValue() {
    skipWhitespace();
    if (text[index] === "{") {
      scanObject();
      return;
    }
    if (text[index] === "[") {
      scanArray();
      return;
    }
    if (text[index] === "\"") {
      scanString();
      return;
    }
    while (index < text.length && !/[\s,}\]]/u.test(text[index])) {
      index++;
    }
  }

  function scanObject() {
    index++;
    skipWhitespace();
    if (text[index] === "}") {
      index++;
      return;
    }

    const properties = new Set();
    while (true) {
      const property = scanString();
      if (properties.has(property)) {
        throw new NetWasmHostError(
          `JSON object contains duplicate property '${property}'`);
      }
      properties.add(property);
      skipWhitespace();
      index++;
      scanValue();
      skipWhitespace();
      if (text[index] === "}") {
        index++;
        return;
      }
      index++;
      skipWhitespace();
    }
  }

  function scanArray() {
    index++;
    skipWhitespace();
    if (text[index] === "]") {
      index++;
      return;
    }
    while (true) {
      scanValue();
      skipWhitespace();
      if (text[index] === "]") {
        index++;
        return;
      }
      index++;
    }
  }

  scanValue();
}
