using System.Text.Json;

namespace NetWasm.Testing.OracleHost;

internal sealed class OracleHostApplication(
    IOracleInputReader inputReader,
    IOracleAssemblyObserver observer)
{
    private readonly IOracleInputReader _inputReader =
        inputReader ?? throw new ArgumentNullException(nameof(inputReader));
    private readonly IOracleAssemblyObserver _observer =
        observer ?? throw new ArgumentNullException(nameof(observer));

    public int Run(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length is < 5 or > 6 ||
            !bool.TryParse(args[4], out var typedTrace) ||
            _inputReader.Read(args[3]) is not { } request)
        {
            Console.Error.WriteLine(
                "usage: NetWasm.Testing.OracleHost <assembly> <type> <method> <input|@input-json> <typed-trace> [reference]");
            return 2;
        }

        try
        {
            var assemblyPath = Path.GetFullPath(args[0]);
            var referencePath = args.Length == 6 ? Path.GetFullPath(args[5]) : null;
            var observations = new Observation[request.Values.Length];
            for (var index = 0; index < request.Values.Length; index++)
            {
                observations[index] = _observer.Observe(
                    assemblyPath,
                    args[1],
                    args[2],
                    request.Values[index],
                    typedTrace,
                    referencePath);
                Console.Error.WriteLine(
                    $"NETWASM_PROGRESS {index + 1}/{request.Values.Length}");
            }

            Console.Out.Write(request.IsBatched
                ? JsonSerializer.Serialize(observations)
                : JsonSerializer.Serialize(observations[0]));
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }
}
