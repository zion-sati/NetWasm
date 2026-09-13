namespace System
{
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
        public virtual string ToString() => "System.Object";
        public Type GetType() => null!;
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
