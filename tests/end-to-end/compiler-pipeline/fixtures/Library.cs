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
