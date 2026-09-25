namespace NetWasm.Correctness.MultiFile;

public static partial class EntryPoint
{
    private static int Right(int input) => new LocalValue().Read(input);
}

file sealed class LocalValue
{
    public int Read(int input) => 19 - input;
}
