// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Fixtures.Library
{
    public static class SharedMath
    {
        public static int Adjust(int value)
        {
            return (value * 3) + 2;
        }

        public static int UnusedLibrarySentinel()
        {
            return 987654319;
        }

        public static int ExportedDependency(int value)
        {
            return value + 100;
        }
    }
}
