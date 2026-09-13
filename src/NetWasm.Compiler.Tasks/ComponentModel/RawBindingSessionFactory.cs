using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Raw;

namespace NetWasm.Compiler.Tasks.ComponentModel;

internal sealed class RawBindingSessionFactory(
    Func<ExternalToolCommand, IRawBindingSession> create) :
    IRawBindingSessionFactory
{
    private readonly Func<ExternalToolCommand, IRawBindingSession> _create = create ??
        throw new ArgumentNullException(nameof(create));

    public IRawBindingSession Create(ExternalToolCommand wasmToolsCommand) =>
        _create(wasmToolsCommand);
}

internal sealed class RawBindingSession(
    ServiceProvider services,
    IRawBuildImportSourceValidator validator,
    IRawAdapterWriter adapters,
    IRawDeploymentFunctionProjector functions) : IRawBindingSession
{
    private readonly IRawBuildImportSourceValidator _validator = validator ??
        throw new ArgumentNullException(nameof(validator));
    private readonly IRawAdapterWriter _adapters = adapters ??
        throw new ArgumentNullException(nameof(adapters));
    private readonly IRawDeploymentFunctionProjector _functions = functions ??
        throw new ArgumentNullException(nameof(functions));

    public RawBindingBuildResult Build(RawBuildImportSourceValidationRequest request)
    {
        var plan = _validator.Validate(request);
        var projection = _functions.Project(
            plan,
            request.RuntimeWitPath,
            request.RuntimeWorld);
        return new(
            _adapters.WriteDeployment(new(projection.AdapterPlans)),
            projection.RequiredImports);
    }

    public void Dispose() => services.Dispose();
}
