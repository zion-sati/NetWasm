namespace NetWasm.Wit.Bindings;

internal static class Program
{
    internal static int Main(string[] arguments)
    {
        try
        {
            return WitBindingToolCompositionRoot.Create().Run(arguments);
        }
        catch (Exception exception) when (exception is WitBindingException or
                                          CompilerException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }
}
