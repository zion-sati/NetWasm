namespace NetWasm.Toolchain.Prerequisites;

public interface IHostToolCompatibilityValidatorResolver
{
    IHostToolCompatibilityValidator Resolve(string toolId);
}
