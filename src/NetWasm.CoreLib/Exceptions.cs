// Adapted from dotnet/runtime System.Private.CoreLib exception implementations.
// AggregateException support and dictionary-enumerator wiring are based on
// dotnet/runtime commit 811225a482702af7ecc35d817966bc70b88a3a23.
// The upstream implementation is licensed under MIT.
// Copyright (c) .NET Foundation and Contributors.
namespace System
{
    internal static class ExceptionHResults
    {
        internal const int Exception = unchecked((int)0x80131500);
        internal const int System = unchecked((int)0x80131501);
        internal const int Argument = unchecked((int)0x80070057);
        internal const int ArgumentOutOfRange = unchecked((int)0x80131502);
        internal const int Arithmetic = unchecked((int)0x80070216);
        internal const int ArrayTypeMismatch = unchecked((int)0x80131503);
        internal const int DivideByZero = unchecked((int)0x80020012);
        internal const int Format = unchecked((int)0x80131537);
        internal const int IndexOutOfRange = unchecked((int)0x80131508);
        internal const int InvalidCast = unchecked((int)0x80004002);
        internal const int InvalidOperation = unchecked((int)0x80131509);
        internal const int KeyNotFound = unchecked((int)0x80131577);
        internal const int NotSupported = unchecked((int)0x80131515);
        internal const int ObjectDisposed = unchecked((int)0x80131622);
        internal const int OperationCanceled = unchecked((int)0x8013153B);
        internal const int OutOfMemory = unchecked((int)0x8007000E);
        internal const int Overflow = unchecked((int)0x80131516);
        internal const int PlatformNotSupported = unchecked((int)0x80131539);
        internal const int Pointer = unchecked((int)0x80004003);
    }

    public class Exception
    {
        private readonly string? _message;
        private readonly Exception? _innerException;
        private string? _source;
        private string? _helpLink;
        private string? _stackTrace = null;
        private int _hResult;
        private Collections.IDictionary? _data;

        public Exception() => HResult = ExceptionHResults.Exception;
        public Exception(string? message)
        {
            _message = message;
            HResult = ExceptionHResults.Exception;
        }
        public Exception(string? message, Exception? innerException)
        {
            _message = message;
            _innerException = innerException;
            HResult = ExceptionHResults.Exception;
        }

        public virtual string Message
        {
            get => _message ?? "An exception was thrown.";
        }
        public Exception? InnerException
        {
            get => _innerException;
        }
        public new Type GetType() => base.GetType();
        public virtual string? StackTrace => _stackTrace;
        public virtual string? Source { get => _source; set => _source = value; }
        public virtual string? HelpLink { get => _helpLink; set => _helpLink = value; }
        public int HResult { get => _hResult; set => _hResult = value; }
        public virtual Collections.IDictionary Data
        {
            get => _data ??= new ExceptionDataDictionary();
        }

        public virtual Exception GetBaseException()
        {
            var current = this;
            while (current.InnerException is not null)
            {
                current = current.InnerException;
            }
            return current;
        }

        public override string ToString() => _innerException is null
            ? Message
            : Message + " ---> " + _innerException.ToString();

        private sealed class ExceptionDataDictionary : Collections.IDictionary
        {
            private object?[] _keys = new object?[4];
            private object?[] _values = new object?[4];
            private int _count;

            public object? this[object key]
            {
                get
                {
                    var index = IndexOf(key);
                    return index < 0 ? null : _values[index];
                }
                set
                {
                    var index = IndexOf(key);
                    if (index >= 0)
                    {
                        _values[index] = value;
                        return;
                    }
                    Add(key, value);
                }
            }

            public Collections.ICollection Keys => new ExceptionDataCollection(this, keys: true);
            public Collections.ICollection Values => new ExceptionDataCollection(this, keys: false);
            public int Count => _count;
            public object SyncRoot => this;
            public bool IsSynchronized => false;
            public bool IsReadOnly => false;
            public bool IsFixedSize => false;

            public bool Contains(object key) => IndexOf(key) >= 0;

            public void Add(object key, object? value)
            {
                ValidateKey(key);
                if (IndexOf(key) >= 0) throw new ArgumentException("An item with the same key has already been added.");
                EnsureCapacity(_count + 1);
                _keys[_count] = key;
                _values[_count] = value;
                _count++;
            }

            public void Remove(object key)
            {
                var index = IndexOf(key);
                if (index < 0) return;
                for (var current = index + 1; current < _count; current++)
                {
                    _keys[current - 1] = _keys[current];
                    _values[current - 1] = _values[current];
                }
                _count--;
                _keys[_count] = null;
                _values[_count] = null;
            }

