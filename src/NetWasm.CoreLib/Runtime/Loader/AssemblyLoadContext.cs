// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;

namespace System.Runtime.Loader
{
    // The public surface of .NET's AssemblyLoadContext over a NetWasm program, whose assemblies are all compiled into it.
    // The default context holds them and resolves a name as Assembly.Load does; a context a program creates resolves
    // through its own Load first and then as the default does. Nothing is loaded at run time: a path, a stream, a native
    // image or a native library throws PlatformNotSupportedException, as Assembly.LoadFrom does. A library such as
    // Microsoft.Testing.Platform, which looks for optional extensions through a load context, runs without them.
    public partial class AssemblyLoadContext
    {
        private const string RuntimeLoading = "Loading assemblies or native libraries at run time is not supported: " +
            "a NetWasm program's assemblies and native code are linked when it is compiled.";

        private static readonly List<AssemblyLoadContext> s_all = [];
        private static AsyncLocal<AssemblyLoadContext?>? s_contextualReflection;
        private static Func<Assembly, string, string>? s_locationOverride;

        private readonly bool _isCollectible;
        private readonly string? _name;
        private bool _unloaded;

        protected AssemblyLoadContext() : this(name: null, isCollectible: false)
        {
        }

        protected AssemblyLoadContext(bool isCollectible) : this(name: null, isCollectible)
        {
        }

        public AssemblyLoadContext(string? name, bool isCollectible = false)
        {
            _name = name;
            _isCollectible = isCollectible;
            lock (s_all)
            {
                s_all.Add(this);
            }
        }

        // NetWasm loads no native library at run time, so nothing raises it.
#pragma warning disable CS0067
        public event Func<Assembly, string, IntPtr>? ResolvingUnmanagedDll;
#pragma warning restore CS0067

        public event Func<AssemblyLoadContext, AssemblyName, Assembly?>? Resolving;

        public event Action<AssemblyLoadContext>? Unloading;

        public static AssemblyLoadContext Default => DefaultAssemblyLoadContext.Instance;

        public static IEnumerable<AssemblyLoadContext> All
        {
            get
            {
                _ = Default;
                lock (s_all)
                {
                    return [.. s_all];
                }
            }
        }

        public static AssemblyLoadContext? CurrentContextualReflectionContext => s_contextualReflection?.Value;

        // The program's assemblies, all in the default context; a context the program creates loads none of its own.
        public IEnumerable<Assembly> Assemblies => ReferenceEquals(this, Default) ? AppDomain.CurrentDomain.GetAssemblies() : [];

        public bool IsCollectible => _isCollectible;

        public string? Name => _name;

        public override string ToString() => $"\"{Name}\" {GetType()}";

        public static AssemblyName GetAssemblyName(string assemblyPath)
        {
            ArgumentNullException.ThrowIfNull(assemblyPath);
            throw new PlatformNotSupportedException(RuntimeLoading);
        }

        // Every assembly is in the default context.
        public static AssemblyLoadContext? GetLoadContext(Assembly assembly)
        {
            ArgumentNullException.ThrowIfNull(assembly);
            return Default;
        }

        public static void SetAssemblyLocationOverride(Func<Assembly, string, string> locationOverride)
        {
            ArgumentNullException.ThrowIfNull(locationOverride);
            if (Interlocked.CompareExchange(ref s_locationOverride, locationOverride, null) is not null)
            {
                throw new InvalidOperationException("The assembly location override was already set.");
            }
        }

        protected virtual Assembly? Load(AssemblyName assemblyName) => null;

        public Assembly LoadFromAssemblyName(AssemblyName assemblyName)
        {
            ArgumentNullException.ThrowIfNull(assemblyName);
            if (Load(assemblyName) is { } loaded)
            {
                return loaded;
            }
            try
            {
                return Assembly.Load(assemblyName);
            }
            catch (FileNotFoundException) when (Resolve(assemblyName) is { } resolved)
            {
                return resolved;
            }
        }

        public Assembly LoadFromAssemblyPath(string assemblyPath)
        {
            ArgumentNullException.ThrowIfNull(assemblyPath);
            throw new PlatformNotSupportedException(RuntimeLoading);
        }

        public Assembly LoadFromNativeImagePath(string nativeImagePath, string? assemblyPath)
        {
            ArgumentNullException.ThrowIfNull(nativeImagePath);
            throw new PlatformNotSupportedException(RuntimeLoading);
        }

        public Assembly LoadFromStream(Stream assembly) => LoadFromStream(assembly, assemblySymbols: null);

        public Assembly LoadFromStream(Stream assembly, Stream? assemblySymbols)
        {
            ArgumentNullException.ThrowIfNull(assembly);
            throw new PlatformNotSupportedException(RuntimeLoading);
        }

        protected IntPtr LoadUnmanagedDllFromPath(string unmanagedDllPath)
        {
            ArgumentNullException.ThrowIfNull(unmanagedDllPath);
            throw new PlatformNotSupportedException(RuntimeLoading);
        }

        protected virtual IntPtr LoadUnmanagedDll(string unmanagedDllName) => IntPtr.Zero;

        // As .NET's: only a collectible context unloads, once, telling its Unloading handlers.
        public void Unload()
        {
            if (!_isCollectible)
            {
                throw new InvalidOperationException("Cannot unload a non-collectible AssemblyLoadContext.");
            }
            if (_unloaded)
            {
                return;
            }
            _unloaded = true;
            Unloading?.Invoke(this);
            lock (s_all)
            {
                s_all.Remove(this);
            }
        }

        public ContextualReflectionScope EnterContextualReflection() => new(this);

        public static ContextualReflectionScope EnterContextualReflection(Assembly? activating) =>
            activating is null ? new(null) : Default.EnterContextualReflection();

        private Assembly? Resolve(AssemblyName assemblyName)
        {
            if (Resolving is not { } resolving)
            {
                return null;
            }
            foreach (Func<AssemblyLoadContext, AssemblyName, Assembly?> handler in resolving.GetInvocationList())
            {
                if (handler(this, assemblyName) is { } resolved)
                {
                    return resolved;
                }
            }
            return null;
        }

        private sealed class DefaultAssemblyLoadContext : AssemblyLoadContext
        {
            internal static readonly AssemblyLoadContext Instance = new DefaultAssemblyLoadContext();

            private DefaultAssemblyLoadContext() : base("Default", isCollectible: false)
            {
            }
        }

        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public struct ContextualReflectionScope : IDisposable
        {
            private readonly AssemblyLoadContext? _predecessor;
            private readonly bool _initialized;

            internal ContextualReflectionScope(AssemblyLoadContext? activating)
            {
                var current = Interlocked.CompareExchange(ref s_contextualReflection, new AsyncLocal<AssemblyLoadContext?>(), null) ??
                    s_contextualReflection;
                _predecessor = current.Value;
                current.Value = activating;
                _initialized = true;
            }

            public void Dispose()
            {
                if (_initialized)
                {
                    s_contextualReflection!.Value = _predecessor;
                }
            }
        }
    }
}
