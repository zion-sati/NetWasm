# NetWasm licensing guide

This guide is explanatory. If it conflicts with the [NetWasm Community License
1.0](../LICENSES/LicenseRef-NetWasm-Community-1.0.txt), the license controls.

The published licensor is the pseudonym **Zion Sati**. Commercial licensing
questions and agreements can be directed to <zion.sati@netwasm.com>. The
project repository is <https://github.com/zion-sati/netwasm>.

## The model

NetWasm compiler and developer-tooling source is public under the Community
License. The tooling is free for individuals, education, non-commercial
open-source work, evaluation, and small and medium-sized businesses:
organizations with fewer than 250 employees **and** less than USD 10,000,000 in
annual revenue. Larger organizations pay for internal commercial use by
sponsoring a matching GitHub Sponsors tier; the Unlimited tier covers any number
of developers. Any organization that productizes the
compiler itself, by bundling it into an SDK or IDE, redistributing it as a
commercial feature, or exposing compilation as a service, needs a separate OEM
or commercial agreement regardless of size. CoreLib, runtime components, WIT/WASI libraries, generated
support code, and templates are MIT licensed unless a component or upstream
notice says otherwise. NetWasmGC and CompactGC are Apache-2.0 licensed.
Generated applications may use any license their authors choose, subject to
the notices and terms of included components.

## License boundary

| Area | License |
| --- | --- |
| Compiler, linker, optimizer, CLI, SDK/pack, build, debugger, IDE/browser, and developer tooling, including the benchmark and qualification harnesses | NetWasm Community License 1.0 |
| CoreLib, runtime libraries, templates, generated support code, and adjacent framework ports | MIT, subject to preserved upstream notices |
| NetWasmGC and CompactGC collector implementations | Apache-2.0 |
| Vendored WASI inputs and other third-party material | Their preserved upstream license |

See the public mirror’s [license map](../LICENSE-MAP.md) for the exact path
assignment. Files with upstream copyright, license, or provenance headers keep
those notices in addition to the path assignment.

## Free-use thresholds

Small and medium-sized businesses use NetWasm free: any organization that is
not a Government Entity, has fewer than 250 Employees, and has Annual Revenue
below USD 10,000,000, with Employees and revenue counted across Affiliates. It
may use the tooling internally for any purpose and may sell, license, host,
deploy, or distribute unlimited Generated Output. Personal non-commercial work,
education and academic research, unpaid work on open-source projects under an
OSI-approved license, official NetWasm contributions, and a 30-day internal
evaluation are free too.

## Sponsors commercial tiers

A larger organization or a Government Entity pays for the limited internal
commercial-use grant by sponsoring a matching tier at
<https://github.com/sponsors/zion-sati>. The tier must cover every Developer
using the tooling for the Organization and its Affiliates.

| Tier | Developer cap | Price (USD/month) |
| --- | ---: | ---: |
| NetWasm Commercial — 5 Developers | 5 | $149 |
| NetWasm Commercial — 25 Developers | 25 | $399 |
| NetWasm Commercial — 100 Developers | 100 | $999 |
| NetWasm Commercial — Unlimited | Unlimited | $2,499 |

Your sponsorship is your license. There is no license key, certificate, order
form, or separate agreement to sign: an active matching tier, or a current
sponsorship receipt, is the complete proof. The grant is honor-system and
requires no sign-in, activation, telemetry, or technical enforcement. It covers
internal development, testing, production builds, and maintenance only.

Sponsor continuously for 12 months, and the releases published while you
sponsored stay licensed for your internal use, maintenance, and modification
after the sponsorship ends. If the tiers are ever withdrawn with no
replacement, every active sponsor keeps the releases published up to then,
whatever the length of its sponsorship. The Licensor is a sole trader
registered in Australia, trading as Zion Sati; its legal name, Australian
Business Number, and vendor details are available on request at
<zion.sati@netwasm.com>. Organizations can pay by card or, through GitHub, by
invoice.

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
