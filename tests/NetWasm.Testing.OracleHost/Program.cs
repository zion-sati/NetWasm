// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Testing.OracleHost;

return new OracleHostApplication(
    new OracleInputReader(),
    new OracleAssemblyObserver()).Run(args);
