namespace NetWasm.Testing.VSTest.Configuration;

internal interface INetWasmRunSettingsReader
{
    bool TryRead(string? runsettingsXml, out NetWasmRunConfiguration? configuration);
}
