namespace NetWasm.Wit.Bindings;

internal static class Program
{
    internal static int Main(string[] arguments) =>
        Run(arguments, arguments.Contains("--worker-contract", StringComparer.Ordinal)
            ? Workers.WitWorkerClientCommandComposition.Create
            : WitBindingToolCompositionRoot.Create);

    internal static int Run(
        string[] arguments,
        Func<IWitBindingCommand> createCommand)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(createCommand);
        try
        {
            return createCommand().Run(arguments);
        }
        catch (Exception exception) when (exception is WitBindingException or
                                          CompilerException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }
}
