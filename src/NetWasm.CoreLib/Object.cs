namespace System
{
    [Serializable]
    public class Object
    {
        public Object()
        {
        }

        ~Object()
        {
        }

        public virtual bool Equals(object? value) => this == value;
        public virtual int GetHashCode() => ObjectIdentityRuntime.GetHashCode(this);
        public virtual string ToString() => GetType().ToString();
        // The compiler lowers every call to GetType, this one included, to a read of the object's
        // type; the body is what a delegate or function pointer to GetType runs.
        public Type GetType() => GetType();
        [Runtime.CompilerServices.MethodImpl(Runtime.CompilerServices.MethodImplOptions.InternalCall)]
        protected extern object MemberwiseClone();
        public static bool Equals(object? left, object? right) =>
            left == null ? right == null : left.Equals(right);
        internal static int GetValueHashCode(object? value) =>
            value?.GetHashCode() ?? 0;
        public static bool ReferenceEquals(object? left, object? right) => left == right;
    }

    public interface IEquatable<T> where T : allows ref struct
    {
        bool Equals(T? other);
    }

    public interface IComparable
    {
        int CompareTo(object? value);
    }

    public interface IComparable<in T> where T : allows ref struct
    {
        int CompareTo(T? other);
    }
}
