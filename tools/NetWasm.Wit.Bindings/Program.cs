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
        if (WitBindingUsage.IsRequested(arguments))
        {
            Console.Out.Write(WitBindingUsage.Text);
            return 0;
        }
        if (arguments.Length == 0)
        {
            Console.Error.Write(WitBindingUsage.Text);
            return 1;
        }
        try
        {
            return createCommand().Run(arguments);
        }
        catch (Exception exception) when (exception is WitBindingException or
                                          CompilerException)
        {
            Console.Error.WriteLine(exception.Message);
            if (exception is WitBindingException { IsUsage: true })
            {
                Console.Error.WriteLine("Run 'netwasm-wit-bindgen --help' for usage.");
            }
            return 1;
        }
    }
}
