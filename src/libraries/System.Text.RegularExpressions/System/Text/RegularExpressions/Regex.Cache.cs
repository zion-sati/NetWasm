// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace System.Text.RegularExpressions
{
    public partial class Regex
    {
        /// <summary>
        /// Gets or sets the maximum number of entries in the current static cache of regular expression
        /// instances.
        /// </summary>
        /// <value>The maximum number of entries in the static cache.</value>
        /// <remarks>
        /// <para>
        /// The <see cref="Regex"/> class maintains an internal cache of regular expression instances used in
        /// static <see cref="Regex"/> method calls, such as <see cref="Regex.Match(string, string)"/> or
        /// <see cref="Regex.Replace(string, string, string)"/>. If the value specified in a set operation is
        /// less than the current cache size, cache entries are discarded until the cache size is equal to the
        /// specified value.
        /// </para>
        /// <para>
        /// By default, the cache holds 15 static regular expression instances. Your application typically
        /// will not have to modify the size of the cache. Use the <see cref="CacheSize"/> property only when
        /// you want to turn off caching or when you have an unusually large cache.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">The value in a set operation is less than zero.</exception>
        public static int CacheSize
        {
            get => RegexCache.MaxCacheSize;
            set
            {
                if (value < 0)
                {
                    ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.value);
                }

                RegexCache.MaxCacheSize = value;
            }
        }
    }

    /// <summary>Cache used to store Regex instances used by the static methods on Regex.</summary>
    internal sealed class RegexCache
    {
        // NetWasm executes the CoreLib surface on a single reactor.  The cache therefore uses ordinary
        // collections and direct field access; no locks or concurrent collection machinery are needed.

        /// <summary>The default maximum number of items to store in the cache.</summary>
        private const int DefaultMaxCacheSize = 15;
        /// <summary>The maximum number of cached items to examine when we need to replace an existing one in the cache with a new one.</summary>
        /// <remarks>This is a somewhat arbitrary value, chosen to be small but at least as large as DefaultMaxCacheSize.</remarks>
        private const int MaxExamineOnDrop = 30;

        /// <summary>A read-through cache of one element, representing the most recently used regular expression.</summary>
        private static Node? s_lastAccessed;
        /// <summary>The dictionary storing all the items in the cache.</summary>
        private static readonly Dictionary<Key, Node> s_cacheDictionary = new(capacity: 31);
        /// <summary>A list of all the items in the cache.</summary>
        private static readonly List<Node> s_cacheList = new List<Node>(DefaultMaxCacheSize);
        /// <summary>Random number generator used to examine a subset of items when we need to drop one from a large list.</summary>
        private static readonly Random s_random = new Random();
        /// <summary>The current maximum number of items allowed in the cache.</summary>
        private static int s_maxCacheSize = DefaultMaxCacheSize;

        /// <summary>Gets or sets the maximum size of the cache.</summary>
        public static int MaxCacheSize
        {
            get => s_maxCacheSize;
            set
            {
                Debug.Assert(value >= 0);
                s_maxCacheSize = value;

                if (value == 0)
                {
                    s_cacheDictionary.Clear();
                    s_cacheList.Clear();
                    s_lastAccessed = null;
                }
                else if (value < s_cacheList.Count)
                {
                    // If the value is being changed to less than the number of items we're currently storing,
                    // discard the excess entries.
                    s_lastAccessed = s_cacheList[0];
                    for (int i = value; i < s_cacheList.Count; i++)
                    {
                        s_cacheDictionary.Remove(s_cacheList[i].Key);
                    }
                    s_cacheList.RemoveRange(value, s_cacheList.Count - value);

                    Debug.Assert(s_cacheList.Count == value);
                    Debug.Assert(s_cacheDictionary.Count == value);
                }
            }
        }

        public static Regex GetOrAdd(string pattern)
        {
            // Keep the no-options path separate so its cache key and default culture remain explicit.

            Regex.ValidatePattern(pattern);

            CultureInfo culture = CultureInfo.CurrentCulture;
            Key key = new Key(pattern, culture.ToString(), RegexOptions.None, Regex.s_defaultMatchTimeout);

            Regex? regex = Get(key);
            if (regex is null)
            {
                regex = new Regex(pattern, culture);
                Add(key, regex);
            }

            return regex;
        }

        public static Regex GetOrAdd(string pattern, RegexOptions options, TimeSpan matchTimeout)
        {
            Regex.ValidatePattern(pattern);
            Regex.ValidateOptions(options);
            Regex.ValidateMatchTimeout(matchTimeout);

            CultureInfo culture = RegexParser.GetTargetCulture(options);
            Key key = new Key(pattern, culture.ToString(), options, matchTimeout);

            Regex? regex = Get(key);
            if (regex is null)
            {
                regex = new Regex(pattern, options, matchTimeout, culture);
                Add(key, regex);
            }

            return regex;
        }

        private static Regex? Get(Key key)
        {
            long lastAccessedStamp = 0;

            // We optimize for repeated usage of the same regular expression over and over,
            // by having a fast-path that stores the most recently used instance.  Check
            // to see if that instance is the one we want; if it is, we're done.
            if (s_lastAccessed is Node lastAccessed)
            {
                if (key.Equals(lastAccessed.Key))
                {
                    return lastAccessed.Regex;
                }

                // We had a last accessed item, but it didn't match the one being requested.
                // In case we need to replace the last accessed node, remember this one's stamp;
                // we'll use it to compute the new access value for the new node replacing it.
                lastAccessedStamp = lastAccessed.LastAccessStamp;
            }

            // Now consult the full cache.
            if (s_maxCacheSize != 0 && // hot-read of s_maxCacheSize to try to avoid the cost of the dictionary lookup if the cache is disabled
                s_cacheDictionary.TryGetValue(key, out Node? node))
            {
                node.LastAccessStamp = lastAccessedStamp + 1;

                // Update our fast-path single-field cache.
                s_lastAccessed = node;

                // Return the cached regex.
                return node.Regex;
            }

            // Not in the cache.
            return null;
        }

        private static void Add(Key key, Regex regex)
        {
            Debug.Assert(s_cacheList.Count == s_cacheDictionary.Count);

            if (s_maxCacheSize == 0 || s_cacheDictionary.TryGetValue(key, out _))
            {
                return;
            }

            // If the cache is full, remove an item to make room for the new one.
            if (s_cacheList.Count == s_maxCacheSize)
            {
                int itemsToExamine;
                bool useRandom;

                if (s_maxCacheSize <= MaxExamineOnDrop)
                {
                    // Our maximum cache size is <= the number of items we're willing to examine (which is kept small simply
                    // to avoid spending a lot of time).  As such, we can just examine the whole list.
                    itemsToExamine = s_cacheList.Count;
                    useRandom = false;
                }
                else
                {
                    // Our maximum cache size is > the number of items we're willing to examine, so we'll instead
                    // examine a random subset.  This isn't perfect: if the size of the list is only a tiny bit
                    // larger than the max we're willing to examine, there's a good chance we'll look at some of
                    // the same items twice.  That's fine; this doesn't need to be perfect.  We do not need a perfect LRU
                    // cache, just one that generally gets rid of older things when new things come in.
                    itemsToExamine = MaxExamineOnDrop;
                    useRandom = true;
                }

                // Pick the first item to use as the min.
                int minListIndex = useRandom ? s_random.Next(s_cacheList.Count) : 0;
                long min = s_cacheList[minListIndex].LastAccessStamp;

                // Now examine the rest, keeping track of the smallest access stamp we find.
                for (int i = 1; i < itemsToExamine; i++)
                {
                    int nextIndex = useRandom ? s_random.Next(s_cacheList.Count) : i;
                    long next = s_cacheList[nextIndex].LastAccessStamp;
                    if (next < min)
                    {
                        minListIndex = nextIndex;
                        min = next;
                    }
                }

                // Remove the key found to have the smallest access stamp. List ordering isn't important, so rather than
                // just removing the element at minListIndex, which would result in an O(N) shift down, we copy the last
                // element to minListIndex, and then remove the last. (If minListIndex is the last, this is a no-op.)
                s_cacheDictionary.Remove(s_cacheList[minListIndex].Key);
                s_cacheList[minListIndex] = s_cacheList[^1];
                s_cacheList.RemoveAt(s_cacheList.Count - 1);
            }

            // Finally add the regex.
            var node = new Node(key, regex);

            if (s_lastAccessed is { } lastAccessed)
            {
                node.LastAccessStamp = lastAccessed.LastAccessStamp + 1;
            }

            s_lastAccessed = node;
            s_cacheList.Add(node);
            s_cacheDictionary.TryAdd(key, node);

            Debug.Assert(s_cacheList.Count <= s_maxCacheSize);
            Debug.Assert(s_cacheList.Count == s_cacheDictionary.Count);
        }

        /// <summary>Used as a key for <see cref="Node"/>.</summary>
        internal readonly struct Key : IEquatable<Key>
        {
            private readonly string _pattern;
            private readonly string _culture;
            private readonly RegexOptions _options;
            private readonly TimeSpan _matchTimeout;

            public Key(string pattern, string culture, RegexOptions options, TimeSpan matchTimeout)
            {
                Debug.Assert(pattern != null, "Pattern must be provided");
                Debug.Assert(culture != null, "Culture must be provided");

                _pattern = pattern;
                _culture = culture;
                _options = options;
                _matchTimeout = matchTimeout;
            }

            public override bool Equals([NotNullWhen(true)] object? obj) =>
                obj is Key other && Equals(other);

            public bool Equals(Key other) =>
                _pattern.Equals(other._pattern) &&
                _culture.Equals(other._culture) &&
                _options == other._options &&
                _matchTimeout == other._matchTimeout;

            public override int GetHashCode() =>
                // Hash code only factors in pattern and options, as regex instances are unlikely to have
                // the same pattern and options but different culture and timeout.
                _pattern.GetHashCode() ^ (int)_options;
        }

        /// <summary>Node for a cached Regex instance.</summary>
        private sealed class Node(Key key, Regex regex)
        {
            /// <summary>The key associated with this cached instance.</summary>
            public readonly Key Key = key;
            /// <summary>The cached Regex instance.</summary>
            public readonly Regex Regex = regex;
            /// <summary>A "time" stamp representing the approximate last access time for this Regex.</summary>
            public long LastAccessStamp;
        }
    }
}
