using System;
using NetWasm.Compiler.Browser.Inputs;
using NetWasm.Compiler.Browser.Results;
using NetWasm.Compiler.Wasm.Units;

namespace NetWasm.Compiler.Browser;

/// <summary>
/// Reuses one compiler across sequential browser compilations. Instances are not
/// thread-safe. An O0 compilation whose options turn on compiled units
/// (<see cref="CompilerOptions.CompiledUnits"/>) keeps each method's code in the session
/// under its <see cref="CompilerOptions.IntermediateOutputPath"/>, which names the units
/// rather than a directory, and the next compilation under that name reuses what still
/// holds. Export the units to keep them beyond the session (for example in IndexedDB) and
/// import them into a later one.
/// </summary>
public sealed class BrowserCompilerSession : IDisposable
{
    private readonly UnitStoreMemory _units;
    private readonly IDisposable _services;
    private readonly IBrowserCompilationRequestFactory _requests;
    private readonly IBrowserCompilationPreparationFactory _preparations;
    private readonly IBrowserCompilationCommand _compiler;
    private bool _disposed;

    public BrowserCompilerSession() : this(BrowserCompilerCompositionRoot.Create())
    {
    }

    private BrowserCompilerSession(BrowserCompilerComposition composition) : this(
        composition.Services, composition.Requests, composition.Preparations,
        composition.Compiler, composition.Units)
    {
    }

    internal BrowserCompilerSession(
        IDisposable services,
        IBrowserCompilationRequestFactory requests,
        IBrowserCompilationPreparationFactory preparations,
        IBrowserCompilationCommand compiler,
        UnitStoreMemory? units = null)
    {
        _units = units ?? new UnitStoreMemory();
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _requests = requests ?? throw new ArgumentNullException(nameof(requests));
        _preparations = preparations ?? throw new ArgumentNullException(nameof(preparations));
        _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
    }

    public BrowserCompilationResult Compile(BrowserCompilationRequest request)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        using var active = _requests.Begin(request);
        return _compiler.Compile(_preparations.Prepare(request));
    }

    // The compiled units kept under a name, as one array a host can store.
    public byte[] ExportCompiledUnits(string name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(name);
        return _units.Export(name);
    }

    // Replaces the compiled units kept under a name with exported ones; units of another
    // compiler version are not reused.
    public void ImportCompiledUnits(string name, byte[] units)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(units);
        _units.Import(name, units);
    }

    // A plain account of what the last compilation under a name reused, or null.
    public string? CompiledUnitSummary(string name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(name);
        return _units.Summary(name);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _services.Dispose();
    }
}
