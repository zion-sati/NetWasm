// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

// Adapted from dotnet/runtime System.Diagnostics debugger metadata
// declarations. The upstream implementation is licensed under MIT.

using System.Diagnostics.CodeAnalysis;

namespace System.Diagnostics
{
    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Module, AllowMultiple = false)]
    public sealed class DebuggableAttribute : Attribute
    {
        [Flags]
        public enum DebuggingModes
        {
            None = 0x0,
            Default = 0x1,
            DisableOptimizations = 0x100,
            IgnoreSymbolStoreSequencePoints = 0x2,
            EnableEditAndContinue = 0x4
        }

        public DebuggableAttribute(bool isJITTrackingEnabled, bool isJITOptimizerDisabled)
        {
            DebuggingFlags = (isJITTrackingEnabled ? DebuggingModes.Default : DebuggingModes.None) |
                (isJITOptimizerDisabled ? DebuggingModes.DisableOptimizations : DebuggingModes.None);
        }

        public DebuggableAttribute(DebuggingModes modes) => DebuggingFlags = modes;
        public bool IsJITTrackingEnabled
        {
            get => (DebuggingFlags & DebuggingModes.Default) != 0;
        }

        public bool IsJITOptimizerDisabled
        {
            get => (DebuggingFlags & DebuggingModes.DisableOptimizations) != 0;
        }
        public DebuggingModes DebuggingFlags { get; }
    }

    public enum DebuggerBrowsableState
    {
        Never = 0,
        Collapsed = 2,
        RootHidden = 3
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
    public sealed class DebuggerBrowsableAttribute : Attribute
    {
        public DebuggerBrowsableAttribute(DebuggerBrowsableState state)
        {
            if (state < DebuggerBrowsableState.Never || state > DebuggerBrowsableState.RootHidden)
            {
                throw new ArgumentOutOfRangeException(nameof(state));
            }

            State = state;
        }

        public DebuggerBrowsableState State { get; }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class DebuggerDisableUserUnhandledExceptionsAttribute : Attribute
    {
        public DebuggerDisableUserUnhandledExceptionsAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Delegate |
        AttributeTargets.Enum | AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Assembly,
        AllowMultiple = true)]
    public sealed class DebuggerDisplayAttribute : Attribute
    {
        public DebuggerDisplayAttribute(string? value)
        {
            Value = value ?? string.Empty;
            Name = string.Empty;
            Type = string.Empty;
        }

        public string Value { get; }
        public string? Name { get; set; }
        public string? Type { get; set; }
        public Type? Target
        {
            get => null;
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                throw new PlatformNotSupportedException("Debugger assembly-qualified type names are not supported.");
            }
        }
        public string? TargetTypeName { get; set; }
    }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Constructor, Inherited = false)]
    public sealed class DebuggerHiddenAttribute : Attribute
    {
        public DebuggerHiddenAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Property |
        AttributeTargets.Constructor | AttributeTargets.Struct, Inherited = false)]
    public sealed class DebuggerNonUserCodeAttribute : Attribute
    {
        public DebuggerNonUserCodeAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method |
        AttributeTargets.Constructor, Inherited = false)]
    public sealed class DebuggerStepThroughAttribute : Attribute
    {
        public DebuggerStepThroughAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Method, Inherited = false)]
    public sealed class DebuggerStepperBoundaryAttribute : Attribute
    {
        public DebuggerStepperBoundaryAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class DebuggerTypeProxyAttribute : Attribute
    {
        public DebuggerTypeProxyAttribute(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
        {
            ArgumentNullException.ThrowIfNull(type);
            throw new PlatformNotSupportedException("Debugger assembly-qualified type names are not supported.");
        }

        public DebuggerTypeProxyAttribute(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] string typeName) =>
            ProxyTypeName = typeName;

        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
        public string ProxyTypeName { get; }
        public Type? Target
        {
            get => null;
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                throw new PlatformNotSupportedException("Debugger assembly-qualified type names are not supported.");
            }
        }

        public string? TargetTypeName { get; set; }
    }

    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class DebuggerVisualizerAttribute : Attribute
    {
        public DebuggerVisualizerAttribute(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] string visualizerTypeName) =>
            VisualizerTypeName = visualizerTypeName;

        public DebuggerVisualizerAttribute(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] string visualizerTypeName,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] string? visualizerObjectSourceTypeName)
        {
            VisualizerTypeName = visualizerTypeName;
            VisualizerObjectSourceTypeName = visualizerObjectSourceTypeName;
        }

        public DebuggerVisualizerAttribute(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] string visualizerTypeName,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type visualizerObjectSource)
        {
            ArgumentNullException.ThrowIfNull(visualizerObjectSource);
            VisualizerTypeName = visualizerTypeName;
            throw new PlatformNotSupportedException("Debugger assembly-qualified type names are not supported.");
        }

        public DebuggerVisualizerAttribute(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type visualizer)
        {
            ArgumentNullException.ThrowIfNull(visualizer);
            throw new PlatformNotSupportedException("Debugger assembly-qualified type names are not supported.");
        }

        public DebuggerVisualizerAttribute(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type visualizer,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] string? visualizerObjectSourceTypeName)
        {
            ArgumentNullException.ThrowIfNull(visualizer);
            throw new PlatformNotSupportedException("Debugger assembly-qualified type names are not supported.");
        }

        public DebuggerVisualizerAttribute(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type visualizer,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type visualizerObjectSource)
        {
            ArgumentNullException.ThrowIfNull(visualizer);
            ArgumentNullException.ThrowIfNull(visualizerObjectSource);
            throw new PlatformNotSupportedException("Debugger assembly-qualified type names are not supported.");
        }

        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
        public string? VisualizerObjectSourceTypeName { get; }

        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
        public string VisualizerTypeName { get; }
        public string? Description { get; set; }
        public Type? Target
        {
            get => null;
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                throw new PlatformNotSupportedException("Debugger assembly-qualified type names are not supported.");
            }
        }

        public string? TargetTypeName { get; set; }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Constructor | AttributeTargets.Interface |
        AttributeTargets.Method | AttributeTargets.Struct, Inherited = false)]
    public sealed class StackTraceHiddenAttribute : Attribute
    {
        public StackTraceHiddenAttribute() { }
    }
}
