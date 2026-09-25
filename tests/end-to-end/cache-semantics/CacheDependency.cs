namespace NetWasm.Correctness.CacheSemantics;

public static class CacheDependency
{
    public static int Step(int input, int index)
    {
        var result = (index & 1) == 0 ? unchecked(input + index) : unchecked(input - index);
#if EDITED
        return unchecked(result + 5);
#else
        return result;
#endif
    }
}
