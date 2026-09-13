namespace System.Runtime.InteropServices.TimeZones
{
    internal readonly struct EnvironmentVariable
    {
        internal EnvironmentVariable(string name, string value)
        {
            Name = name ?? throw new ArgumentNullException();
            Value = value ?? throw new ArgumentNullException();
        }

        internal string Name { get; }
        internal string Value { get; }
    }
}
