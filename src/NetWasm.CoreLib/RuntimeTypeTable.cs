using System.Numerics;
using System.Runtime.CompilerServices;

namespace System
{
    // The runtime type table (RuntimeTypeTableEncoder in the compiler), read in place: one bit
    // stream, least significant bit first, every offset a bit offset from the table's start. The
    // runtime keeps what its hot paths read; Type reads its flags, generic definition and arguments,
    // related type (declaring or element) and assembly here: a type's shape gives its flags and
    // definition in constant time, a rank over a section's presence bits finds its generic arguments
    // or related type, and a binary search over runs of IDs finds its assembly.
    internal static unsafe class RuntimeTypeTable
    {
        private const int RankBlockBits = 256;
        private const int ListSampleInterval = 8;

        internal static int Flags(int typeId)
        {
            byte* table = Table;
            return (int)Read(table, HeaderBits + 32L * Read(table, Shape(table, typeId), (int)(Header(table, 2) >> 12 & 63)), 32);
        }

        // A generic instantiation's open definition; the compiler includes it wherever this is reachable.
        internal static int GenericDefinition(int typeId)
        {
            byte* table = Table;
            uint widths = Header(table, 2);
            return (int)Read(table, Shape(table, typeId) + (widths >> 12 & 63) + (widths >> 18 & 63), (int)(Header(table, 3) >> 24 & 63));
        }

        // The type's TypeAttributes: the value its shape indexes among those past the flag words, with
        // the interface, abstract and sealed bits from its flags. The compiler includes them wherever
        // this is reachable.
        internal static int Attributes(int typeId)
        {
            byte* table = Table;
            uint widths = Header(table, 2);
            long offset = (widths >> 12 & 63) + (widths >> 18 & 63) + (Header(table, 3) >> 24 & 63);
            long index = Read(table, Shape(table, typeId) + offset, (int)((widths >> 24 & 63) - offset));
            int flags = Flags(typeId);
            return (int)Read(table, HeaderBits + 32 * (Header(table, 4) + index), 32) |
                ((flags & RuntimeTypeFactsFlags.Interface) != 0 ? 0x20 : 0) |
                ((flags & RuntimeTypeFactsFlags.Abstract) != 0 ? 0x80 : 0) |
                ((flags & RuntimeTypeFactsFlags.Sealed) != 0 ? 0x100 : 0);
        }

        // The interfaces in the type's own extra assignable list that it implements itself: the list's IDs
        // whose bits, past the last list, are set. Finding the list walks the section, as the runtime
        // does once at startup; the compiler includes the bits wherever this is reachable.
        internal static int[] Interfaces(int typeId)
        {
            byte* table = Table;
            int capacity = (int)Header(table, 1);
            int idBits = (int)(Header(table, 2) & 63);
            int countBits = (int)(Header(table, 3) & 63);
            long presence = Header(table, 16);
            long offset = presence + capacity;
            long own = -1;
            int ownCount = 0;
            long before = 0;
            long entries = 0;
            for (int id = 0; id < capacity; id++)
            {
                if (Read(table, presence + id, 1) == 0)
                {
                    continue;
                }
                int count = (int)Read(table, offset, countBits);
                if (id == typeId)
                {
                    own = offset + countBits;
                    ownCount = count;
                    before = entries;
                }
                offset += countBits + (long)count * idBits;
                entries += count;
            }
            int implemented = 0;
            for (int index = 0; index < ownCount; index++)
            {
                implemented += (int)Read(table, offset + before + index, 1);
            }
            int[] interfaces = new int[implemented];
            for (int index = 0, next = 0; index < ownCount; index++)
            {
                if (Read(table, offset + before + index, 1) != 0)
                {
                    interfaces[next++] = (int)Read(table, own + (long)index * idBits, idBits);
                }
            }
            return interfaces;
        }

        // The index (from 1) of the type's assembly: that of the last run of type IDs starting at or
        // before it. The compiler includes the runs wherever this is reachable.
        internal static int Assembly(int typeId)
        {
            byte* table = Table;
            long section = Header(table, 7);
            int assemblyBits = (int)Read(table, section, 6);
            int idBits = (int)(Header(table, 2) & 63);
            int runBits = idBits + assemblyBits;
            long runs = section + 6 + assemblyBits + idBits + 1;
            int low = 0, high = (int)Read(table, section + 6 + assemblyBits, idBits + 1) - 1;
            while (low < high)
            {
                int middle = (low + high + 1) / 2;
                if (Read(table, runs + (long)middle * runBits, idBits) <= typeId) low = middle;
                else high = middle - 1;
            }
            return (int)Read(table, runs + (long)low * runBits + idBits, assemblyBits);
        }

        // Whether a type has the ID: an unused ID's record holds shape 0.
        internal static bool IsRegistered(int typeId)
        {
            byte* table = Table;
            int shapeIndexBits = (int)(Header(table, 2) >> 6 & 63);
            return Read(table, Header(table, 6) + (long)typeId * shapeIndexBits, shapeIndexBits) != 0;
        }

        // The largest type ID + 1.
        internal static int Capacity() => (int)Header(Table, 1);

