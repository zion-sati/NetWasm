// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

// A page's generated module starts fetching the deployment's startup files while the runtime's modules load, so
// they download in the same round trip. This fetch hands each of those responses out once, to the first request for
// its absolute URL, and fetches everything else itself. The loaders verify every file's digest as before.
// A request with an abort signal fetches itself, so aborting it behaves as it would without the prefetch.
export function createPrefetchedFetch(options) {
  if (options === null || typeof options !== "object" || typeof options.fetch !== "function") {
    throw new TypeError("prefetched fetch requires a fetch action");
  }
  const { fetch, responses } = options;
  if (!(responses instanceof Map)) {
    throw new TypeError("prefetched responses must be a Map");
  }
  for (const [url, response] of responses) {
    if (!isCanonicalUrl(url)) {
      throw new TypeError("prefetched response URLs must be canonical absolute URLs");
    }
    if (response === null || typeof response !== "object" || typeof response.then !== "function") {
      throw new TypeError(`prefetched response for '${url}' must be a promise`);
    }
  }
  return function prefetchedFetch(input, init) {
    if (typeof input === "string" && init?.signal == null && responses.has(input)) {
      const response = responses.get(input);
      responses.delete(input);
      return response;
    }
    return fetch(input, init);
  };
}

function isCanonicalUrl(value) {
  if (typeof value !== "string") return false;
  try {
    return new URL(value).href === value;
  } catch {
    return false;
  }
}