            public void Clear()
            {
                for (var index = 0; index < _count; index++)
                {
                    _keys[index] = null;
                    _values[index] = null;
                }
                _count = 0;
            }

            public Collections.IDictionaryEnumerator GetEnumerator() => new ExceptionDataEnumerator(this);
            Collections.IEnumerator Collections.IEnumerable.GetEnumerator() => GetEnumerator();

            public void CopyTo(Array array, int index)
            {
                if (array is not object[] target || index < 0 || index > target.Length - _count)
                {
                    throw new ArgumentException();
                }
                for (var offset = 0; offset < _count; offset++)
                {
                    target[index + offset] = new ExceptionDataEntry(_keys[offset]!, _values[offset]);
                }
            }

            private int IndexOf(object key)
            {
                ValidateKey(key);
                for (var index = 0; index < _count; index++)
                {
                    if (object.Equals(_keys[index], key)) return index;
                }
                return -1;
            }

            private static void ValidateKey(object key)
            {
                if (key is null) throw new ArgumentNullException(nameof(key));
            }

            private void EnsureCapacity(int required)
            {
                if (required <= _keys.Length) return;
                var capacity = _keys.Length * 2;
                if (capacity < required) capacity = required;
                var keys = new object?[capacity];
                var values = new object?[capacity];
                Array.Copy(_keys, 0, keys, 0, _count);
                Array.Copy(_values, 0, values, 0, _count);
                _keys = keys;
                _values = values;
            }

            private sealed class ExceptionDataCollection : Collections.ICollection
            {
                internal readonly ExceptionDataDictionary _owner;
                internal readonly bool _keys;

                public ExceptionDataCollection(ExceptionDataDictionary owner, bool keys)
                {
                    _owner = owner;
                    _keys = keys;
                }

                public int Count => _owner._count;
                public object SyncRoot => _owner.SyncRoot;
                public bool IsSynchronized => false;

                public void CopyTo(Array array, int index)
                {
                    if (array is not object[] target || index < 0 || index > target.Length - _owner._count)
                    {
                        throw new ArgumentException();
                    }
                    for (var offset = 0; offset < _owner._count; offset++)
                    {
                        target[index + offset] = (_keys ? _owner._keys[offset] : _owner._values[offset])!;
                    }
                }

                public Collections.IEnumerator GetEnumerator() => new ExceptionDataCollectionEnumerator(this);
            }

            private sealed class ExceptionDataEnumerator : Collections.IDictionaryEnumerator
            {
                private readonly ExceptionDataDictionary _owner;
                private int _index = -1;

                public ExceptionDataEnumerator(ExceptionDataDictionary owner) => _owner = owner;
                public object Current => Entry;
                public Collections.DictionaryEntry Entry => new(_owner._keys[_index]!, _owner._values[_index]);
                public object Key => _owner._keys[_index]!;
                public object? Value => _owner._values[_index];
                public bool MoveNext() => ++_index < _owner._count;
                public void Reset() => _index = -1;
            }

            private sealed class ExceptionDataCollectionEnumerator : Collections.IEnumerator
            {
                private readonly ExceptionDataCollection _collection;
                private int _index = -1;

                public ExceptionDataCollectionEnumerator(ExceptionDataCollection collection) => _collection = collection;
                public object Current => _collection._keys
                    ? _collection._owner._keys[_index]!
                    : _collection._owner._values[_index]!;
                public bool MoveNext() => ++_index < _collection._owner._count;
                public void Reset() => _index = -1;
            }

            private sealed class ExceptionDataEntry
            {
                public ExceptionDataEntry(object key, object? value)
                {
                    Key = key;
                    Value = value;
                }

                public object Key { get; }
                public object? Value { get; }
            }
        }
    }

    public class SystemException : Exception
    {
        public SystemException() => HResult = ExceptionHResults.System;
        public SystemException(string? message) : base(message) => HResult = ExceptionHResults.System;
        public SystemException(string? message, Exception? innerException) : base(message, innerException) => HResult = ExceptionHResults.System;
    }

    public class PlatformNotSupportedException : NotSupportedException
    {
        public PlatformNotSupportedException() => HResult = ExceptionHResults.PlatformNotSupported;
        public PlatformNotSupportedException(string? message) : base(message) => HResult = ExceptionHResults.PlatformNotSupported;
        public PlatformNotSupportedException(string? message, Exception? innerException) : base(message, innerException) => HResult = ExceptionHResults.PlatformNotSupported;
    }

