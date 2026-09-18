using System;
using System.Collections.Generic;
using NetWasm.Compiler.Browser.Results;

namespace NetWasm.Compiler.Browser;

/// <summary>A pending immutable browser compilation and its cache partition.</summary>
public sealed record BrowserCompilationPreparation(
    string Handle,
    FrontendArtifactCacheDescriptor? FrontendCache);

/// <summary>A completed prepared compilation and its newly committed cache entries.</summary>
public sealed record BrowserPreparedCompilationResult(
    BrowserCompilationResult Compilation,
    FrontendArtifactCachePublication? FrontendPublication);
