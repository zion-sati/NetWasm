// Adapted from dotnet/runtime System.ComponentModel.DescriptionAttribute.
// The upstream implementation is licensed under MIT.

namespace System.ComponentModel
{
    [AttributeUsage(AttributeTargets.All)]
    public class DescriptionAttribute : Attribute
    {
        public static readonly DescriptionAttribute Default = new();

        public DescriptionAttribute() : this(string.Empty)
        {
        }

        public DescriptionAttribute(string description)
        {
            DescriptionValue = description;
        }

        public virtual string Description => DescriptionValue;

        protected string DescriptionValue { get; set; }

        public override bool Equals(object? obj) =>
            obj is DescriptionAttribute other && other.Description == Description;

        public override int GetHashCode() => Description?.GetHashCode() ?? 0;

        public override bool IsDefaultAttribute() => Equals(Default);
    }
}
