// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Wit.Example.Strings._1._0._0;

public static partial class StringsExports
{
    public static partial string EchoBack(string value) => StringsImports.Echo(value);
}

public static class StringComponent
{
    public static int Run(int value) => StringsImports.Echo("ready").Length + value;
}
