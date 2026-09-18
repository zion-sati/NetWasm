using System;
using System.Collections.Generic;
using NetWasm.Compiler.Browser.Inputs;
using NetWasm.Compiler.Browser.Results;

namespace NetWasm.Compiler.Browser;

/// <summary>
/// Reuses one compiler and its in-memory frontend artifacts across sequential
/// browser compilations. Instances are not thread-safe.
/// </summary>
public sealed class BrowserCompilerSession : IDisposable
{
    private readonly IDisposable _services;
    private readonly IBrowserCompilationRequestFactory _requests;
    private readonly IBrowserCompilationPreparationFactory _preparations;
    private readonly IFrontendArtifactCacheTransport _frontendArtifacts;
    private readonly IBrowserCompilationCommand _compiler;
    private PendingCompilation? _pending;
    private FrontendArtifactCachePublication? _publication;
    private bool _disposed;

    public BrowserCompilerSession() : this(BrowserCompilerCompositionRoot.Create())
    {
    }

    private BrowserCompilerSession(BrowserCompilerComposition composition) : this(
        composition.Services, composition.Requests, composition.Preparations,
        composition.FrontendArtifacts, composition.Compiler)
    {
    }

    internal BrowserCompilerSession(
        IDisposable services,
        IBrowserCompilationRequestFactory requests,
        IBrowserCompilationPreparationFactory preparations,
        IFrontendArtifactCacheTransport frontendArtifacts,
        IBrowserCompilationCommand compiler)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _requests = requests ?? throw new ArgumentNullException(nameof(requests));
        _preparations = preparations ?? throw new ArgumentNullException(nameof(preparations));
        _frontendArtifacts = frontendArtifacts ??
            throw new ArgumentNullException(nameof(frontendArtifacts));
        _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
    }

    public BrowserCompilationResult Compile(BrowserCompilationRequest request)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        if (_pending is not null || _publication is not null)
            throw new InvalidOperationException(
                "Complete, publish or cancel the previous compilation first.");
        using var active = _requests.Begin(request);
        return _compiler.Compile(_preparations.Prepare(request));
    }

    public BrowserCompilationPreparation Prepare(BrowserCompilationRequest request)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        if (_pending is not null || _publication is not null)
            throw new InvalidOperationException(
                "Complete, publish or cancel the previous compilation first.");
        using var active = _requests.Begin(request);
        var prepared = _preparations.Prepare(request);
        var options = prepared.Options with { EnableFrontendCache = true };
        prepared = prepared with { Options = options };
        var descriptor = _frontendArtifacts.Prepare(options);
        var handle = Guid.NewGuid().ToString("N");
        _pending = new(handle, prepared, descriptor);
        return new(handle, descriptor);
    }

    public BrowserPreparedCompilationResult CompilePrepared(
        string handle,
        IReadOnlyList<FrontendArtifactCacheEntry> frontendArtifacts)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(handle);
        ArgumentNullException.ThrowIfNull(frontendArtifacts);
        var pending = _pending;
        if (pending is null || !string.Equals(pending.Handle, handle, StringComparison.Ordinal))
            throw new InvalidOperationException("The prepared compilation handle is stale or invalid.");
        IDisposable? cache = null;
        try
        {
            cache = pending.Descriptor is null ? null :
                _frontendArtifacts.BeginCompilation(pending.Descriptor, frontendArtifacts);
            using var active = _requests.Begin(pending.Compilation.Request);
            var result = _compiler.Compile(pending.Compilation);
            FrontendArtifactCachePublication? published = null;
            if (pending.Descriptor is not null)
            {
                try
                {
                    published = _frontendArtifacts.CompleteCompilation(pending.Descriptor);
                    _publication = published;
                }
                catch (Exception exception) when (exception is InvalidOperationException or
                    ArgumentException or System.IO.IOException or
                    System.Security.Cryptography.CryptographicException)
                {
                    published = null;
                }
            }
            return new(result, published);
        }
        finally
        {
            try
            {
                if (cache is not null) cache.Dispose();
                else if (pending.Descriptor is not null)
                {
                    try
                    {
                        _frontendArtifacts.CancelPreparation(pending.Descriptor);
                    }
                    catch (InvalidOperationException)
                    {
                        // BeginCompilation may have consumed the preparation before failing.
                    }
                }
            }
            finally
            {
                _pending = null;
            }
        }
    }

    public void CancelPrepared(string handle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(handle);
        if (_pending is null || !string.Equals(_pending.Handle, handle, StringComparison.Ordinal))
            throw new InvalidOperationException("The prepared compilation handle is stale or invalid.");
        if (_pending.Descriptor is not null)
            _frontendArtifacts.CancelPreparation(_pending.Descriptor);
        _pending = null;
    }

    public FrontendArtifactCacheBatch ReadFrontendArtifactBatch(
        FrontendArtifactCachePublication publication)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        RequirePublication(publication);
        return _frontendArtifacts.ReadBatch(publication);
    }

    public void AcknowledgeFrontendArtifactBatch(
        FrontendArtifactCachePublication publication,
        FrontendArtifactCacheBatch batch)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        RequirePublication(publication);
        _frontendArtifacts.AcknowledgeBatch(publication, batch);
        if (batch.IsFinal) _publication = null;
    }

    public void AbandonFrontendArtifactPublication(
        FrontendArtifactCachePublication publication)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        RequirePublication(publication);
        _frontendArtifacts.Abandon(publication);
        _publication = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        var pending = _pending;
        var publication = _publication;
        _pending = null;
        _publication = null;
        try
        {
            try
            {
                if (pending?.Descriptor is not null)
                    _frontendArtifacts.CancelPreparation(pending.Descriptor);
            }
            finally
            {
                try
                {
                    if (publication is not null)
                        _frontendArtifacts.Abandon(publication);
                }
                finally
                {
                    _services.Dispose();
                }
            }
        }
        finally
        {
            _disposed = true;
        }
    }

    private sealed record PendingCompilation(
        string Handle,
        PreparedBrowserCompilation Compilation,
        FrontendArtifactCacheDescriptor? Descriptor);

    private void RequirePublication(FrontendArtifactCachePublication publication)
    {
        ArgumentNullException.ThrowIfNull(publication);
        if (_publication is null || !string.Equals(_publication.Token,
                publication.Token, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "The frontend artifact publication is stale or invalid.");
    }
}
