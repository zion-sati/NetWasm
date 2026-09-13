namespace System.Runtime.InteropServices.TimeZones
{
    internal sealed class TimeZoneAssetDigestCalculator : ITimeZoneAssetDigestCalculator
    {
        private static readonly uint[] RoundConstants =
        {
            0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5,
            0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
            0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3,
            0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
            0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc,
            0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
            0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7,
            0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
            0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13,
            0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
            0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3,
            0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
            0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5,
            0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
            0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208,
            0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2,
        };

        public byte[] Calculate(byte[] contents, int length)
        {
            if (contents == null)
            {
                throw new ArgumentNullException();
            }
            if ((uint)length > (uint)contents.Length)
            {
                throw new ArgumentOutOfRangeException();
            }

            var bitLength = checked((ulong)length * 8);
            var paddedLength = checked((length + 9 + 63) / 64 * 64);
            var padded = new byte[paddedLength];
            Array.Copy(contents, 0, padded, 0, length);
            padded[length] = 0x80;
            for (var index = 0; index < 8; index++)
            {
                padded[paddedLength - 1 - index] = (byte)(bitLength >> index * 8);
            }

            var state = new uint[]
            {
                0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a,
                0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19,
            };
            var schedule = new uint[64];
            for (var block = 0; block < padded.Length; block += 64)
            {
                for (var index = 0; index < 16; index++)
                {
                    var offset = block + index * 4;
                    schedule[index] = (uint)(padded[offset] << 24 |
                        padded[offset + 1] << 16 |
                        padded[offset + 2] << 8 |
                        padded[offset + 3]);
                }
                for (var index = 16; index < 64; index++)
                {
                    var left = schedule[index - 15];
                    var right = schedule[index - 2];
                    var sigma0 = RotateRight(left, 7) ^ RotateRight(left, 18) ^ left >> 3;
                    var sigma1 = RotateRight(right, 17) ^ RotateRight(right, 19) ^ right >> 10;
                    schedule[index] = unchecked(
                        schedule[index - 16] + sigma0 + schedule[index - 7] + sigma1);
                }

                var a = state[0];
                var b = state[1];
                var c = state[2];
                var d = state[3];
                var e = state[4];
                var f = state[5];
                var g = state[6];
                var h = state[7];
                for (var index = 0; index < 64; index++)
                {
                    var sum1 = RotateRight(e, 6) ^ RotateRight(e, 11) ^ RotateRight(e, 25);
                    var choice = e & f ^ ~e & g;
                    var temporary1 = unchecked(h + sum1 + choice + RoundConstants[index] + schedule[index]);
                    var sum0 = RotateRight(a, 2) ^ RotateRight(a, 13) ^ RotateRight(a, 22);
                    var majority = a & b ^ a & c ^ b & c;
                    var temporary2 = unchecked(sum0 + majority);
                    h = g;
                    g = f;
                    f = e;
                    e = unchecked(d + temporary1);
                    d = c;
                    c = b;
                    b = a;
                    a = unchecked(temporary1 + temporary2);
                }
                state[0] = unchecked(state[0] + a);
                state[1] = unchecked(state[1] + b);
                state[2] = unchecked(state[2] + c);
                state[3] = unchecked(state[3] + d);
                state[4] = unchecked(state[4] + e);
                state[5] = unchecked(state[5] + f);
                state[6] = unchecked(state[6] + g);
                state[7] = unchecked(state[7] + h);
            }

            var digest = new byte[32];
            for (var index = 0; index < state.Length; index++)
            {
                var value = state[index];
                digest[index * 4] = (byte)(value >> 24);
                digest[index * 4 + 1] = (byte)(value >> 16);
                digest[index * 4 + 2] = (byte)(value >> 8);
                digest[index * 4 + 3] = (byte)value;
            }
            return digest;
        }

        private static uint RotateRight(uint value, int count) =>
            value >> count | value << 32 - count;
    }
}
