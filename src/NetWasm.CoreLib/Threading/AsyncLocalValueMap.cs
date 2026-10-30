// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime System.Private.CoreLib at commit
// 474ed963699fc29809015392738a15231aaae2de.

using System.Collections.Generic;

namespace System.Threading
{
    internal interface IAsyncLocalValueMap
    {
        bool TryGetValue(IAsyncLocal key, out object? value);

        IAsyncLocalValueMap Set(
            IAsyncLocal key,
            object? value,
            bool treatNullValueAsNonexistent);
    }

    internal static class AsyncLocalValueMap
    {
        internal static IAsyncLocalValueMap Empty { get; } =
            new EmptyAsyncLocalValueMap();

        internal static bool IsEmpty(IAsyncLocalValueMap map) =>
            ReferenceEquals(map, Empty);

        internal static IAsyncLocalValueMap Create(
            IAsyncLocal key,
            object? value,
            bool treatNullValueAsNonexistent) =>
            value is not null || !treatNullValueAsNonexistent
                ? new OneElementAsyncLocalValueMap(Pair(key, value))
                : Empty;

        private static KeyValuePair<IAsyncLocal, object?> Pair(
            IAsyncLocal key,
            object? value) => new(key, value);

        private sealed class EmptyAsyncLocalValueMap : IAsyncLocalValueMap
        {
            public IAsyncLocalValueMap Set(
                IAsyncLocal key,
                object? value,
                bool treatNullValueAsNonexistent) =>
                value is not null || !treatNullValueAsNonexistent
                    ? new OneElementAsyncLocalValueMap(Pair(key, value))
                    : this;

            public bool TryGetValue(IAsyncLocal key, out object? value)
            {
                value = null;
                return false;
            }
        }

        private sealed class OneElementAsyncLocalValueMap(
            KeyValuePair<IAsyncLocal, object?> item0) : IAsyncLocalValueMap
        {
            public IAsyncLocalValueMap Set(
                IAsyncLocal key,
                object? value,
                bool treatNullValueAsNonexistent)
            {
                if (value is not null || !treatNullValueAsNonexistent)
                {
                    var item = Pair(key, value);
                    return ReferenceEquals(key, item0.Key)
                        ? new OneElementAsyncLocalValueMap(item)
                        : new TwoElementAsyncLocalValueMap(item0, item);
                }

                return ReferenceEquals(key, item0.Key) ? Empty : this;
            }

            public bool TryGetValue(IAsyncLocal key, out object? value)
            {
                if (ReferenceEquals(key, item0.Key))
                {
                    value = item0.Value;
                    return true;
                }

                value = null;
                return false;
            }
        }

        private sealed class TwoElementAsyncLocalValueMap(
            KeyValuePair<IAsyncLocal, object?> item0,
            KeyValuePair<IAsyncLocal, object?> item1) : IAsyncLocalValueMap
        {
            public IAsyncLocalValueMap Set(
                IAsyncLocal key,
                object? value,
                bool treatNullValueAsNonexistent)
            {
                if (value is not null || !treatNullValueAsNonexistent)
                {
                    var item = Pair(key, value);
                    return ReferenceEquals(key, item0.Key)
                        ? new TwoElementAsyncLocalValueMap(item, item1)
                        : ReferenceEquals(key, item1.Key)
                            ? new TwoElementAsyncLocalValueMap(item0, item)
                            : new ThreeElementAsyncLocalValueMap(
                                item0,
                                item1,
                                item);
                }

                return ReferenceEquals(key, item0.Key)
                    ? new OneElementAsyncLocalValueMap(item1)
                    : ReferenceEquals(key, item1.Key)
                        ? new OneElementAsyncLocalValueMap(item0)
                        : this;
            }

            public bool TryGetValue(IAsyncLocal key, out object? value)
            {
                if (ReferenceEquals(key, item0.Key))
                {
                    value = item0.Value;
                    return true;
                }
                if (ReferenceEquals(key, item1.Key))
                {
                    value = item1.Value;
                    return true;
                }

                value = null;
                return false;
            }
        }

