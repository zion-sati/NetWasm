namespace NetWasm.Wit.Example.Strings._1._0._0;

public static partial class StringsExports
{
    public static partial string EchoBack(string value) => StringsImports.Echo(value);
}

public static class StringComponent
{
    public static int Run(int value) => StringsImports.Echo("ready").Length + value;
}
