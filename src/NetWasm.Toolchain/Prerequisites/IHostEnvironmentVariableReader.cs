namespace NetWasm.Toolchain.Prerequisites;

public interface IHostEnvironmentVariableReader
{
    string? Read(string name);
}