    public class NotSupportedException : SystemException
    {
        public NotSupportedException() => HResult = ExceptionHResults.NotSupported;
        public NotSupportedException(string? message) : base(message) => HResult = ExceptionHResults.NotSupported;
        public NotSupportedException(string? message, Exception? innerException) : base(message, innerException) => HResult = ExceptionHResults.NotSupported;
    }

    public class InvalidOperationException : SystemException
    {
        public InvalidOperationException() => HResult = ExceptionHResults.InvalidOperation;
        public InvalidOperationException(string? message) : base(message) => HResult = ExceptionHResults.InvalidOperation;
        public InvalidOperationException(string? message, Exception? innerException) : base(message, innerException) => HResult = ExceptionHResults.InvalidOperation;
    }

    public class ObjectDisposedException : InvalidOperationException
    {
        public ObjectDisposedException() => HResult = ExceptionHResults.ObjectDisposed;
        public ObjectDisposedException(string? objectName)
        {
            ObjectName = objectName;
            HResult = ExceptionHResults.ObjectDisposed;
        }
        public ObjectDisposedException(string? objectName, string? message) : base(message)
        {
            ObjectName = objectName;
            HResult = ExceptionHResults.ObjectDisposed;
        }
        public ObjectDisposedException(string? message, Exception? innerException) : base(message, innerException) =>
            HResult = ExceptionHResults.ObjectDisposed;
        public string? ObjectName { get; }

        public override string Message
        {
            get => ObjectName is null || ObjectName.Length == 0
                ? base.Message
                : base.Message + " (Object name: '" + ObjectName + "')";
        }

        // Runtime type-name metadata is intentionally unavailable in the
        // NetWasm profile, so these guards preserve the exception contract
        // without synthesizing an object name.
        public static void ThrowIf(bool condition, object instance)
        {
            if (condition) throw new ObjectDisposedException(null);
        }

        public static void ThrowIf(bool condition, Type type)
        {
            if (condition) throw new ObjectDisposedException(null);
        }
    }

    public class OperationCanceledException : SystemException
    {
        private readonly Threading.CancellationToken _cancellationToken;

        public OperationCanceledException() => HResult = ExceptionHResults.OperationCanceled;
        public OperationCanceledException(string? message) : base(message) => HResult = ExceptionHResults.OperationCanceled;
        public OperationCanceledException(string? message, Exception? innerException) : base(message, innerException) => HResult = ExceptionHResults.OperationCanceled;
        public OperationCanceledException(Threading.CancellationToken token)
            : this(null, null, token)
        {
        }
        public OperationCanceledException(
            string? message,
            Threading.CancellationToken token)
            : this(message, null, token)
        {
        }
        public OperationCanceledException(
            string? message,
            Exception? innerException,
            Threading.CancellationToken token)
            : base(message, innerException)
        {
            HResult = ExceptionHResults.OperationCanceled;
            _cancellationToken = token;
        }

        public Threading.CancellationToken CancellationToken => _cancellationToken;
    }

    public class NullReferenceException : SystemException
    {
        public NullReferenceException() => HResult = ExceptionHResults.Pointer;
        public NullReferenceException(string? message) : base(message) => HResult = ExceptionHResults.Pointer;
        public NullReferenceException(string? message, Exception? innerException) : base(message, innerException) => HResult = ExceptionHResults.Pointer;
    }

    public class IndexOutOfRangeException : SystemException
    {
        public IndexOutOfRangeException() => HResult = ExceptionHResults.IndexOutOfRange;
        public IndexOutOfRangeException(string? message) : base(message) => HResult = ExceptionHResults.IndexOutOfRange;
        public IndexOutOfRangeException(string? message, Exception? innerException) : base(message, innerException) => HResult = ExceptionHResults.IndexOutOfRange;
    }

    public class ArithmeticException : SystemException
    {
        public ArithmeticException() => HResult = ExceptionHResults.Arithmetic;
        public ArithmeticException(string? message) : base(message) => HResult = ExceptionHResults.Arithmetic;
        public ArithmeticException(string? message, Exception? innerException) : base(message, innerException) => HResult = ExceptionHResults.Arithmetic;
    }

    public class DivideByZeroException : ArithmeticException
    {
        public DivideByZeroException() => HResult = ExceptionHResults.DivideByZero;
        public DivideByZeroException(string? message) : base(message) => HResult = ExceptionHResults.DivideByZero;
        public DivideByZeroException(string? message, Exception? innerException) : base(message, innerException) => HResult = ExceptionHResults.DivideByZero;
    }

