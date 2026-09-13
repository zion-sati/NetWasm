namespace System.Collections.Generic
{
    // Portions derived from dotnet/runtime System.Private.CoreLib's
    // KeyValuePair helpers at commit 811225a482702af7ecc35d817966bc70b88a3a23.
    public static class KeyValuePair
    {
        public static KeyValuePair<TKey, TValue> Create<TKey, TValue>(TKey key, TValue value) =>
            new(key, value);

        internal static string PairToString(object? key, object? value) =>
            "[" + (key?.ToString() ?? string.Empty) + ", " +
            (value?.ToString() ?? string.Empty) + "]";
    }

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
