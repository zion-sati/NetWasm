// NetWasm portable single-reactor contract.
// This deliberately narrow Boolean surface is used by the retained ActivityListener
// publication flag; it does not claim cross-thread memory ordering.

namespace System.Threading
{
    internal static class Volatile
    {
        public static bool Read(ref bool location) => location;

        public static void Write(ref bool location, bool value) => location = value;
    }
}
