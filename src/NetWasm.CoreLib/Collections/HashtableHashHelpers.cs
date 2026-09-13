// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime System.Private.CoreLib HashHelpers.cs at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
// NetWasm retains only the managed prime-sizing helpers used by ordinary Hashtable;
// runtime fast-mod and formatter-serialization support are outside this boundary.

namespace System.Collections
{
    internal static class HashtableHashHelpers
    {
        internal const int HashPrime = 101;

        // This is the maximum prime smaller than Array.MaxLength.
        private const int MaxPrimeArrayLength = 0x7FFFFFC3;

        private static ReadOnlySpan<int> Primes =>
        [
            3, 7, 11, 17, 23, 29, 37, 47, 59, 71, 89, 107, 131, 163, 197, 239, 293, 353, 431, 521, 631, 761, 919,
            1103, 1327, 1597, 1931, 2333, 2801, 3371, 4049, 4861, 5839, 7013, 8419, 10103, 12143, 14591,
            17519, 21023, 25229, 30293, 36353, 43627, 52361, 62851, 75431, 90523, 108631, 130363, 156437,
            187751, 225307, 270371, 324449, 389357, 467237, 560689, 672827, 807403, 968897, 1162687, 1395263,
            1674319, 2009191, 2411033, 2893249, 3471899, 4166287, 4999559, 5999471, 7199369
        ];

        internal static bool IsPrime(int candidate)
        {
            if ((candidate & 1) != 0)
            {
                int limit = (int)Math.Sqrt(candidate);
                for (int divisor = 3; divisor <= limit; divisor += 2)
                {
                    if (candidate % divisor == 0)
                        return false;
                }

                return true;
            }

            return candidate == 2;
        }

        internal static int GetPrime(int min)
        {
            if (min < 0)
                throw new ArgumentException("Hashtable capacity overflow.");

            foreach (int prime in Primes)
            {
                if (prime >= min)
                    return prime;
            }

            for (int candidate = min | 1; candidate < int.MaxValue; candidate += 2)
            {
                if (IsPrime(candidate) && (candidate - 1) % HashPrime != 0)
                    return candidate;
            }

            return min;
        }

        internal static int ExpandPrime(int oldSize)
        {
            int newSize = 2 * oldSize;
            if ((uint)newSize > MaxPrimeArrayLength && MaxPrimeArrayLength > oldSize)
                return MaxPrimeArrayLength;

            return GetPrime(newSize);
        }
    }
}