        // The entry assembly's index, or 0 for a program without one.
        internal static int EntryAssembly()
        {
            byte* table = Table;
            long section = Header(table, 7);
            return (int)Read(table, section + 6, (int)Read(table, section, 6));
        }

        // How many assembly indexes the table's width allows, the largest index + 1 at most.
        internal static int AssemblyCapacity()
        {
            byte* table = Table;
            return 1 << (int)Read(table, Header(table, 7), 6);
        }

        // The type's generic arguments, or null when it has none or the table leaves them out.
        internal static int[]? GenericArguments(int typeId)
        {
            byte* table = Table;
            long section = Header(table, 17);
            if (section == 0)
            {
                return null;
            }
            long presence = Dictionary(table, section, out int valueBits);
            if (Read(table, presence + typeId, 1) == 0)
            {
                return null;
            }
            int countBits = (int)(Header(table, 3) >> 6 & 63);
            int sampleBits = (int)(Header(table, 3) >> 18 & 63);
            int rank = Rank(table, presence, typeId);
            long samples = presence + Header(table, 1) + Blocks(table) * RankBits(table);
            long lists = samples + (Header(table, 18) + ListSampleInterval - 1) / ListSampleInterval * (long)sampleBits;
            long offset = lists + Read(table, samples + rank / ListSampleInterval * (long)sampleBits, sampleBits);
            for (int skip = 0; skip < rank % ListSampleInterval; skip++)
            {
                offset += countBits + Read(table, offset, countBits) * (long)valueBits;
            }
            var arguments = new int[Read(table, offset, countBits)];
            for (int index = 0; index < arguments.Length; index++)
            {
                arguments[index] = Id(table, section, (int)Read(table, offset + countBits + (long)index * valueBits, valueBits));
            }
            return arguments;
        }

        // The enclosing type's or generic parameter owner's ID, or 0; the compiler includes it wherever
        // DeclaringType is reachable.
        internal static int DeclaringType(int typeId) => Related(typeId);

        // An array's, pointer's or by-reference type's element ID; the compiler includes it wherever
        // GetElementType is reachable.
        internal static int ElementType(int typeId) => Related(typeId);

        // The type's related type: a nested type's declaring type, a generic parameter's owner, or an
        // array's, pointer's or by-reference type's element. 0 for none.
        private static int Related(int typeId)
        {
            byte* table = Table;
            long section = Header(table, 19);
            if (section == 0)
            {
                return 0;
            }
            long presence = Dictionary(table, section, out int valueBits);
            if (Read(table, presence + typeId, 1) == 0)
            {
                return 0;
            }
            return Id(table, section, (int)Read(table, presence + Header(table, 1) + Blocks(table) * RankBits(table) +
                (long)Rank(table, presence, typeId) * valueBits, valueBits));
        }

        private const int HeaderBits = 20 * 32;

        // The bit offset of the type's shape: its flag index, layout index and generic definition.
        private static long Shape(byte* table, int typeId)
        {
            uint widths = Header(table, 2);
            int shapeIndexBits = (int)(widths >> 6 & 63);
            return Header(table, 8) + Read(table, Header(table, 6) + (long)typeId * shapeIndexBits, shapeIndexBits) *
                (long)(widths >> 24 & 63);
        }

        // A section's ID dictionary: returns where its presence bits start and the width of its values,
        // which are type IDs without a dictionary and indexes into it with one.
        private static long Dictionary(byte* table, long section, out int valueBits)
        {
            int idBits = (int)(Header(table, 2) & 63);
            uint count = Read(table, section, idBits + 1);
            valueBits = count == 0 ? idBits : 32 - BitOperations.LeadingZeroCount(count - 1);
            return section + idBits + 1 + count * (long)idBits;
        }

        private static int Id(byte* table, long section, int value)
        {
            int idBits = (int)(Header(table, 2) & 63);
            return Read(table, section, idBits + 1) == 0
                ? value
                : (int)Read(table, section + idBits + 1 + (long)value * idBits, idBits);
        }

        private static byte* Table => (byte*)Type.InternalGetFacts();

        private static uint Header(byte* table, int field) => Read(table, field * 32L, 32);

        private static int RankBits(byte* table) => (int)(Header(table, 3) >> 12 & 63);

        private static long Blocks(byte* table) => (Header(table, 1) + RankBlockBits - 1) / RankBlockBits;

        // Set presence bits before the type: its block's directory count, then the block's own bits.
        private static int Rank(byte* table, long presence, int typeId)
        {
            int block = typeId / RankBlockBits;
            int rank = (int)Read(table, presence + Header(table, 1) + block * (long)RankBits(table), RankBits(table));
            for (int bit = block * RankBlockBits; bit < typeId; bit += 32)
            {
                rank += BitOperations.PopCount(Read(table, presence + bit, Math.Min(32, typeId - bit)));
            }
            return rank;
        }

        private static uint Read(byte* table, long offset, int width)
        {
            ulong word = Unsafe.ReadUnaligned<ulong>(table + (offset >> 3)) >> (int)(offset & 7);
            return width == 32 ? (uint)word : (uint)(word & ((1UL << width) - 1));
        }
    }
}
