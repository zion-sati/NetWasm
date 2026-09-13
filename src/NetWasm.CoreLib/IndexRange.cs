namespace System
{
    public readonly struct Index : IEquatable<Index>
    {
        private readonly int _value;

        public Index(int value, bool fromEnd = false)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException();
            }
            _value = fromEnd ? ~value : value;
        }

        public static Index Start
        {
            get => new(0);
        }
        public static Index End
        {
            get => new(0, fromEnd: true);
        }
        public static Index FromStart(int value) => new(value);
        public static Index FromEnd(int value) => new(value, fromEnd: true);
        public int Value
        {
            get => _value < 0 ? ~_value : _value;
        }
        public bool IsFromEnd
        {
            get => _value < 0;
        }

        public int GetOffset(int length)
        {
            var offset = _value;
            if (IsFromEnd)
            {
                offset += length + 1;
            }
            return offset;
        }

        public bool Equals(Index other) => _value == other._value;
        public override bool Equals(object? value) => value is Index other && Equals(other);
        public override int GetHashCode() => _value;
        public override string ToString() => IsFromEnd ? "^" + Value.ToString() : ((uint)Value).ToString();
        public static implicit operator Index(int value) => new(value);
    }

    public readonly struct Range : IEquatable<Range>
    {
        public Range(Index start, Index end)
        {
            Start = start;
            End = end;
        }

        public Index Start { get; }
        public Index End { get; }
        public static Range All
        {
            get => new(Index.Start, Index.End);
        }
        public static Range StartAt(Index start) => new(start, Index.End);
        public static Range EndAt(Index end) => new(Index.Start, end);

        public (int Offset, int Length) GetOffsetAndLength(int length)
        {
            var start = Start.GetOffset(length);
            var end = End.GetOffset(length);
            if ((uint)end > (uint)length || (uint)start > (uint)end)
            {
                throw new ArgumentOutOfRangeException();
            }
            return (start, end - start);
        }

        public bool Equals(Range other) => Start.Equals(other.Start) && End.Equals(other.End);
        public override bool Equals(object? value) => value is Range other && Equals(other);
        public override int GetHashCode() => unchecked(Start.GetHashCode() * 31 + End.GetHashCode());
        public override string ToString() => Start.ToString() + ".." + End.ToString();
    }
}
