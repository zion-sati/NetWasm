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

Before submitting a documentation change, check the documentation index,
diagnostic coverage, support-status categories, critical target and tooling
terminology, and every changed link in GitHub's rendered Markdown.

For a package-boundary change, also run `eng/build-packages.sh` from a clean
checkout and inspect the resulting package graph before publication. The
command builds the 13 core packages from the audited checked-in runtime pack;
it never regenerates native assets, pushes, or publishes them.
The five native `NetWasm.HostTools.<host RID>` packages are produced and
qualified separately on matching CI hosts, then verified alongside the neutral
packages before publication. The Windows ARM64 `wasm-ld` binary is built once
by [NetWasm.NativeTools](https://github.com/zion-sati/NetWasm.NativeTools) from
pinned LLVM source and published as a versioned release. This repository pins
the exact release archive SHA-256 and verifies its native ARM64 build receipt
before packaging; a local VM is not a package producer. See
`eng/toolchain.json` for pinned inputs and `eng/build-host-tools-package.py`
for the packaging entry point. CI invokes
`eng/qualify-sdk-consumers.sh` with the complete candidate package set on
each supported host.
The Linux producer also stages a SHA-pinned Ubuntu GCC `libatomic.so.1` for the
pinned Node binary and uses pinned `patchelf` to set Node's package-relative
`$ORIGIN` RUNPATH. Its NuGet payload includes the GCC copyright, GPL text and
corresponding-source URLs; the host package verifier checks this closure.

Changes to `src/NetWasm.Runtime`, the runtime-pack policy or regeneration tools,
the BDWGC adaptation, or the pinned native toolchain have a separate maintainer
gate. Regenerate with the pinned Emscripten environment by running
`src/NetWasm.Runtime.Pack/tools/regenerate-runtime-pack.sh --allow-emscripten`,
commit the resulting runtime-pack assets, and let the pull-request CI verify the
canonical Ubuntu x64 result. Ordinary package metadata changes do not enter this
gate.

## Publish a release

Merge the qualified PR to `main` with **Create a merge commit**, which preserves
its signed head commit. Do not squash or rebase it: those methods replace the
signed commit. Verify that the full signed PR-head SHA is an ancestor of
`origin/main` and that its tree matches the source intended for release. Create
and publish the GitHub Release targeting that signed SHA, for example:

```sh
gh release create vVERSION --repo zion-sati/NetWasm \
  --target FULL_SIGNED_PR_HEAD_SHA --notes-file release-notes.md
```

Add `--prerelease` for a preview. The CLI creates the tag and Release together;
no local tag or version edit is needed. After GitHub creates a merge commit,
the Release form's default `main` target names that merge commit, which the
owner-signature verifier does not accept. Select the signed PR-head SHA with
`--target` instead. The published Release tag supplies the package version.

The release workflow verifies that the tag's commit is on `main`, builds the
coordinated package set from that commit, checks its manifest and payloads, and
publishes through trusted NuGet.org publishing. It waits until prerequisite
packages are available from the NuGet.org package feed before publishing
`NetWasm.Sdk`, and waits for the SDK before publishing `NetWasm.Templates`.
Each stage runs under the tagged source's pinned .NET SDK and requests a fresh
temporary credential. A manual workflow retry must name an existing published
Release tag; it reuses an already published package only when its contents
match the release candidate, allowing for NuGet.org's repository signature.

Do not run `eng/prepare-release.py` for this workflow. That older helper writes
versioned source metadata and a tag before the GitHub Release, which conflicts
with the current release-as-source-of-truth flow.
