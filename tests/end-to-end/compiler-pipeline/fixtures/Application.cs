using NetWasm.Fixtures.Library;

namespace NetWasm.Fixtures.Application
{
    public sealed class Counter
    {
        private int _value;

        public Counter(int value)
        {
            _value = value;
        }

        public void Add(int value)
        {
            _value = _value + value;
        }

        public int Value()
        {
            return _value;
        }
    }

    public static class EntryPoint
    {
        private static int _seed = 7;

        public static int Run(int input)
        {
            Counter counter = new Counter(SharedMath.Adjust(input) + _seed);
            int index = 0;
            while (index < 3)
            {
                counter.Add(index + 1);
                index = index + 1;
            }

            if (input > 0)
            {
                counter.Add(10);
            }
            else
            {
                counter.Add(-10);
            }

            string text = "a\u03a9\ud83d\ude00\0\ud800";
            int result = counter.Value()
                + text.Length
                + text[0]
                + text[1]
                + text[2]
                + text[3]
                + text[4]
                + text[5]
                + string.Empty.Length;
            return result;
        }

        public static int UnusedApplicationSentinel(int value)
        {
            string unused = "unused-literal-sentinel";
            return value + unused.Length + 987654318;
        }

        public static int ExportedOnly(int value)
        {
            return SharedMath.ExportedDependency(value);
        }

        public static int IndexAt(int index)
        {
            return "a"[index];
        }

        public static int LengthOf(string value)
        {
            return value.Length;
        }

        public static int CharAt(string value, int index)
        {
            return value[index];
        }
    }
}
