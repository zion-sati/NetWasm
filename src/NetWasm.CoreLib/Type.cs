using System.Reflection;

namespace System
{
    public sealed partial class Type : MemberInfo
    {
        internal int SemanticTypeId = 0;

        private Type()
        {
        }

        public static Type? GetTypeFromHandle(RuntimeTypeHandle handle) => null;
        public static bool operator ==(Type? left, Type? right) =>
            object.ReferenceEquals(left, right);
        public static bool operator !=(Type? left, Type? right) =>
            !object.ReferenceEquals(left, right);
        public override bool Equals(object? value) =>
            value is Type other && this == other;
        public override int GetHashCode() => SemanticTypeId;
    }

    public readonly struct RuntimeTypeHandle
    {
    }

    public readonly struct RuntimeFieldHandle
    {
    }
}