    public class OverflowException : ArithmeticException
    {
        public OverflowException() => HResult = ExceptionHResults.Overflow;
        public OverflowException(string? message) : base(message) => HResult = ExceptionHResults.Overflow;
        public OverflowException(string? message, Exception? innerException) : base(message, innerException) => HResult = ExceptionHResults.Overflow;
    }

    public class FormatException : SystemException
    {
        public FormatException() => HResult = ExceptionHResults.Format;
        public FormatException(string? message) : base(message) => HResult = ExceptionHResults.Format;
        public FormatException(string? message, Exception? innerException) : base(message, innerException) => HResult = ExceptionHResults.Format;
    }

    public class InvalidCastException : SystemException
    {
        public InvalidCastException() => HResult = ExceptionHResults.InvalidCast;
        public InvalidCastException(string? message) : base(message) => HResult = ExceptionHResults.InvalidCast;
        public InvalidCastException(string? message, Exception? innerException) : base(message, innerException) => HResult = ExceptionHResults.InvalidCast;
        public InvalidCastException(string? message, int hResult) : base(message) => HResult = hResult;
    }

    public class OutOfMemoryException : SystemException
    {
        public OutOfMemoryException() => HResult = ExceptionHResults.OutOfMemory;
        public OutOfMemoryException(string? message) : base(message) => HResult = ExceptionHResults.OutOfMemory;
        public OutOfMemoryException(string? message, Exception? innerException) : base(message, innerException) => HResult = ExceptionHResults.OutOfMemory;
    }

    public class ArgumentException : SystemException
    {
        public ArgumentException() => HResult = ExceptionHResults.Argument;
        public ArgumentException(string? message) : base(message) => HResult = ExceptionHResults.Argument;
        public ArgumentException(string? message, string? paramName) : base(message)
        {
            ParamName = paramName;
            HResult = ExceptionHResults.Argument;
        }
        public ArgumentException(string? message, Exception? innerException) : base(message, innerException) => HResult = ExceptionHResults.Argument;
        public ArgumentException(string? message, string? paramName, Exception? innerException) : base(message, innerException)
        {
            ParamName = paramName;
            HResult = ExceptionHResults.Argument;
        }
        public virtual string? ParamName { get; }

        public override string Message
        {
            get => ParamName is null || ParamName.Length == 0
                ? base.Message
                : base.Message + " (Parameter '" + ParamName + "')";
        }

        public static void ThrowIfNullOrEmpty(string? argument, string? paramName = null)
        {
            if (string.IsNullOrEmpty(argument))
            {
                ArgumentNullException.ThrowIfNull(argument, paramName);
                throw new ArgumentException("Value cannot be empty.", paramName);
            }
        }

        public static void ThrowIfNullOrWhiteSpace(string? argument, string? paramName = null)
        {
            if (argument is null || IsWhiteSpace(argument))
            {
                ArgumentNullException.ThrowIfNull(argument, paramName);
                throw new ArgumentException("The value cannot be whitespace.", paramName);
            }
        }

