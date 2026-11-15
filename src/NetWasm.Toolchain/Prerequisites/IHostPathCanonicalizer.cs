// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Toolchain.Prerequisites;

public interface IHostPathCanonicalizer
{
    string Canonicalize(string path);
}
