// Adapted from dotnet/runtime System.Diagnostics debugger metadata
// declarations. The upstream implementation is licensed under MIT.

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
        public DebuggerBrowsableAttribute(DebuggerBrowsableState state) => State = state;
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
        private Type? _target;

        public DebuggerDisplayAttribute(string? value)
        {
            Value = value ?? string.Empty;
            Name = string.Empty;
            Type = string.Empty;
        }

        public string Value { get; }
        public string? Name { get; set; }
        public string? Type { get; set; }
        public Type? Target { get => _target; set => _target = value; }
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
        private Type? _target;

        public DebuggerTypeProxyAttribute(Type type) => ProxyTypeName = type?.ToString() ?? string.Empty;
        public DebuggerTypeProxyAttribute(string typeName) => ProxyTypeName = typeName;
        public string ProxyTypeName { get; }
        public Type? Target { get => _target; set => _target = value; }
        public string? TargetTypeName { get; set; }
    }

    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class DebuggerVisualizerAttribute : Attribute
    {
        private Type? _target;

        public DebuggerVisualizerAttribute(string visualizerTypeName) => VisualizerTypeName = visualizerTypeName;

        public DebuggerVisualizerAttribute(string visualizerTypeName, string? visualizerObjectSourceTypeName)
        {
            VisualizerTypeName = visualizerTypeName;
            VisualizerObjectSourceTypeName = visualizerObjectSourceTypeName;
        }

        public DebuggerVisualizerAttribute(string visualizerTypeName, Type visualizerObjectSource)
        {
            VisualizerTypeName = visualizerTypeName;
            VisualizerObjectSourceTypeName = visualizerObjectSource?.ToString();
        }

        public DebuggerVisualizerAttribute(Type visualizer) => VisualizerTypeName = visualizer?.ToString() ?? string.Empty;

        public DebuggerVisualizerAttribute(Type visualizer, string? visualizerObjectSourceTypeName)
        {
            VisualizerTypeName = visualizer?.ToString() ?? string.Empty;
            VisualizerObjectSourceTypeName = visualizerObjectSourceTypeName;
        }

        public DebuggerVisualizerAttribute(Type visualizer, Type visualizerObjectSource)
        {
            VisualizerTypeName = visualizer?.ToString() ?? string.Empty;
            VisualizerObjectSourceTypeName = visualizerObjectSource?.ToString();
        }

        public string? VisualizerObjectSourceTypeName { get; }
        public string VisualizerTypeName { get; }
        public string? Description { get; set; }
        public Type? Target { get => _target; set => _target = value; }
        public string? TargetTypeName { get; set; }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Constructor | AttributeTargets.Interface |
        AttributeTargets.Method | AttributeTargets.Struct, Inherited = false)]
    public sealed class StackTraceHiddenAttribute : Attribute
    {
        public StackTraceHiddenAttribute() { }
    }
}