        private static bool IsWhiteSpace(string value)
        {
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (character is not (' ' or '\t' or '\r' or '\n' or '\f' or '\v')) return false;
            }
            return true;
        }
    }

    public class ArgumentNullException : ArgumentException
    {
        public ArgumentNullException() => HResult = ExceptionHResults.Pointer;
        public ArgumentNullException(string? paramName) : base(null, paramName) => HResult = ExceptionHResults.Pointer;
        public ArgumentNullException(string? paramName, string? message) : base(message, paramName) => HResult = ExceptionHResults.Pointer;
        public ArgumentNullException(string? paramName, Exception? innerException) : base(null, paramName, innerException) => HResult = ExceptionHResults.Pointer;

        public static void ThrowIfNull(object? argument, string? paramName = null)
        {
            if (argument is null) throw new ArgumentNullException(paramName);
        }

        public static unsafe void ThrowIfNull(void* argument, string? paramName = null)
        {
            if (argument is null) throw new ArgumentNullException(paramName);
        }
    }

    public class ArgumentOutOfRangeException : ArgumentException
    {
        public ArgumentOutOfRangeException() => HResult = ExceptionHResults.ArgumentOutOfRange;
        public ArgumentOutOfRangeException(string? paramName) : base(null, paramName) => HResult = ExceptionHResults.ArgumentOutOfRange;
        public ArgumentOutOfRangeException(string? paramName, string? message) : base(message, paramName) => HResult = ExceptionHResults.ArgumentOutOfRange;
        public ArgumentOutOfRangeException(string? paramName, object? actualValue, string? message) : base(message, paramName)
        {
            ActualValue = actualValue;
            HResult = ExceptionHResults.ArgumentOutOfRange;
        }
        public virtual object? ActualValue { get; }
        public override string Message
        {
            get => base.Message;
        }

        public ArgumentOutOfRangeException(string? message, Exception? innerException) : base(message, innerException) => HResult = ExceptionHResults.ArgumentOutOfRange;

        public static void ThrowIfEqual<T>(T value, T other, string? paramName = null)
        {
            if (object.Equals(value, other)) throw new ArgumentOutOfRangeException(paramName, value, "The value must not be equal to the other value.");
        }

        public static void ThrowIfNotEqual<T>(T value, T other, string? paramName = null)
        {
            if (!object.Equals(value, other)) throw new ArgumentOutOfRangeException(paramName, value, "The value must be equal to the other value.");
        }

        public static void ThrowIfGreaterThan<T>(T value, T other, string? paramName = null) where T : IComparable<T>
        {
            if (value.CompareTo(other) > 0) throw new ArgumentOutOfRangeException(paramName, value, "The value must be less than or equal to the other value.");
        }

        public static void ThrowIfGreaterThanOrEqual<T>(T value, T other, string? paramName = null) where T : IComparable<T>
        {
            if (value.CompareTo(other) >= 0) throw new ArgumentOutOfRangeException(paramName, value, "The value must be less than the other value.");
        }

        public static void ThrowIfLessThan<T>(T value, T other, string? paramName = null) where T : IComparable<T>
        {
            if (value.CompareTo(other) < 0) throw new ArgumentOutOfRangeException(paramName, value, "The value must be greater than or equal to the other value.");
        }

        public static void ThrowIfLessThanOrEqual<T>(T value, T other, string? paramName = null) where T : IComparable<T>
        {
            if (value.CompareTo(other) <= 0) throw new ArgumentOutOfRangeException(paramName, value, "The value must be greater than the other value.");
        }

        public static void ThrowIfNegativeOrZero<T>(T value, string? paramName = null)
            where T : Numerics.INumberBase<T>
        {
            if (T.IsNegative(value) || T.IsZero(value))
                throw new ArgumentOutOfRangeException(paramName, value, "The value must be positive.");
        }

        public static void ThrowIfNegative<T>(T value, string? paramName = null)
            where T : Numerics.INumberBase<T>
        {
            if (T.IsNegative(value))
                throw new ArgumentOutOfRangeException(paramName, value, "The value must be non-negative.");
        }

        public static void ThrowIfZero<T>(T value, string? paramName = null)
            where T : Numerics.INumberBase<T>
        {
            if (T.IsZero(value))
                throw new ArgumentOutOfRangeException(paramName, value, "The value must be non-zero.");
        }
    }

    public class ArrayTypeMismatchException : SystemException
    {
        public ArrayTypeMismatchException() => HResult = ExceptionHResults.ArrayTypeMismatch;
        public ArrayTypeMismatchException(string? message) : base(message) => HResult = ExceptionHResults.ArrayTypeMismatch;
        public ArrayTypeMismatchException(string? message, Exception? innerException) : base(message, innerException) => HResult = ExceptionHResults.ArrayTypeMismatch;
    }
}

