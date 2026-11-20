# Security

This page covers what a NetWasm program can reach, how a browser deployment
checks what it loads, and the Content Security Policy a page needs.

NetWasm is pre-1.0 and has had no independent security audit. A
security-sensitive deployment needs its own review of the exact NetWasm
version, host and capabilities it uses. Report a vulnerability privately as
[SECURITY.md](../SECURITY.md) describes, not in a public issue.

## What a program can reach

A program reaches only what its host grants: the directories mounted into its
sandbox, and the network, clocks and other WASI capabilities the host
supplies. It cannot open other files, start processes or read machine and user
information; see [host capabilities](host-capabilities.md). A program imports
only the capabilities its reachable code uses, so a component's imports list
everything it can ask its host for.

In a browser, a program reaches the page only through the JavaScript modules it
declares with `[JSImport]` and `NetWasmJavaScriptModule` items, and through
NetWasm's own host. There is no implicit DOM, storage or global module
registry. Its `HttpClient` requests go through `fetch`, under the browser's
usual same-origin and CORS rules.

## What startup checks

A browser deployment's manifest, `deployment.json`, records the SHA-256 digest
of every file the program loads, and the page module carries the manifest's own
digest. Startup checks each file against its digest before using it, so a
modified or mismatched file stops startup instead of running. In a secure
context (HTTPS, or `localhost`) it hashes with `crypto.subtle`; on a page served
over plain HTTP it hashes in JavaScript, with the same result.

JavaScript modules are imported from the bytes that were checked: startup
fetches each module, checks it, and imports it from a `blob:` URL that holds
those bytes. Release builds trust the manifest's shape, which the build
validated; set `NetWasmValidateDeploymentManifest` to `true` to check it again
at startup, as Debug builds do.

## Content Security Policy

A page with a Content Security Policy must allow module scripts from the site,
modules from `blob:` URLs (the checked modules above) and WebAssembly
compilation:

```text
Content-Security-Policy: default-src 'self'; script-src 'self' blob: 'wasm-unsafe-eval'
```

With this policy a page starts in Chrome, Firefox and Safari; without `blob:`
or `'wasm-unsafe-eval'` it stops at startup with a policy violation in each.
The runtime uses no `eval`, `new Function` or `innerHTML`. If the policy also
restricts `connect-src` (or `default-src`), allow the site itself, from which
startup fetches the program and its manifest, and every origin the program's
`HttpClient` calls.

## Cross-origin isolation

A deployment needs no `Cross-Origin-Opener-Policy` or
`Cross-Origin-Embedder-Policy` headers: NetWasm uses no `SharedArrayBuffer` and
no threads.
