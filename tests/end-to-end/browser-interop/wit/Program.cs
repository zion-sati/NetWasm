// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;

namespace NetWasm.Wit.Netwasm.Worker.Template._1._0._0;

public static partial class ApplicationExports
{
    public static partial uint Run(uint total)
    {
        var survivor = new byte[4096];
        survivor[0] = (byte)total;
        survivor[^1] = (byte)(total + 1);
        var collections = GC.CollectionCount(0);
        GC.Collect();
        if (GC.CollectionCount(0) <= collections || survivor[0] != (byte)total
            || survivor[^1] != (byte)(total + 1))
        {
            throw new InvalidOperationException("WIT worker GC survivor contract failed.");
        }

        for (var completed = 0u; completed < total;)
        {
            completed++;
            NotificationsImports.Report(completed, total);
        }
        return total;
    }
}
