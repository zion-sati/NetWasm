namespace System.Collections.Generic
{
    // Portions derived from dotnet/runtime System.Private.CoreLib's
    // KeyValuePair helpers at commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
    public static class KeyValuePair
    {
        public static KeyValuePair<TKey, TValue> Create<TKey, TValue>(TKey key, TValue value) =>
            new(key, value);

        internal static string PairToString(object? key, object? value) =>
            "[" + (key?.ToString() ?? string.Empty) + ", " +
            (value?.ToString() ?? string.Empty) + "]";
    }

    [Serializable]
    [System.Runtime.CompilerServices.TypeForwardedFrom("mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
    public readonly struct KeyValuePair<TKey, TValue>
    {
        public KeyValuePair(TKey key, TValue value)
        {
            Key = key;
            Value = value;
        }

        public TKey Key { get; }
        public TValue Value { get; }

        public override string ToString() =>
            KeyValuePair.PairToString(Key, Value);

        public void Deconstruct(out TKey key, out TValue value)
        {
            key = Key;
            value = Value;
        }
    }
}
