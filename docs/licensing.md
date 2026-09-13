# NetWasm licensing guide

This guide is explanatory. If it conflicts with the [NetWasm Community License
1.0](../LICENSES/LicenseRef-NetWasm-Community-1.0.txt), the license controls.

The published licensor is the pseudonym **Zion Sati**. Commercial licensing
questions and agreements can be directed to <zionsatidev@gmail.com>. The
project repository is <https://github.com/zion-sati/netwasm>.

## The model

NetWasm compiler and developer-tooling source is public under the Community
License. Individuals, education, qualifying non-commercial open-source work,
evaluators, and organizations with fewer than 250 employees **and** less than
USD 10,000,000 in annual revenue may use the tooling without charge. Larger
organizations can obtain internal commercial use through an active matching
GitHub Sponsors tier. Any organization that productizes the compiler itself—by
bundling it into an SDK or IDE, redistributing it as a commercial feature, or
exposing compilation as a service—needs a separate OEM or commercial agreement
regardless of size. CoreLib, runtime components, WIT/WASI libraries, generated
support code, and templates are MIT licensed unless an upstream notice says
otherwise. Generated applications may use any license their authors choose.

## License boundary

| Area | License |
| --- | --- |
| Compiler, linker, optimizer, CLI, SDK/pack, build, debugger, IDE/browser, and developer tooling | NetWasm Community License 1.0 |
| CoreLib, runtime libraries, templates, generated support code, and adjacent framework ports | MIT, subject to preserved upstream notices |
| Vendored WASI inputs and other third-party material | Their preserved upstream license |

See the public mirror’s [license map](../LICENSE-MAP.md) for the exact path
assignment. Files with upstream copyright, license, or provenance headers keep
those notices in addition to the path assignment.

## Free-use thresholds

A qualifying small organization is not a Government Entity, has fewer than 250
Employees, and has Annual Revenue below USD 10,000,000, with Employees and
revenue counted across Affiliates. It may use the tooling internally for any
purpose and may sell, license, host, deploy, or distribute unlimited Generated
Output. Other free uses include personal non-commercial work, education and
academic research, qualifying open-source work, official NetWasm contributions,
and a 30-day internal evaluation.

## Sponsors commercial tiers

An organization outside the free-use exceptions may establish the limited
internal commercial-use grant with an active matching GitHub Sponsors tier at
<https://github.com/sponsors/zion-sati>. The tier must cover every Developer
using the tooling for the Organization and its Affiliates.

| Tier | Developer cap | Price (USD/month) |
| --- | ---: | ---: |
| NetWasm Commercial — 5 Developers | 5 | $149 |
| NetWasm Commercial — 25 Developers | 25 | $399 |
| NetWasm Commercial — 100 Developers | 100 | $999 |
| NetWasm Commercial — Unlimited | Unlimited | $2,499 |

A current matching tier or sponsorship receipt is the operative proof. The
grant is honor-system and requires no sign-in, activation, telemetry, or
technical enforcement. It covers internal development, testing, production
builds, and maintenance only, during the active sponsorship period.

Bundling, embedding, installing for third parties, redistribution, resale,
sublicensing, hosted/cloud/API/build access, and other OEM or Commercial
Offering use require a separate written agreement regardless of organization
size, Developer count, or Sponsors tier. Selling or distributing Generated
Output is not itself a Commercial Offering.

The Community License is governed by the laws in force in Victoria, Australia;
the courts of Victoria have non-exclusive jurisdiction. See Section 15 of the
license for the complete governing-law and venue clause.

## Generated Output and notices

The Community License does not claim ownership of inputs or Generated Output,
does not require output to carry the Community License, and does not restrict
the author’s choice of output license. Code intentionally injected into output
that carries a separate MIT or upstream notice remains governed by that notice.
Review [third-party notices](../THIRD-PARTY-NOTICES.md) before redistributing
copied or vendored material.

This repository includes the complete Community License, MIT, BSD, and
applicable upstream license texts, and preserves source-level notices.
