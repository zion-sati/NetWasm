const factoryKeys = ["digest", "fetch", "manifestUrl"];

export function createBrowserArtifactTransport(options) {
  assertExactDataObject(options, factoryKeys, "browser artifact transport options");
  const { manifestUrl, fetch, digest } = options;
  if (typeof fetch !== "function" || typeof digest !== "function") {
    throw new TypeError("browser artifact transport requires fetch and digest acts");
  }
  const manifest = validateManifestUrl(manifestUrl);

  return Object.freeze({
    async readArtifact(artifact, signal) {
      const response = await fetch(resolveArtifactUrl(manifest, artifact.relativePath), {
        signal: signal ?? undefined,
      });
      if (response === null || typeof response !== "object"
          || typeof response.ok !== "boolean" || typeof response.arrayBuffer !== "function") {
        throw new TypeError("browser artifact fetch returned an invalid response");
      }
      if (!response.ok) {
        throw new Error("browser artifact fetch was unsuccessful");
      }
      const buffer = await response.arrayBuffer();
      if (!(buffer instanceof ArrayBuffer)) {
        throw new TypeError("browser artifact response returned invalid bytes");
      }
      return new Uint8Array(buffer);
    },
    async hashBytes(bytes) {
      const value = await digest("SHA-256", bytes);
      if (!(value instanceof ArrayBuffer) || value.byteLength !== 32) {
        throw new TypeError("browser SHA-256 returned an invalid digest");
      }
      return [...new Uint8Array(value)]
        .map(byte => byte.toString(16).padStart(2, "0"))
        .join("");
    },
  });
}

function validateManifestUrl(value) {
  if (typeof value !== "string" || value.length === 0) {
    throw new TypeError("browser deployment manifest URL is required");
  }
  let url;
  try {
    url = new URL(value);
  } catch {
    throw new TypeError("browser deployment manifest URL is invalid");
  }
  if (url.href !== value || url.protocol !== "https:" && url.protocol !== "http:"
      || url.username.length !== 0 || url.password.length !== 0
      || url.hash.length !== 0 || url.pathname.endsWith("/")) {
    throw new TypeError("browser deployment manifest URL must be canonical absolute HTTP(S)");
  }
  return url;
}

function resolveArtifactUrl(manifest, relativePath) {
  const encodedPath = relativePath.split("/").map(encodeURIComponent).join("/");
  return new URL(encodedPath, manifest).href;
}

function assertExactDataObject(value, keys, label) {
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || Object.getOwnPropertySymbols(value).length !== 0) {
    throw new TypeError(`${label} is invalid`);
  }
  const prototype = Object.getPrototypeOf(value);
  const descriptors = Object.getOwnPropertyDescriptors(value);
  const actualKeys = Object.keys(descriptors).sort();
  if (prototype !== null && prototype !== Object.prototype
      || actualKeys.length !== keys.length
      || actualKeys.some((key, index) => key !== keys[index])
      || Object.values(descriptors).some(descriptor => !descriptor.enumerable || !("value" in descriptor))) {
    throw new TypeError(`${label} shape is invalid`);
  }
}