namespace System
{
    public class AccessViolationException : SystemException { public AccessViolationException() { } public AccessViolationException(string? message) : base(message) { } public AccessViolationException(string? message, Exception? innerException) : base(message, innerException) { } }
    public class AppDomainUnloadedException : SystemException { public AppDomainUnloadedException() { } public AppDomainUnloadedException(string? message) : base(message) { } public AppDomainUnloadedException(string? message, Exception? innerException) : base(message, innerException) { } }
    public class ApplicationException : Exception { public ApplicationException() { } public ApplicationException(string? message) : base(message) { } public ApplicationException(string? message, Exception? innerException) : base(message, innerException) { } }
    public class CannotUnloadAppDomainException : SystemException { public CannotUnloadAppDomainException() { } public CannotUnloadAppDomainException(string? message) : base(message) { } public CannotUnloadAppDomainException(string? message, Exception? innerException) : base(message, innerException) { } }
    public class ContextMarshalException : SystemException { public ContextMarshalException() { } public ContextMarshalException(string? message) : base(message) { } public ContextMarshalException(string? message, Exception? innerException) : base(message, innerException) { } }
    public class DataMisalignedException : SystemException { public DataMisalignedException() { } public DataMisalignedException(string? message) : base(message) { } public DataMisalignedException(string? message, Exception? innerException) : base(message, innerException) { } }
    public class ExecutionEngineException : SystemException { public ExecutionEngineException() { } public ExecutionEngineException(string? message) : base(message) { } public ExecutionEngineException(string? message, Exception? innerException) : base(message, innerException) { } }
    public class InsufficientExecutionStackException : SystemException { public InsufficientExecutionStackException() { } public InsufficientExecutionStackException(string? message) : base(message) { } public InsufficientExecutionStackException(string? message, Exception? innerException) : base(message, innerException) { } }
    public class InsufficientMemoryException : OutOfMemoryException { public InsufficientMemoryException() { } public InsufficientMemoryException(string? message) : base(message) { } public InsufficientMemoryException(string? message, Exception? innerException) : base(message, innerException) { } }
    public class InvalidProgramException : SystemException { public InvalidProgramException() { } public InvalidProgramException(string? message) : base(message) { } public InvalidProgramException(string? message, Exception? innerException) : base(message, innerException) { } }
    public class InvalidTimeZoneException : Exception { public InvalidTimeZoneException() { } public InvalidTimeZoneException(string? message) : base(message) { } public InvalidTimeZoneException(string? message, Exception? innerException) : base(message, innerException) { } }
    public class MemberAccessException : SystemException { public MemberAccessException() { } public MemberAccessException(string? message) : base(message) { } public MemberAccessException(string? message, Exception? innerException) : base(message, innerException) { } }
    public class MethodAccessException : MemberAccessException { public MethodAccessException() { } public MethodAccessException(string? message) : base(message) { } public MethodAccessException(string? message, Exception? innerException) : base(message, innerException) { } }
    public class MulticastNotSupportedException : SystemException { public MulticastNotSupportedException() { } public MulticastNotSupportedException(string? message) : base(message) { } public MulticastNotSupportedException(string? message, Exception? innerException) : base(message, innerException) { } }
    public class NotImplementedException : SystemException { public NotImplementedException() { } public NotImplementedException(string? message) : base(message) { } public NotImplementedException(string? message, Exception? innerException) : base(message, innerException) { } }
    public class RankException : SystemException { public RankException() { } public RankException(string? message) : base(message) { } public RankException(string? message, Exception? innerException) : base(message, innerException) { } }
    public class StackOverflowException : SystemException { public StackOverflowException() { } public StackOverflowException(string? message) : base(message) { } public StackOverflowException(string? message, Exception? innerException) : base(message, innerException) { } }
    public class TimeoutException : SystemException { public TimeoutException() { } public TimeoutException(string? message) : base(message) { } public TimeoutException(string? message, Exception? innerException) : base(message, innerException) { } }
    public class TimeZoneNotFoundException : Exception { public TimeZoneNotFoundException() { } public TimeZoneNotFoundException(string? message) : base(message) { } public TimeZoneNotFoundException(string? message, Exception? innerException) : base(message, innerException) { } }
    public class TypeUnloadedException : SystemException { public TypeUnloadedException() { } public TypeUnloadedException(string? message) : base(message) { } public TypeUnloadedException(string? message, Exception? innerException) : base(message, innerException) { } }
    public class UnauthorizedAccessException : SystemException { public UnauthorizedAccessException() { } public UnauthorizedAccessException(string? message) : base(message) { } public UnauthorizedAccessException(string? message, Exception? innerException) : base(message, innerException) { } }

    public class BadImageFormatException : SystemException
    {
        private readonly string? _fileName;
        private readonly string? _fusionLog = null;
        public BadImageFormatException() { }
        public BadImageFormatException(string? message) : base(message) { }
        public BadImageFormatException(string? message, Exception? innerException) : base(message, innerException) { }
        public BadImageFormatException(string? message, string? fileName) : base(message) => _fileName = fileName;
        public BadImageFormatException(string? message, string? fileName, Exception? innerException) : base(message, innerException) => _fileName = fileName;
        public string? FileName
        {
            get => _fileName;
        }
        public string? FusionLog
        {
            get => _fusionLog;
        }
        public override string Message
        {
            get => base.Message;
        }
        public override string ToString()
        {
            return base.ToString();
        }
    }

    public class DuplicateWaitObjectException : ArgumentException
    {
        private readonly string? _parameterName;
        public DuplicateWaitObjectException() { }
        public DuplicateWaitObjectException(string? parameterName) : base(null, parameterName) => _parameterName = parameterName;
        public DuplicateWaitObjectException(string? parameterName, Exception? innerException) : base(null, parameterName, innerException) => _parameterName = parameterName;
        public DuplicateWaitObjectException(string? message, string? parameterName) : base(message, parameterName) => _parameterName = parameterName;
    }

