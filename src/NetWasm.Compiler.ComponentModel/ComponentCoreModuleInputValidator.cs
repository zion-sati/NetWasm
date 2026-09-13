using System;

namespace NetWasm.Compiler.ComponentModel;

public interface IComponentCoreModuleInputValidator
{
    void Validate(ComponentCoreModuleLinkRequest request);
}

public sealed class ComponentCoreModuleInputValidator(IFileExistence files) :
    IComponentCoreModuleInputValidator
{
    private readonly IFileExistence _files = files ??
        throw new ArgumentNullException(nameof(files));

    public void Validate(ComponentCoreModuleLinkRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireFile(request.ApplicationModulePath, "application core module");
        RequireFile(request.RuntimeModulePath, "runtime core module");
        ArgumentNullException.ThrowIfNull(request.Target);
    }

    private void RequireFile(string path, string description)
    {
        if (!_files.Exists(path))
        {
            throw ComponentException.Invalid($"{description} '{path}' does not exist");
        }
    }
}
