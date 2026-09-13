using NetWasm.Testing.OracleHost;

return new OracleHostApplication(
    new OracleInputReader(),
    new OracleAssemblyObserver()).Run(args);
