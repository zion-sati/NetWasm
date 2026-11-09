// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using System.Collections.Generic;
namespace System.Reflection;

public abstract partial class Assembly
{
    public virtual Module ManifestModule => throw MetadataUnavailable();
    public virtual Module? GetModule(string name) => throw MetadataUnavailable();
    public Module[] GetModules() => GetModules(false);
    public virtual Module[] GetModules(bool getResourceModules) => throw MetadataUnavailable();
    public virtual IEnumerable<Module> Modules => GetLoadedModules(true);
    public Module[] GetLoadedModules() => GetLoadedModules(false);
    public virtual Module[] GetLoadedModules(bool getResourceModules) => throw MetadataUnavailable();
    public Module LoadModule(string moduleName, byte[] rawModule) => LoadModule(moduleName, rawModule, null);
    public virtual Module LoadModule(string moduleName, byte[] rawModule, byte[]? rawSymbolStore) => throw MetadataUnavailable();
    public virtual event ModuleResolveEventHandler? ModuleResolve
    {
        add => throw MetadataUnavailable();
        remove => throw MetadataUnavailable();
    }
}
