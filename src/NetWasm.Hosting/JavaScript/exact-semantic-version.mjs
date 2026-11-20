// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

// The version of a versioned WIT identity such as wasi:http/types@0.3.2, as a regular expression source to compose:
// one exact semantic version, MAJOR.MINOR.PATCH without leading zeros, with the prerelease and build metadata a WIT
// package version may carry (wasi:tls@0.3.0-draft). NetWasm.Hosting's C# checks the same grammar.
const number = "(?:0|[1-9][0-9]*)";
const identifiers = "[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*";

export const exactSemanticVersion = `${number}\\.${number}\\.${number}(?:-${identifiers})?(?:\\+${identifiers})?`;
