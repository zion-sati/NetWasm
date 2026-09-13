// Adapted from dotnet/runtime System.Runtime.ConstrainedExecution metadata
// declarations. The upstream implementation is licensed under MIT.

namespace System.Runtime.ConstrainedExecution
{
    public enum Cer : int
    {
        None = 0,
        MayFail = 1,
        Success = 2
    }

    public enum Consistency : int
    {
        MayCorruptProcess = 0,
        MayCorruptAppDomain = 1,
        MayCorruptInstance = 2,
        WillNotCorruptState = 3
    }

    [AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Method, Inherited = false)]
    public sealed class PrePrepareMethodAttribute : Attribute
    {
        public PrePrepareMethodAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Struct |
        AttributeTargets.Constructor | AttributeTargets.Method | AttributeTargets.Interface, Inherited = false)]
    public sealed class ReliabilityContractAttribute : Attribute
    {
        public ReliabilityContractAttribute(Consistency consistencyGuarantee, Cer cer)
        {
            ConsistencyGuarantee = consistencyGuarantee;
            Cer = cer;
        }

        public Consistency ConsistencyGuarantee { get; }
        public Cer Cer { get; }
    }
}
