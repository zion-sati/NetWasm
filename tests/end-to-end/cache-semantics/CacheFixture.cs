namespace NetWasm.Correctness.CacheSemantics;

public static class EntryPoint
{
    public static int Run(int input)
    {
#if EDITED
        var result = 19;
#else
        var result = 17;
#endif
        for (var index = 0; index < 7; index++)
            result = unchecked(result * 3 + CacheDependency.Step(input, index));
        return result;
    }

}
