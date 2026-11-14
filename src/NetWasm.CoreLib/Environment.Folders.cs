// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce (Environment.cs's GetFolderPath and
// ProcessPath);
// Environment.GetFolderPathCore.Unix.cs resolves the folder, as upstream's WASI build does.

namespace System;

public static partial class Environment
{
    public static string GetFolderPath(SpecialFolder folder) => GetFolderPath(folder, SpecialFolderOption.None);

    public static string GetFolderPath(SpecialFolder folder, SpecialFolderOption option)
    {
        // No need to validate if 'folder' is defined; GetFolderPathCore handles this check.

        if (option is not SpecialFolderOption.None and not SpecialFolderOption.Create and not SpecialFolderOption.DoNotVerify)
        {
            // Use a throw helper so that if 'option' is a constant,
            // the JIT can inline this method and remove the validation check entirely.
            Throw(option);
            static void Throw(SpecialFolderOption option) =>
                throw new ArgumentOutOfRangeException(nameof(option), option, SR.Format(SR.Arg_EnumIllegalVal, option));
        }

        return GetFolderPathCore(folder, option);
    }

    private static string? s_processPath;

    // As upstream's Environment.cs.
    public static string? ProcessPath
    {
        get
        {
            string? processPath = s_processPath;
            if (processPath == null)
            {
                // The value is cached both as a performance optimization and to ensure that the API always returns
                // the same path in a given process.
                Threading.Interlocked.CompareExchange(ref s_processPath, GetProcessPath() ?? "", null);
                processPath = s_processPath;
            }
            return (processPath.Length != 0) ? processPath : null;
        }
    }

    // NetWasm: a WASI program has no executable path.
    private static string? GetProcessPath() => null;
}