    public class EntryPointNotFoundException : TypeLoadException { public EntryPointNotFoundException() { } public EntryPointNotFoundException(string? message) : base(message) { } public EntryPointNotFoundException(string? message, Exception? innerException) : base(message, innerException) { } }
    public class FieldAccessException : MemberAccessException { public FieldAccessException() { } public FieldAccessException(string? message) : base(message) { } public FieldAccessException(string? message, Exception? innerException) : base(message, innerException) { } }
    public class DllNotFoundException : TypeLoadException { public DllNotFoundException() { } public DllNotFoundException(string? message) : base(message) { } public DllNotFoundException(string? message, Exception? innerException) : base(message, innerException) { } }

    public class MissingMemberException : MemberAccessException
    {
        private readonly string? _memberName;
        public MissingMemberException() { }
        public MissingMemberException(string? message) : base(message) { }
        public MissingMemberException(string? message, Exception? innerException) : base(message, innerException) { }
        public MissingMemberException(string? message, string? memberName) : base(message) => _memberName = memberName;
        public override string Message
        {
            get => _memberName is null ? base.Message : base.Message + " " + _memberName;
        }
    }
    public class MissingFieldException : MissingMemberException
    {
        public MissingFieldException() { }
        public MissingFieldException(string? message) : base(message) { }
        public MissingFieldException(string? message, Exception? innerException) : base(message, innerException) { }
        public MissingFieldException(string? message, string? fieldName) : base(message, fieldName) { }
        public override string Message { get => base.Message; }
    }
    public class MissingMethodException : MissingMemberException
    {
        public MissingMethodException() { }
        public MissingMethodException(string? message) : base(message) { }
        public MissingMethodException(string? message, Exception? innerException) : base(message, innerException) { }
        public MissingMethodException(string? message, string? signature) : base(message, signature) { }
        public override string Message { get => base.Message; }
    }

    public class NotFiniteNumberException : ArithmeticException
    {
        private readonly double _offendingNumber;
        public NotFiniteNumberException() { }
        public NotFiniteNumberException(double offendingNumber) => _offendingNumber = offendingNumber;
        public NotFiniteNumberException(string? message) : base(message) { }
        public NotFiniteNumberException(string? message, double offendingNumber) : base(message) => _offendingNumber = offendingNumber;
        public NotFiniteNumberException(string? message, double offendingNumber, Exception? innerException) : base(message, innerException) => _offendingNumber = offendingNumber;
        public NotFiniteNumberException(string? message, Exception? innerException) : base(message, innerException) { }
        public double OffendingNumber
        {
            get => _offendingNumber;
        }
    }

    public class TypeLoadException : SystemException
    {
        private readonly string? _typeName = null;
        public TypeLoadException() { }
        public TypeLoadException(string? message) : base(message) { }
        public TypeLoadException(string? message, Exception? innerException) : base(message, innerException) { }
        public string? TypeName
        {
            get => _typeName;
        }
        public override string Message
        {
            get => base.Message;
        }
    }
    public class TypeAccessException : TypeLoadException { public TypeAccessException() { } public TypeAccessException(string? message) : base(message) { } public TypeAccessException(string? message, Exception? innerException) : base(message, innerException) { } }
    public class TypeInitializationException : SystemException
    {
        private readonly string? _typeName;
        public TypeInitializationException(string? typeName, Exception? innerException) : base("The type initializer for '" + typeName + "' threw an exception.", innerException) => _typeName = typeName;
        public string? TypeName
        {
            get => _typeName;
        }
    }
}

namespace System.Diagnostics
{
    public class UnreachableException : System.Exception
    {
        public UnreachableException() { }
        public UnreachableException(string? message) : base(message) { }
        public UnreachableException(string? message, System.Exception? innerException) : base(message, innerException) { }
    }
}

namespace System.Runtime
{
    public class AmbiguousImplementationException : System.Exception
    {
        public AmbiguousImplementationException() { }
        public AmbiguousImplementationException(string? message) : base(message) { }
        public AmbiguousImplementationException(string? message, System.Exception? innerException) : base(message, innerException) { }
    }
}

namespace System.Runtime.CompilerServices
{
    public class RuntimeWrappedException : System.Exception
    {
        private readonly object? _wrappedException;
        public RuntimeWrappedException(object? thrownObject) => _wrappedException = thrownObject;
        public object? WrappedException
        {
            get => _wrappedException;
        }
    }

    public class SwitchExpressionException : System.InvalidOperationException
    {
        private readonly object? _unmatchedValue;
        public SwitchExpressionException() { }
        public SwitchExpressionException(System.Exception? innerException) : base(null, innerException) { }
        public SwitchExpressionException(object? unmatchedValue) => _unmatchedValue = unmatchedValue;
        public SwitchExpressionException(string? message) : base(message) { }
        public SwitchExpressionException(string? message, System.Exception? innerException) : base(message, innerException) { }
        public override string Message
        {
            get => base.Message;
        }
        public object? UnmatchedValue
        {
            get => _unmatchedValue;
        }
    }
}

