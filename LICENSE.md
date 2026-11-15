# NetWasm license policy

NetWasm uses a source-path license boundary. Tooling, meaning the compiler
and the surrounding tools required to create a user's deployable package, is
covered by the [NetWasm Community License
1.0](LICENSES/LicenseRef-NetWasm-Community-1.0.txt), together with its tests
and qualification harnesses. Everything else is MIT licensed unless a separate
component or upstream notice applies, including all code a user needs to
deploy an application: CoreLib, runtime libraries, WIT/WASI libraries, the
JavaScript host and the other JavaScript that ships with an application,
templates, and generated support code. NetWasmGC and CompactGC are licensed
under Apache-2.0 and carry their own licence texts and notices.

The published licensor is the pseudonym **Zion Sati**. Commercial licensing
questions and agreements: <zion.sati@netwasm.com>. Project repository:
<https://github.com/zion-sati/netwasm>.

| Area | License |
| --- | --- |
| Tooling: the compiler, CLI, MSBuild tasks, SDK and packs, toolchain, host tools, build-time bundling, the WIT bindings tool, and their tests, test infrastructure and qualification harnesses | NetWasm Community License 1.0 |
| Everything else: `NetWasm.CoreLib`, runtime libraries, the JavaScript host (`NetWasm.Hosting`), templates, generated support code, the test adapter, NetWasm's build and release scripts, and their tests | MIT |
| NetWasmGC and CompactGC collector implementations | Apache-2.0 |
| Vendored WASI description files | Upstream WASI license |
| Other third-party material | Its preserved source notice and applicable upstream license |

`LICENSE-MAP.md` contains the mechanically checked path map. NetWasm's own
source files begin with a notice naming the copyright holder and the license
the map gives their path. Source files with upstream copyright, license, or
provenance headers retain those notices; this policy does not replace them.

The Community License is free for individuals, education, open-source work,
official contributions, evaluation, and small and medium-sized businesses:
organizations with fewer than 250 employees and less than USD 10,000,000 in
annual revenue. Larger organizations pay for a limited internal commercial-use
right by sponsoring a matching GitHub Sponsors tier, which covers that tier's
Developer cap. The designated tiers are
published at <https://github.com/sponsors/zion-sati> and the prices are listed
in the [licensing guide](docs/licensing.md). The sponsorship itself is the
license and its proof; there is no license key or separate agreement. Use is
honor-system; no sign-in, activation, or telemetry is required.

The Sponsors grant does not permit bundling, embedding, redistribution,
resale, sublicensing, OEM distribution, or hosted/cloud/API/build access to
the tooling. Those Commercial Offering activities require a separate written
agreement, regardless of organization size or tier. Generated Output may be
licensed and distributed under terms chosen by its author.

The Community License is governed by the laws in force in Victoria, Australia;
the courts of Victoria have non-exclusive jurisdiction. See Section 15 of the
complete license text for the full governing-law and venue clause.

This repository intentionally makes no separate copyright-provenance claim for
copied material. Retained upstream notices identify the material to which they
apply.

The complete applicable texts are included locally:

- [NetWasm Community License 1.0](LICENSES/LicenseRef-NetWasm-Community-1.0.txt)
- [MIT](LICENSES/MIT.txt)
- [BSD-2-Clause](LICENSES/BSD-2-Clause.txt)
- [BSD-3-Clause](LICENSES/BSD-3-Clause.txt)

See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) and
[THIRD-PARTY-NOTICES.TXT](THIRD-PARTY-NOTICES.TXT) for the source-attribution
bridge used by copied material.
