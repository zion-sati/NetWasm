namespace NetWasm.TimeZones;

internal static class Program
{
    internal static int Main(string[] arguments) => Create().Run(
        arguments,
        Console.Out,
        Console.Error);

    internal static ITimeZoneToolApplication Create() => new TimeZoneToolApplication(
        new TimeZoneCommandLineParser(),
        new TimeZoneGenerationCommand(
            new TimeZoneCatalogReader(
                new IanaVersionReader(),
                new TzifParser(
                    new PosixFutureRuleExpander(new PosixRuleParser()))),
            new TimeZoneSelectionSelector(),
            new TimeZoneAssetEncoder(),
            new BrotliAssetCompressor(),
            new TimeZoneManifestEncoder(),
            new TimeZoneBrowserLoaderEncoder(),
            new ArtifactWriter()));
}