namespace System.Runtime.InteropServices
{
    public class InvalidComObjectException : SystemException { public InvalidComObjectException() { } public InvalidComObjectException(string? message) : base(message) { } public InvalidComObjectException(string? message, Exception? innerException) : base(message, innerException) { } }
    public class InvalidOleVariantTypeException : SystemException { public InvalidOleVariantTypeException() { } public InvalidOleVariantTypeException(string? message) : base(message) { } public InvalidOleVariantTypeException(string? message, Exception? innerException) : base(message, innerException) { } }
    public class MarshalDirectiveException : SystemException { public MarshalDirectiveException() { } public MarshalDirectiveException(string? message) : base(message) { } public MarshalDirectiveException(string? message, Exception? innerException) : base(message, innerException) { } }
    public class SafeArrayRankMismatchException : SystemException { public SafeArrayRankMismatchException() { } public SafeArrayRankMismatchException(string? message) : base(message) { } public SafeArrayRankMismatchException(string? message, Exception? innerException) : base(message, innerException) { } }
    public class SafeArrayTypeMismatchException : SystemException { public SafeArrayTypeMismatchException() { } public SafeArrayTypeMismatchException(string? message) : base(message) { } public SafeArrayTypeMismatchException(string? message, Exception? innerException) : base(message, innerException) { } }
}

namespace System.Security.Cryptography
{
    public class CryptographicException : SystemException
    {
        public CryptographicException() { }
        public CryptographicException(int hResult) => HResult = hResult;
        public CryptographicException(string? message) : base(message) { }
        public CryptographicException(string? message, Exception? innerException) : base(message, innerException) { }
        public CryptographicException(string? format, string? insert) : base(format + insert) { }
    }
}

namespace System.Security
{
    public class SecurityException : SystemException
    {
        private object? _demanded;
        private object? _denySetInstance;
        private string? _grantedSet;
        private string? _permissionState;
        private Type? _permissionType;
        private object? _permitOnlySetInstance;
        private string? _refusedSet;
        private string? _url;
        public SecurityException() { }
        public SecurityException(string? message) : base(message) { }
        public SecurityException(string? message, Exception? innerException) : base(message, innerException) { }
        public SecurityException(string? message, Type? type) : base(message) => _permissionType = type;
        public SecurityException(string? message, Type? type, string? state) : base(message) { _permissionType = type; _permissionState = state; }
        public object? Demanded { get => _demanded; set => _demanded = value; }
        public object? DenySetInstance { get => _denySetInstance; set => _denySetInstance = value; }
        public string? GrantedSet { get => _grantedSet; set => _grantedSet = value; }
        public string? PermissionState { get => _permissionState; set => _permissionState = value; }
        public Type? PermissionType { get => _permissionType; set => _permissionType = value; }
        public object? PermitOnlySetInstance { get => _permitOnlySetInstance; set => _permitOnlySetInstance = value; }
        public string? RefusedSet { get => _refusedSet; set => _refusedSet = value; }
        public string? Url { get => _url; set => _url = value; }

        public override string ToString()
        {
            return base.ToString();
        }
    }

    public class VerificationException : SystemException { public VerificationException() { } public VerificationException(string? message) : base(message) { } public VerificationException(string? message, Exception? innerException) : base(message, innerException) { } }
}

namespace System.Runtime.ExceptionServices
{
    public sealed class ExceptionDispatchInfo
    {
        private readonly System.Exception _sourceException;
        private ExceptionDispatchInfo(System.Exception sourceException) => _sourceException = sourceException;
        public System.Exception SourceException
        {
            get => _sourceException;
        }
        public static ExceptionDispatchInfo Capture(System.Exception sourceException) =>
            sourceException is null ? throw new System.ArgumentNullException(nameof(sourceException)) : new(sourceException);
        public void Throw() => throw _sourceException;
        public static void Throw(System.Exception sourceException) => throw sourceException;
    }
}

namespace System.Collections.Generic
{
    public class KeyNotFoundException : SystemException
    {
        public KeyNotFoundException() => HResult = ExceptionHResults.KeyNotFound;
        public KeyNotFoundException(string? message) : base(message) => HResult = ExceptionHResults.KeyNotFound;
        public KeyNotFoundException(string? message, Exception? innerException) : base(message, innerException) => HResult = ExceptionHResults.KeyNotFound;
    }
}
