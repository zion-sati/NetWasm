# Maintaining the documentation

Document NetWasm-specific behavior, prerequisites and limitations. Show runnable
commands, but do not explain standard .NET behavior when NetWasm does not deviate
from it.

User-facing behaviour and its documentation should change together. When a
change affects any of the following, update the corresponding page under
`docs/` in the same pull request:

- compiler diagnostics;
- support status or known limitations;
- target widths, output formats, or Component Model and WASI boundaries;
- CLI options, build inputs, generated artifacts, or deployment entry points;
- CoreLib and runtime behaviour; or
- WIT, JavaScript interop, the runtime ABI, or host capabilities.

Describe capabilities as available only after their implementation and
qualification are complete. Keep investigation notes and test evidence out of
the user-facing contract pages.

Before submitting a documentation change, run the focused verifier from the
repository root:

```sh
bash docs/verify-docs.sh
```

It checks diagnostic coverage, documentation navigation, support-status
categories, and critical target and tooling terminology.

For a package-boundary change, also run `eng/build-packages.sh` from a clean
checkout and inspect the resulting package graph before publication. The
command builds the 13 core packages from the audited checked-in runtime pack;
it never regenerates native assets, pushes, or publishes them.

Changes to `src/NetWasm.Runtime`, the runtime-pack policy or regeneration tools,
the BDWGC adaptation, or the pinned native toolchain have a separate maintainer
gate. Regenerate with the pinned Emscripten environment by running
`src/NetWasm.Runtime.Pack/tools/regenerate-runtime-pack.sh --allow-emscripten`,
commit the resulting runtime-pack assets, and let the pull-request CI verify the
canonical Ubuntu x64 result. Ordinary package metadata changes do not enter this
gate.

## Prepare a release

From a clean `main` checkout, with the public Git author and an approved signing
key configured, run:

```sh
python3 eng/prepare-release.py --version VERSION
```

Replace `VERSION` with the next semantic version. The command updates coordinated
package versions, creates a signed source commit and tag, and commits the matching
release manifest. It preserves the repository's tag prefix and rejects existing
tags or unapproved author metadata. No manifest editing is needed.

Review its output, push `main` and the printed tag atomically, then publish a GitHub
Release for that tag. Preparation does not build, test, push, or publish; the
release workflow builds and validates the exact tagged source before trusted
NuGet.org publication.
