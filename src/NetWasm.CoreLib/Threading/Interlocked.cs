namespace System.Threading
{
    public static class Interlocked
    {
        public static T CompareExchange<T>(ref T location, T value, T comparand)
            where T : class?
        {
            var previous = location;
            if (object.ReferenceEquals(previous, comparand))
            {
                location = value;
            }
            return previous;
        }

        public static T Exchange<T>(ref T location, T value)
            where T : class?
        {
            var previous = location;
            location = value;
            return previous;
        }
    }
}
