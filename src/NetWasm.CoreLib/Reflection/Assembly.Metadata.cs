// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Preserve upstream forwarding/value behavior; unavailable runtime metadata throws PNSE.
using System.Collections.Generic;
using System.Configuration.Assemblies;
using System.Globalization;
using System.IO;
using System.Security;
namespace System.Reflection;

public abstract partial class Assembly
{
    public virtual string? CodeBase => throw MetadataUnavailable();
    public virtual MethodInfo? EntryPoint => throw MetadataUnavailable();
    public virtual string? FullName => throw MetadataUnavailable();
    public virtual string ImageRuntimeVersion => throw MetadataUnavailable();
    public virtual bool IsDynamic => false;
    public virtual string Location => throw MetadataUnavailable();
    public virtual bool ReflectionOnly => throw MetadataUnavailable();
    public bool IsFullyTrusted => true;
    public virtual bool GlobalAssemblyCache => throw MetadataUnavailable();
    public virtual long HostContext => throw MetadataUnavailable();
    public virtual SecurityRuleSet SecurityRuleSet => SecurityRuleSet.None;
    public virtual string EscapedCodeBase => AssemblyName.EscapeCodeBase(CodeBase);
    public override string ToString() => FullName ?? base.ToString()!;
    public static string CreateQualifiedName(string? assemblyName, string? typeName) => typeName + ", " + assemblyName;

    public virtual AssemblyName GetName() => GetName(false);
    public virtual AssemblyName GetName(bool copiedName) => throw MetadataUnavailable();
    public virtual AssemblyName[] GetReferencedAssemblies() => throw MetadataUnavailable();
    public virtual Type? GetType(string name) => GetType(name, false, false);
    public virtual Type? GetType(string name, bool throwOnError) => GetType(name, throwOnError, false);
    public virtual Type? GetType(string name, bool throwOnError, bool ignoreCase) => throw MetadataUnavailable();
    public virtual Type[] GetTypes()
    {
        Module[] m = GetModules(false);
        if (m.Length == 1)
        {
            return m[0].GetTypes();
        }

        int finalLength = 0;
        Type[][] moduleTypes = new Type[m.Length][];

        for (int i = 0; i < moduleTypes.Length; i++)
        {
            moduleTypes[i] = m[i].GetTypes();
            finalLength += moduleTypes[i].Length;
        }

        int current = 0;
        Type[] ret = new Type[finalLength];
        for (int i = 0; i < moduleTypes.Length; i++)
        {
            int length = moduleTypes[i].Length;
            Array.Copy(moduleTypes[i], 0, ret, current, length);
            current += length;
        }

        return ret;
    }
    public virtual Type[] GetExportedTypes() => throw MetadataUnavailable();
    public virtual Type[] GetForwardedTypes() => throw MetadataUnavailable();
    public virtual IEnumerable<Type> ExportedTypes => GetExportedTypes();
    public virtual string[] GetManifestResourceNames() => throw MetadataUnavailable();
    public virtual Stream? GetManifestResourceStream(string name) => throw MetadataUnavailable();
    public virtual Stream? GetManifestResourceStream(Type type, string name) => throw MetadataUnavailable();
    public virtual FileStream? GetFile(string name) => throw MetadataUnavailable();
    public virtual FileStream[] GetFiles() => GetFiles(false);
    public virtual FileStream[] GetFiles(bool getResourceModules) => throw MetadataUnavailable();
    public virtual Assembly GetSatelliteAssembly(CultureInfo culture) => throw MetadataUnavailable();
    public virtual Assembly GetSatelliteAssembly(CultureInfo culture, Version? version) => throw MetadataUnavailable();

    public object? CreateInstance(string typeName) => CreateInstance(typeName, false, BindingFlags.Public | BindingFlags.Instance, null, null, null, null);
    public object? CreateInstance(string typeName, bool ignoreCase) => CreateInstance(typeName, ignoreCase, BindingFlags.Public | BindingFlags.Instance, null, null, null, null);
    public virtual object? CreateInstance(string typeName, bool ignoreCase, BindingFlags bindingAttr, Binder? binder, object[]? args, CultureInfo? culture, object[]? activationAttributes) => throw MetadataUnavailable();

    public static Assembly? GetAssembly(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        throw MetadataUnavailable();
    }
    public static Assembly GetCallingAssembly() => throw MetadataUnavailable();
    public static Assembly GetExecutingAssembly() => throw MetadataUnavailable();
    public static Assembly? GetEntryAssembly() => throw MetadataUnavailable();
    public static Assembly LoadFile(string path) => throw MetadataUnavailable();
    public static Assembly LoadFrom(string assemblyFile) => throw MetadataUnavailable();
    public static Assembly LoadFrom(string assemblyFile, byte[]? hashValue, AssemblyHashAlgorithm hashAlgorithm) => throw MetadataUnavailable();
    public static Assembly? LoadWithPartialName(string partialName) => throw MetadataUnavailable();
    public static Assembly ReflectionOnlyLoad(byte[] rawAssembly) => throw MetadataUnavailable();
    public static Assembly ReflectionOnlyLoad(string assemblyString) => throw MetadataUnavailable();
    public static Assembly ReflectionOnlyLoadFrom(string assemblyFile) => throw MetadataUnavailable();
    public static Assembly UnsafeLoadFrom(string assemblyFile) => throw MetadataUnavailable();

    private static PlatformNotSupportedException MetadataUnavailable() => new(
        "Runtime assembly discovery, loading and activation are not supported on NetWasm.");
}
