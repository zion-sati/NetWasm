namespace System.Collections.Generic
{
    internal static class SZArrayHelper
    {
        internal static IEnumerator<T> GetEnumerator<T>(T[] array) =>
            new SZArrayEnumerator<T>(array);

        internal static int GetCount<T>(T[] array) => array.Length;

        internal static bool GetIsReadOnly<T>(T[] array) => true;

        internal static T GetItem<T>(T[] array, int index) => array[index];

        internal static void SetItem<T>(T[] array, int index, T value) =>
            array[index] = value;

        internal static bool Contains<T>(T[] array, T value) =>
            System.Array.IndexOf(array, value) >= 0;

        internal static int IndexOf<T>(T[] array, T value) =>
            System.Array.IndexOf(array, value);

        internal static void CopyTo<T>(T[] array, T[] destination, int index) =>
            System.Array.Copy(array, 0, destination, index, array.Length);

        internal static void Add<T>(T[] array, T value) => ThrowFixedSize();

        internal static void Clear<T>(T[] array) => ThrowFixedSize();

        internal static bool Remove<T>(T[] array, T value) =>
            ThrowFixedSize<bool>();

        internal static void Insert<T>(T[] array, int index, T value) =>
            ThrowFixedSize();

        internal static void RemoveAt<T>(T[] array, int index) => ThrowFixedSize();

        private static void ThrowFixedSize() =>
            throw new System.NotSupportedException();

        private static TResult ThrowFixedSize<TResult>() =>
            throw new System.NotSupportedException();
    }

    internal sealed class SZArrayEnumerator<T> : IEnumerator<T>
    {
        private readonly T[] _array;
        private int _index;

        internal SZArrayEnumerator(T[] array)
        {
            _array = array;
            _index = -1;
        }

        public T Current
        {
            get
            {
                if (_index < 0 || _index >= _array.Length)
                {
                    throw new System.InvalidOperationException();
                }
                return _array[_index];
            }
        }

        object System.Collections.IEnumerator.Current => Current!;

        public bool MoveNext()
        {
            var next = _index + 1;
            if (next < _array.Length)
            {
                _index = next;
                return true;
            }
            _index = _array.Length;
            return false;
        }

        public void Dispose()
        {
        }

        void System.Collections.IEnumerator.Reset() =>
            throw new System.NotSupportedException();
    }
}
