# NetWasm license policy

NetWasm uses a source-path license boundary. Compiler, linker, optimizer,
build, debugger, IDE, and other developer tooling is covered by the [NetWasm
Community License 1.0](LICENSES/LicenseRef-NetWasm-Community-1.0.txt). CoreLib,
runtime libraries, WIT/WASI libraries, templates, generated support code, and
other code intended to enter user applications are MIT licensed unless a
separate upstream notice applies.

The published licensor is the pseudonym **Zion Sati**. Commercial licensing
questions and agreements: <zionsatidev@gmail.com>. Project repository:
<https://github.com/zion-sati/netwasm>.

| Area | License |
| --- | --- |
| Compiler projects, CLI, SDK/pack, build scripts, debugger, IDE/browser tooling, developer tools, and tests/test infrastructure | NetWasm Community License 1.0 |
| `NetWasm.CoreLib`, runtime libraries, templates, and generated support code | MIT |
| Vendored WASI description files | Upstream WASI license |
| Other third-party material | Its preserved source notice and applicable upstream license |

`LICENSE-MAP.md` contains the mechanically checked path map. Source files with
upstream copyright, license, or provenance headers retain those notices; this
policy does not replace them.

The Community License permits free use for individuals, education, qualifying
open-source work, official contributions, evaluation, and organizations with
fewer than 250 employees and less than USD 10,000,000 in annual revenue. It
also grants an active matching GitHub Sponsors tier a limited internal
commercial-use right for that tier's Developer cap. The designated tiers are
published at <https://github.com/sponsors/zion-sati> and the prices are listed
in the [licensing guide](docs/licensing.md). Use is honor-system; no sign-in,
activation, or telemetry is required.

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
