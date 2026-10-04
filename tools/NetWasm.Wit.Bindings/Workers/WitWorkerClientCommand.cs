using System.Collections.Immutable;
using System.Text;

namespace NetWasm.Wit.Bindings.Workers;

public sealed record WitWorkerClientOptions(
    string ContractPath, string OutputPath, string JavaScriptOutputPath,
    string Namespace, string ClassName, string JavaScriptModule, string WorkerClientModule);

public interface IWitWorkerClientOptionsReader
{
    WitWorkerClientOptions Read(IReadOnlyList<string> arguments);
}

public sealed class WitWorkerClientOptionsReader : IWitWorkerClientOptionsReader
{
    private static readonly ImmutableHashSet<string> Names = ImmutableHashSet.Create(StringComparer.Ordinal,
        "--worker-contract", "--output", "--javascript-output", "--namespace", "--class",
        "--javascript-module", "--worker-client-module");

    public WitWorkerClientOptions Read(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < arguments.Count; index += 2)
        {
            var name = arguments[index];
            if (!Names.Contains(name) || index + 1 == arguments.Count
                || string.IsNullOrWhiteSpace(arguments[index + 1]) || !values.TryAdd(name, arguments[index + 1]))
                throw WitBindingException.Invalid("worker client arguments are unknown, duplicated or incomplete");
        }
        if (values.Count != Names.Count) throw WitBindingException.Invalid("worker client requires all documented arguments");
        return new(values["--worker-contract"], values["--output"], values["--javascript-output"],
            values["--namespace"], values["--class"], values["--javascript-module"], values["--worker-client-module"]);
    }
}

internal sealed class WitWorkerClientCommand(
    IWitWorkerClientOptionsReader options,
    IByteFileReader inputs,
    IWitWorkerCSharpClientWriter clients,
    ITextFileWriter outputs) : IWitBindingCommand
{
    private readonly IWitWorkerClientOptionsReader _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly IByteFileReader _inputs = inputs ?? throw new ArgumentNullException(nameof(inputs));
    private readonly IWitWorkerCSharpClientWriter _clients = clients ?? throw new ArgumentNullException(nameof(clients));
    private readonly ITextFileWriter _outputs = outputs ?? throw new ArgumentNullException(nameof(outputs));

    public int Run(string[] arguments)
    {
        var options = _options.Read(arguments);
        try
        {
            var source = _clients.Write(new(_inputs.Read(Path.GetFullPath(options.ContractPath)), options.Namespace,
                options.ClassName, options.JavaScriptModule, options.WorkerClientModule));
            _outputs.Write(Path.GetFullPath(options.OutputPath), Encoding.UTF8.GetString(source.CSharp));
            _outputs.Write(Path.GetFullPath(options.JavaScriptOutputPath), Encoding.UTF8.GetString(source.JavaScript));
            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw WitBindingException.Invalid("worker client generation failed: " + exception.Message);
        }
    }
}

internal static class WitWorkerClientCommandComposition
{
    public static IWitBindingCommand Create() => new WitWorkerClientCommand(new WitWorkerClientOptionsReader(),
        new SystemByteFileReader(), WitWorkerCSharpClientComposition.CreateWriter(), new SystemTextFileWriter());
}