        private sealed class ThreeElementAsyncLocalValueMap(
            KeyValuePair<IAsyncLocal, object?> item0,
            KeyValuePair<IAsyncLocal, object?> item1,
            KeyValuePair<IAsyncLocal, object?> item2) : IAsyncLocalValueMap
        {
            public IAsyncLocalValueMap Set(
                IAsyncLocal key,
                object? value,
                bool treatNullValueAsNonexistent)
            {
                if (value is not null || !treatNullValueAsNonexistent)
                {
                    var item = Pair(key, value);
                    return ReferenceEquals(key, item0.Key)
                        ? new ThreeElementAsyncLocalValueMap(item, item1, item2)
                        : ReferenceEquals(key, item1.Key)
                            ? new ThreeElementAsyncLocalValueMap(item0, item, item2)
                            : ReferenceEquals(key, item2.Key)
                                ? new ThreeElementAsyncLocalValueMap(item0, item1, item)
                                : new FourElementAsyncLocalValueMap(
                                    item0,
                                    item1,
                                    item2,
                                    item);
                }

                return ReferenceEquals(key, item0.Key)
                    ? new TwoElementAsyncLocalValueMap(item1, item2)
                    : ReferenceEquals(key, item1.Key)
                        ? new TwoElementAsyncLocalValueMap(item0, item2)
                        : ReferenceEquals(key, item2.Key)
                            ? new TwoElementAsyncLocalValueMap(item0, item1)
                            : this;
            }

            public bool TryGetValue(IAsyncLocal key, out object? value)
            {
                if (ReferenceEquals(key, item0.Key))
                {
                    value = item0.Value;
                    return true;
                }
                if (ReferenceEquals(key, item1.Key))
                {
                    value = item1.Value;
                    return true;
                }
                if (ReferenceEquals(key, item2.Key))
                {
                    value = item2.Value;
                    return true;
                }

                value = null;
                return false;
            }
        }

        private sealed class FourElementAsyncLocalValueMap(
            KeyValuePair<IAsyncLocal, object?> item0,
            KeyValuePair<IAsyncLocal, object?> item1,
            KeyValuePair<IAsyncLocal, object?> item2,
            KeyValuePair<IAsyncLocal, object?> item3) : IAsyncLocalValueMap
        {
            public IAsyncLocalValueMap Set(
                IAsyncLocal key,
                object? value,
                bool treatNullValueAsNonexistent)
            {
                if (value is not null || !treatNullValueAsNonexistent)
                {
                    var item = Pair(key, value);
                    return ReferenceEquals(key, item0.Key)
                        ? new FourElementAsyncLocalValueMap(item, item1, item2, item3)
                        : ReferenceEquals(key, item1.Key)
                            ? new FourElementAsyncLocalValueMap(item0, item, item2, item3)
                            : ReferenceEquals(key, item2.Key)
                                ? new FourElementAsyncLocalValueMap(item0, item1, item, item3)
                                : ReferenceEquals(key, item3.Key)
                                    ? new FourElementAsyncLocalValueMap(item0, item1, item2, item)
                                    : new MultiElementAsyncLocalValueMap(
                                        [item0, item1, item2, item3, item]);
                }

                return ReferenceEquals(key, item0.Key)
                    ? new ThreeElementAsyncLocalValueMap(item1, item2, item3)
                    : ReferenceEquals(key, item1.Key)
                        ? new ThreeElementAsyncLocalValueMap(item0, item2, item3)
                        : ReferenceEquals(key, item2.Key)
                            ? new ThreeElementAsyncLocalValueMap(item0, item1, item3)
                            : ReferenceEquals(key, item3.Key)
                                ? new ThreeElementAsyncLocalValueMap(item0, item1, item2)
                                : this;
            }

            public bool TryGetValue(IAsyncLocal key, out object? value)
            {
                if (ReferenceEquals(key, item0.Key))
                {
                    value = item0.Value;
                    return true;
                }
                if (ReferenceEquals(key, item1.Key))
                {
                    value = item1.Value;
                    return true;
                }
                if (ReferenceEquals(key, item2.Key))
                {
                    value = item2.Value;
                    return true;
                }
                if (ReferenceEquals(key, item3.Key))
                {
                    value = item3.Value;
                    return true;
                }

                value = null;
                return false;
            }
        }

        private sealed class MultiElementAsyncLocalValueMap(
            KeyValuePair<IAsyncLocal, object?>[] keyValues) : IAsyncLocalValueMap
        {
            internal const int MaxMultiElements = 16;

            public IAsyncLocalValueMap Set(
                IAsyncLocal key,
                object? value,
                bool treatNullValueAsNonexistent)
            {
                for (var index = 0; index < keyValues.Length; index++)
                {
                    if (!ReferenceEquals(key, keyValues[index].Key))
                    {
                        continue;
                    }

                    if (value is not null || !treatNullValueAsNonexistent)
                    {
                        var replacement = (KeyValuePair<IAsyncLocal, object?>[])
                            keyValues.Clone();
                        replacement[index] = Pair(key, value);
                        return new MultiElementAsyncLocalValueMap(replacement);
                    }

                    if (keyValues.Length == 5)
                    {
                        var remaining = new KeyValuePair<IAsyncLocal, object?>[4];
                        CopyExcept(keyValues, remaining, index);
                        return new FourElementAsyncLocalValueMap(
                            remaining[0],
                            remaining[1],
                            remaining[2],
                            remaining[3]);
                    }

                    var smaller = new KeyValuePair<IAsyncLocal, object?>[
                        keyValues.Length - 1];
                    CopyExcept(keyValues, smaller, index);
                    return new MultiElementAsyncLocalValueMap(smaller);
                }

                if (value is null && treatNullValueAsNonexistent)
                {
                    return this;
                }

                if (keyValues.Length < MaxMultiElements)
                {
                    var larger = new KeyValuePair<IAsyncLocal, object?>[
                        keyValues.Length + 1];
                    Array.Copy(keyValues, larger, keyValues.Length);
                    larger[keyValues.Length] = Pair(key, value);
                    return new MultiElementAsyncLocalValueMap(larger);
                }

                var many = new ManyElementAsyncLocalValueMap(
                    MaxMultiElements + 1);
                foreach (var pair in keyValues)
                {
                    many[pair.Key] = pair.Value;
                }
                many[key] = value;
                return many;
            }

            public bool TryGetValue(IAsyncLocal key, out object? value)
            {
                foreach (var pair in keyValues)
                {
                    if (ReferenceEquals(key, pair.Key))
                    {
                        value = pair.Value;
                        return true;
                    }
                }

                value = null;
                return false;
            }
        }

        private sealed class ManyElementAsyncLocalValueMap(int capacity) :
            Dictionary<IAsyncLocal, object?>(capacity), IAsyncLocalValueMap
        {
            public IAsyncLocalValueMap Set(
                IAsyncLocal key,
                object? value,
                bool treatNullValueAsNonexistent)
            {
                var containsKey = ContainsKey(key);
                if (value is not null || !treatNullValueAsNonexistent)
                {
                    var map = new ManyElementAsyncLocalValueMap(
                        Count + (containsKey ? 0 : 1));
                    CopyTo(map, keyToSkip: null);
                    map[key] = value;
                    return map;
                }

                if (!containsKey)
                {
                    return this;
                }

                if (Count == MultiElementAsyncLocalValueMap.MaxMultiElements + 1)
                {
                    var values = new KeyValuePair<IAsyncLocal, object?>[
                        MultiElementAsyncLocalValueMap.MaxMultiElements];
                    var index = 0;
                    foreach (var pair in this)
                    {
                        if (!ReferenceEquals(pair.Key, key))
                        {
                            values[index++] = pair;
                        }
                    }
                    return new MultiElementAsyncLocalValueMap(values);
                }

                var smaller = new ManyElementAsyncLocalValueMap(Count - 1);
                CopyTo(smaller, key);
                return smaller;
            }

            private void CopyTo(
                ManyElementAsyncLocalValueMap destination,
                IAsyncLocal? keyToSkip)
            {
                foreach (var pair in this)
                {
                    if (!ReferenceEquals(pair.Key, keyToSkip))
                    {
                        destination[pair.Key] = pair.Value;
                    }
                }
            }
        }

        private static void CopyExcept(
            KeyValuePair<IAsyncLocal, object?>[] source,
            KeyValuePair<IAsyncLocal, object?>[] destination,
            int excludedIndex)
        {
            if (excludedIndex != 0)
            {
                Array.Copy(source, destination, excludedIndex);
            }
            if (excludedIndex != source.Length - 1)
            {
                Array.Copy(
                    source,
                    excludedIndex + 1,
                    destination,
                    excludedIndex,
                    source.Length - excludedIndex - 1);
            }
        }
    }
}
