// Adapted from dotnet/runtime System.Private.CoreLib exception implementations.
// AggregateException support and dictionary-enumerator wiring are based on
// dotnet/runtime commit 474ed963699fc29809015392738a15231aaae2de.
// The upstream implementation is licensed under MIT.
// Copyright (c) .NET Foundation and Contributors.
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace System
{
    [Serializable]
    public partial class Exception : ISerializable
    {
        private readonly string? _message;
        private readonly Exception? _innerException;
        private string? _source;
        private string? _helpLink;
        private string? _stackTrace = null;
        private int _hResult;
        private Collections.IDictionary? _data;

        public Exception() => HResult = HResults.COR_E_EXCEPTION;
        public Exception(string? message)
        {
            _message = message;
            HResult = HResults.COR_E_EXCEPTION;
        }
        public Exception(string? message, Exception? innerException)
        {
            _message = message;
            _innerException = innerException;
            HResult = HResults.COR_E_EXCEPTION;
        }

        // Formatter execution is unavailable; retain the standard base contract
        // so derived library exception declarations and overrides bind normally.
        [Obsolete(Obsoletions.LegacyFormatterImplMessage, DiagnosticId = Obsoletions.LegacyFormatterImplDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        protected Exception(SerializationInfo info, StreamingContext context) =>
            throw new PlatformNotSupportedException("Exception formatter deserialization is not available on this NetWasm platform.");

        [Obsolete(Obsoletions.LegacyFormatterImplMessage, DiagnosticId = Obsoletions.LegacyFormatterImplDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        public virtual void GetObjectData(SerializationInfo info, StreamingContext context) =>
            throw new PlatformNotSupportedException("Exception.GetObjectData formatter serialization is not available on this NetWasm platform.");

        [Obsolete(Obsoletions.LegacyFormatterMessage, DiagnosticId = Obsoletions.LegacyFormatterDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        protected event EventHandler<SafeSerializationEventArgs> SerializeObjectState
        {
            add => throw new PlatformNotSupportedException("Exception.SerializeObjectState is not available on this NetWasm platform.");
            remove => throw new PlatformNotSupportedException("Exception.SerializeObjectState is not available on this NetWasm platform.");
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
        // The frames the runtime captured at the throw, which StackTrace(Exception) reads without
        // calling a derived type's StackTrace override, as upstream reads its stack data.
        internal string? CapturedStackTrace => _stackTrace;
        public virtual string? Source { get => _source; set => _source = value; }
        public virtual string? HelpLink { get => _helpLink; set => _helpLink = value; }
        public int HResult { get => _hResult; set => _hResult = value; }
        public virtual Collections.IDictionary Data
        {
            get => _data ??= new Collections.ListDictionaryInternal();
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

        // Upstream's shape: the class name, the message after ": ", the inner exception after " ---> " and the end
        // of its stack trace, then this exception's stack trace. NetWasm: the class name is the type's name, or its
        // ID without names (issue 225).
        public override string ToString()
        {
            string className = GetType().ToString();
            string message = Message;
            string result = string.IsNullOrEmpty(message) ? className : className + ": " + message;
            if (_innerException is not null)
            {
                result += Environment.NewLine + " ---> " + _innerException.ToString() + Environment.NewLine +
                    "   --- End of inner exception stack trace ---";
            }
            string? stackTrace = StackTrace;
            if (stackTrace is not null)
            {
                result += Environment.NewLine + stackTrace;
            }
            return result;
        }
    }

    [Serializable]
    public class SystemException : Exception
    {
        [Obsolete(Obsoletions.LegacyFormatterImplMessage, DiagnosticId = Obsoletions.LegacyFormatterImplDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        protected SystemException(SerializationInfo info, StreamingContext context) : base(info, context) { }

        public SystemException() => HResult = HResults.COR_E_SYSTEM;
        public SystemException(string? message) : base(message) => HResult = HResults.COR_E_SYSTEM;
        public SystemException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_SYSTEM;
    }

    [Serializable]
    public class PlatformNotSupportedException : NotSupportedException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected PlatformNotSupportedException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("PlatformNotSupportedException formatter deserialization is not available on this NetWasm platform.");

        public PlatformNotSupportedException() => HResult = HResults.COR_E_PLATFORMNOTSUPPORTED;
        public PlatformNotSupportedException(string? message) : base(message) => HResult = HResults.COR_E_PLATFORMNOTSUPPORTED;
        public PlatformNotSupportedException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_PLATFORMNOTSUPPORTED;
    }

    [Serializable]
    public class NotSupportedException : SystemException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected NotSupportedException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("NotSupportedException formatter deserialization is not available on this NetWasm platform.");

        public NotSupportedException() => HResult = HResults.COR_E_NOTSUPPORTED;
        public NotSupportedException(string? message) : base(message) => HResult = HResults.COR_E_NOTSUPPORTED;
        public NotSupportedException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_NOTSUPPORTED;
    }

    [Serializable]
    public class InvalidOperationException : SystemException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected InvalidOperationException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("InvalidOperationException formatter deserialization is not available on this NetWasm platform.");

        public InvalidOperationException() => HResult = HResults.COR_E_INVALIDOPERATION;
        public InvalidOperationException(string? message) : base(message) => HResult = HResults.COR_E_INVALIDOPERATION;
        public InvalidOperationException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_INVALIDOPERATION;
    }

    [Serializable]
    public partial class ObjectDisposedException : InvalidOperationException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected ObjectDisposedException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("ObjectDisposedException formatter deserialization is not available on this NetWasm platform.");

        private readonly string? _objectName;

        private ObjectDisposedException() : this(null, (string?)null) { }

        public ObjectDisposedException(string? objectName) : this(objectName, (string?)null) { }

        public ObjectDisposedException(string? objectName, string? message) : base(message)
        {
            _objectName = objectName;
            HResult = HResults.COR_E_OBJECTDISPOSED;
        }
        public ObjectDisposedException(string? message, Exception? innerException) : base(message, innerException) =>
            HResult = HResults.COR_E_OBJECTDISPOSED;
        public string ObjectName => _objectName ?? string.Empty;

        public override string Message
        {
            get => ObjectName is null || ObjectName.Length == 0
                ? base.Message
                : base.Message + " (Object name: '" + ObjectName + "')";
        }

        // Runtime type-name metadata is intentionally unavailable in the
        // NetWasm profile, so these guards preserve the exception contract
        // without synthesizing an object name.
        public static void ThrowIf([DoesNotReturnIf(true)] bool condition, object instance)
        {
            if (condition) throw new ObjectDisposedException(null);
        }

        public static void ThrowIf([DoesNotReturnIf(true)] bool condition, Type type)
        {
            if (condition) throw new ObjectDisposedException(null);
        }
    }

    [Serializable]
    public class OperationCanceledException : SystemException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected OperationCanceledException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("OperationCanceledException formatter deserialization is not available on this NetWasm platform.");

        private readonly Threading.CancellationToken _cancellationToken;

        public OperationCanceledException() => HResult = HResults.COR_E_OPERATIONCANCELED;
        public OperationCanceledException(string? message) : base(message) => HResult = HResults.COR_E_OPERATIONCANCELED;
        public OperationCanceledException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_OPERATIONCANCELED;
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
            HResult = HResults.COR_E_OPERATIONCANCELED;
            _cancellationToken = token;
        }

        public Threading.CancellationToken CancellationToken => _cancellationToken;
    }

    [Serializable]
    public class NullReferenceException : SystemException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected NullReferenceException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("NullReferenceException formatter deserialization is not available on this NetWasm platform.");

        public NullReferenceException() => HResult = HResults.E_POINTER;
        public NullReferenceException(string? message) : base(message) => HResult = HResults.E_POINTER;
        public NullReferenceException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.E_POINTER;
    }

    [Serializable]
    public sealed class IndexOutOfRangeException : SystemException
    {
        public IndexOutOfRangeException() => HResult = HResults.COR_E_INDEXOUTOFRANGE;
        public IndexOutOfRangeException(string? message) : base(message) => HResult = HResults.COR_E_INDEXOUTOFRANGE;
        public IndexOutOfRangeException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_INDEXOUTOFRANGE;
    }

    [Serializable]
    public class ArithmeticException : SystemException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected ArithmeticException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("ArithmeticException formatter deserialization is not available on this NetWasm platform.");

        public ArithmeticException() => HResult = HResults.COR_E_ARITHMETIC;
        public ArithmeticException(string? message) : base(message) => HResult = HResults.COR_E_ARITHMETIC;
        public ArithmeticException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_ARITHMETIC;
    }

    [Serializable]
    public class DivideByZeroException : ArithmeticException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected DivideByZeroException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("DivideByZeroException formatter deserialization is not available on this NetWasm platform.");

        public DivideByZeroException() => HResult = HResults.COR_E_DIVIDEBYZERO;
        public DivideByZeroException(string? message) : base(message) => HResult = HResults.COR_E_DIVIDEBYZERO;
        public DivideByZeroException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_DIVIDEBYZERO;
    }

    [Serializable]
    public class OverflowException : ArithmeticException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected OverflowException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("OverflowException formatter deserialization is not available on this NetWasm platform.");

        public OverflowException() => HResult = HResults.COR_E_OVERFLOW;
        public OverflowException(string? message) : base(message) => HResult = HResults.COR_E_OVERFLOW;
        public OverflowException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_OVERFLOW;
    }

    [Serializable]
    public class FormatException : SystemException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected FormatException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("FormatException formatter deserialization is not available on this NetWasm platform.");

        public FormatException() => HResult = HResults.COR_E_FORMAT;
        public FormatException(string? message) : base(message) => HResult = HResults.COR_E_FORMAT;
        public FormatException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_FORMAT;
    }

    [Serializable]
    public class InvalidCastException : SystemException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected InvalidCastException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("InvalidCastException formatter deserialization is not available on this NetWasm platform.");

        public InvalidCastException() => HResult = HResults.COR_E_INVALIDCAST;
        public InvalidCastException(string? message) : base(message) => HResult = HResults.COR_E_INVALIDCAST;
        public InvalidCastException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_INVALIDCAST;
        public InvalidCastException(string? message, int hResult) : base(message) => HResult = hResult;
    }

    [Serializable]
    public class OutOfMemoryException : SystemException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected OutOfMemoryException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("OutOfMemoryException formatter deserialization is not available on this NetWasm platform.");

        public OutOfMemoryException() => HResult = HResults.COR_E_OUTOFMEMORY;
        public OutOfMemoryException(string? message) : base(message) => HResult = HResults.COR_E_OUTOFMEMORY;
        public OutOfMemoryException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_OUTOFMEMORY;
    }

    [Serializable]
    public partial class ArgumentException : SystemException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected ArgumentException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("ArgumentException formatter deserialization is not available on this NetWasm platform.");

        public ArgumentException() => HResult = HResults.COR_E_ARGUMENT;
        public ArgumentException(string? message) : base(message) => HResult = HResults.COR_E_ARGUMENT;
        public ArgumentException(string? message, string? paramName) : base(message)
        {
            ParamName = paramName;
            HResult = HResults.COR_E_ARGUMENT;
        }
        public ArgumentException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_ARGUMENT;
        public ArgumentException(string? message, string? paramName, Exception? innerException) : base(message, innerException)
        {
            ParamName = paramName;
            HResult = HResults.COR_E_ARGUMENT;
        }
        public virtual string? ParamName { get; }

        public override string Message
        {
            get => ParamName is null || ParamName.Length == 0
                ? base.Message
                : base.Message + " (Parameter '" + ParamName + "')";
        }

        // Preserve upstream caller-expression metadata so ported callers that
        // omit paramName still report the offending argument, including RNG.
        public static void ThrowIfNullOrEmpty([NotNull] string? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
        {
            if (argument is null) throw new ArgumentNullException(paramName);
            if (argument.Length == 0)
                throw new ArgumentException("Value cannot be empty.", paramName);
        }

        public static void ThrowIfNullOrWhiteSpace([NotNull] string? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
        {
            if (argument is null) throw new ArgumentNullException(paramName);
            if (string.IsNullOrWhiteSpace(argument))
                throw new ArgumentException("The value cannot be whitespace.", paramName);
        }
    }

    [Serializable]
    public class ArgumentNullException : ArgumentException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected ArgumentNullException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("ArgumentNullException formatter deserialization is not available on this NetWasm platform.");

        public ArgumentNullException() => HResult = HResults.E_POINTER;
        public ArgumentNullException(string? paramName) : base(null, paramName) => HResult = HResults.E_POINTER;

        [DoesNotReturn]
        internal static void Throw(string? paramName) =>
            throw new ArgumentNullException(paramName);
        public ArgumentNullException(string? paramName, string? message) : base(message, paramName) => HResult = HResults.E_POINTER;
        public ArgumentNullException(string? paramName, Exception? innerException) : base(null, paramName, innerException) => HResult = HResults.E_POINTER;

        public static void ThrowIfNull([NotNull] object? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
        {
            if (argument is null) throw new ArgumentNullException(paramName);
        }

        public static unsafe void ThrowIfNull([NotNull] void* argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
        {
            if (argument is null) throw new ArgumentNullException(paramName);
        }

        // Upstream's internal overload: a zero IntPtr is null for CoreLib's native-pointer arguments.
        internal static void ThrowIfNull(IntPtr argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
        {
            if (argument == IntPtr.Zero) throw new ArgumentNullException(paramName);
        }
    }

    [Serializable]
    public partial class ArgumentOutOfRangeException : ArgumentException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected ArgumentOutOfRangeException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("ArgumentOutOfRangeException formatter deserialization is not available on this NetWasm platform.");

        public ArgumentOutOfRangeException() => HResult = HResults.COR_E_ARGUMENTOUTOFRANGE;
        public ArgumentOutOfRangeException(string? paramName) : base(null, paramName) => HResult = HResults.COR_E_ARGUMENTOUTOFRANGE;
        public ArgumentOutOfRangeException(string? paramName, string? message) : base(message, paramName) => HResult = HResults.COR_E_ARGUMENTOUTOFRANGE;
        public ArgumentOutOfRangeException(string? paramName, object? actualValue, string? message) : base(message, paramName)
        {
            ActualValue = actualValue;
            HResult = HResults.COR_E_ARGUMENTOUTOFRANGE;
        }
        public virtual object? ActualValue { get; }
        public override string Message
        {
            get => base.Message;
        }

        public ArgumentOutOfRangeException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_ARGUMENTOUTOFRANGE;

        public static void ThrowIfEqual<T>(T value, T other, [CallerArgumentExpression(nameof(value))] string? paramName = null)
        {
            if (EqualityComparer<T>.Default.Equals(value, other)) throw new ArgumentOutOfRangeException(paramName, value, "The value must not be equal to the other value.");
        }

        public static void ThrowIfNotEqual<T>(T value, T other, [CallerArgumentExpression(nameof(value))] string? paramName = null)
        {
            if (!EqualityComparer<T>.Default.Equals(value, other)) throw new ArgumentOutOfRangeException(paramName, value, "The value must be equal to the other value.");
        }

        public static void ThrowIfGreaterThan<T>(T value, T other, [CallerArgumentExpression(nameof(value))] string? paramName = null) where T : IComparable<T>
        {
            if (value.CompareTo(other) > 0) throw new ArgumentOutOfRangeException(paramName, value, "The value must be less than or equal to the other value.");
        }

        public static void ThrowIfGreaterThanOrEqual<T>(T value, T other, [CallerArgumentExpression(nameof(value))] string? paramName = null) where T : IComparable<T>
        {
            if (value.CompareTo(other) >= 0) throw new ArgumentOutOfRangeException(paramName, value, "The value must be less than the other value.");
        }

        public static void ThrowIfLessThan<T>(T value, T other, [CallerArgumentExpression(nameof(value))] string? paramName = null) where T : IComparable<T>
        {
            if (value.CompareTo(other) < 0) throw new ArgumentOutOfRangeException(paramName, value, "The value must be greater than or equal to the other value.");
        }

        public static void ThrowIfLessThanOrEqual<T>(T value, T other, [CallerArgumentExpression(nameof(value))] string? paramName = null) where T : IComparable<T>
        {
            if (value.CompareTo(other) <= 0) throw new ArgumentOutOfRangeException(paramName, value, "The value must be greater than the other value.");
        }

        public static void ThrowIfNegativeOrZero<T>(T value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
            where T : Numerics.INumberBase<T>
        {
            if (T.IsNegative(value) || T.IsZero(value))
                throw new ArgumentOutOfRangeException(paramName, value, "The value must be positive.");
        }

        public static void ThrowIfNegative<T>(T value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
            where T : Numerics.INumberBase<T>
        {
            if (T.IsNegative(value))
                throw new ArgumentOutOfRangeException(paramName, value, "The value must be non-negative.");
        }

        public static void ThrowIfZero<T>(T value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
            where T : Numerics.INumberBase<T>
        {
            if (T.IsZero(value))
                throw new ArgumentOutOfRangeException(paramName, value, "The value must be non-zero.");
        }
    }

    [Serializable]
    public class ArrayTypeMismatchException : SystemException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected ArrayTypeMismatchException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("ArrayTypeMismatchException formatter deserialization is not available on this NetWasm platform.");

        public ArrayTypeMismatchException() => HResult = HResults.COR_E_ARRAYTYPEMISMATCH;
        public ArrayTypeMismatchException(string? message) : base(message) => HResult = HResults.COR_E_ARRAYTYPEMISMATCH;
        public ArrayTypeMismatchException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_ARRAYTYPEMISMATCH;
    }
}

namespace System
{
    [Serializable]
    public class AccessViolationException : SystemException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected AccessViolationException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("AccessViolationException formatter deserialization is not available on this NetWasm platform.");
        public AccessViolationException() => HResult = HResults.E_POINTER; public AccessViolationException(string? message) : base(message) => HResult = HResults.E_POINTER; public AccessViolationException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.E_POINTER;
    }
    [Serializable]
    public class AppDomainUnloadedException : SystemException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected AppDomainUnloadedException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("AppDomainUnloadedException formatter deserialization is not available on this NetWasm platform.");
        public AppDomainUnloadedException() => HResult = HResults.COR_E_APPDOMAINUNLOADED; public AppDomainUnloadedException(string? message) : base(message) => HResult = HResults.COR_E_APPDOMAINUNLOADED; public AppDomainUnloadedException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_APPDOMAINUNLOADED;
    }
    [Serializable]
    public class ApplicationException : Exception
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected ApplicationException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("ApplicationException formatter deserialization is not available on this NetWasm platform.");
        public ApplicationException() => HResult = HResults.COR_E_APPLICATION; public ApplicationException(string? message) : base(message) => HResult = HResults.COR_E_APPLICATION; public ApplicationException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_APPLICATION;
    }
    [Serializable]
    public class CannotUnloadAppDomainException : SystemException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected CannotUnloadAppDomainException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("CannotUnloadAppDomainException formatter deserialization is not available on this NetWasm platform.");
        public CannotUnloadAppDomainException() => HResult = HResults.COR_E_CANNOTUNLOADAPPDOMAIN; public CannotUnloadAppDomainException(string? message) : base(message) => HResult = HResults.COR_E_CANNOTUNLOADAPPDOMAIN; public CannotUnloadAppDomainException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_CANNOTUNLOADAPPDOMAIN;
    }
    [Serializable]
    public class ContextMarshalException : SystemException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected ContextMarshalException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("ContextMarshalException formatter deserialization is not available on this NetWasm platform.");
        public ContextMarshalException() => HResult = HResults.COR_E_CONTEXTMARSHAL; public ContextMarshalException(string? message) : base(message) => HResult = HResults.COR_E_CONTEXTMARSHAL; public ContextMarshalException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_CONTEXTMARSHAL;
    }
    [Serializable]
    public sealed class DataMisalignedException : SystemException { public DataMisalignedException() => HResult = HResults.COR_E_DATAMISALIGNED; public DataMisalignedException(string? message) : base(message) => HResult = HResults.COR_E_DATAMISALIGNED; public DataMisalignedException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_DATAMISALIGNED; }
    [Serializable]
    public sealed class ExecutionEngineException : SystemException { public ExecutionEngineException() => HResult = HResults.COR_E_EXECUTIONENGINE; public ExecutionEngineException(string? message) : base(message) => HResult = HResults.COR_E_EXECUTIONENGINE; public ExecutionEngineException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_EXECUTIONENGINE; }
    [Serializable]
    public sealed class InsufficientExecutionStackException : SystemException { public InsufficientExecutionStackException() => HResult = HResults.COR_E_INSUFFICIENTEXECUTIONSTACK; public InsufficientExecutionStackException(string? message) : base(message) => HResult = HResults.COR_E_INSUFFICIENTEXECUTIONSTACK; public InsufficientExecutionStackException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_INSUFFICIENTEXECUTIONSTACK; }
    [Serializable]
    public sealed class InsufficientMemoryException : OutOfMemoryException { public InsufficientMemoryException() => HResult = HResults.COR_E_INSUFFICIENTMEMORY; public InsufficientMemoryException(string? message) : base(message) => HResult = HResults.COR_E_INSUFFICIENTMEMORY; public InsufficientMemoryException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_INSUFFICIENTMEMORY; }
    [Serializable]
    public sealed class InvalidProgramException : SystemException { public InvalidProgramException() => HResult = HResults.COR_E_INVALIDPROGRAM; public InvalidProgramException(string? message) : base(message) => HResult = HResults.COR_E_INVALIDPROGRAM; public InvalidProgramException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_INVALIDPROGRAM; }
    [Serializable]
    public class InvalidTimeZoneException : Exception
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected InvalidTimeZoneException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("InvalidTimeZoneException formatter deserialization is not available on this NetWasm platform.");
        public InvalidTimeZoneException() { }
        public InvalidTimeZoneException(string? message) : base(message) { }
        public InvalidTimeZoneException(string? message, Exception? innerException) : base(message, innerException) { }
    }
    [Serializable]
    public class MemberAccessException : SystemException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected MemberAccessException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("MemberAccessException formatter deserialization is not available on this NetWasm platform.");
        public MemberAccessException() => HResult = HResults.COR_E_MEMBERACCESS; public MemberAccessException(string? message) : base(message) => HResult = HResults.COR_E_MEMBERACCESS; public MemberAccessException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_MEMBERACCESS;
    }
    [Serializable]
    public class MethodAccessException : MemberAccessException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected MethodAccessException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("MethodAccessException formatter deserialization is not available on this NetWasm platform.");
        public MethodAccessException() => HResult = HResults.COR_E_METHODACCESS; public MethodAccessException(string? message) : base(message) => HResult = HResults.COR_E_METHODACCESS; public MethodAccessException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_METHODACCESS;
    }
    [Serializable]
    public sealed class MulticastNotSupportedException : SystemException { public MulticastNotSupportedException() => HResult = HResults.COR_E_MULTICASTNOTSUPPORTED; public MulticastNotSupportedException(string? message) : base(message) => HResult = HResults.COR_E_MULTICASTNOTSUPPORTED; public MulticastNotSupportedException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_MULTICASTNOTSUPPORTED; }
    [Serializable]
    public class NotImplementedException : SystemException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected NotImplementedException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("NotImplementedException formatter deserialization is not available on this NetWasm platform.");
        public NotImplementedException() => HResult = HResults.E_NOTIMPL; public NotImplementedException(string? message) : base(message) => HResult = HResults.E_NOTIMPL; public NotImplementedException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.E_NOTIMPL;
    }
    [Serializable]
    public class RankException : SystemException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected RankException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("RankException formatter deserialization is not available on this NetWasm platform.");
        public RankException() => HResult = HResults.COR_E_RANK; public RankException(string? message) : base(message) => HResult = HResults.COR_E_RANK; public RankException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_RANK;
    }
    [Serializable]
    public sealed class StackOverflowException : SystemException { public StackOverflowException() => HResult = HResults.COR_E_STACKOVERFLOW; public StackOverflowException(string? message) : base(message) => HResult = HResults.COR_E_STACKOVERFLOW; public StackOverflowException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_STACKOVERFLOW; }
    [Serializable]
    public class TimeoutException : SystemException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected TimeoutException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("TimeoutException formatter deserialization is not available on this NetWasm platform.");
        public TimeoutException() => HResult = HResults.COR_E_TIMEOUT; public TimeoutException(string? message) : base(message) => HResult = HResults.COR_E_TIMEOUT; public TimeoutException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_TIMEOUT;
    }
    [Serializable]
    public class TimeZoneNotFoundException : Exception
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected TimeZoneNotFoundException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("TimeZoneNotFoundException formatter deserialization is not available on this NetWasm platform.");
        public TimeZoneNotFoundException() { }
        public TimeZoneNotFoundException(string? message) : base(message) { }
        public TimeZoneNotFoundException(string? message, Exception? innerException) : base(message, innerException) { }
    }
    [Serializable]
    public class TypeUnloadedException : SystemException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected TypeUnloadedException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("TypeUnloadedException formatter deserialization is not available on this NetWasm platform.");
        public TypeUnloadedException() => HResult = HResults.COR_E_TYPEUNLOADED; public TypeUnloadedException(string? message) : base(message) => HResult = HResults.COR_E_TYPEUNLOADED; public TypeUnloadedException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_TYPEUNLOADED;
    }
    [Serializable]
    public class UnauthorizedAccessException : SystemException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected UnauthorizedAccessException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("UnauthorizedAccessException formatter deserialization is not available on this NetWasm platform.");
        public UnauthorizedAccessException() => HResult = HResults.COR_E_UNAUTHORIZEDACCESS; public UnauthorizedAccessException(string? message) : base(message) => HResult = HResults.COR_E_UNAUTHORIZEDACCESS; public UnauthorizedAccessException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_UNAUTHORIZEDACCESS;
    }

    [Serializable]
    public partial class BadImageFormatException : SystemException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected BadImageFormatException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("BadImageFormatException formatter deserialization is not available on this NetWasm platform.");

        private readonly string? _fileName;
        private readonly string? _fusionLog = null;
        public BadImageFormatException() => HResult = HResults.COR_E_BADIMAGEFORMAT;
        public BadImageFormatException(string? message) : base(message) => HResult = HResults.COR_E_BADIMAGEFORMAT;
        public BadImageFormatException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_BADIMAGEFORMAT;
        public BadImageFormatException(string? message, string? fileName) : base(message)
        {
            _fileName = fileName;
            HResult = HResults.COR_E_BADIMAGEFORMAT;
        }
        public BadImageFormatException(string? message, string? fileName, Exception? innerException) : base(message, innerException)
        {
            _fileName = fileName;
            HResult = HResults.COR_E_BADIMAGEFORMAT;
        }
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

    [Serializable]
    public class DuplicateWaitObjectException : ArgumentException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected DuplicateWaitObjectException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("DuplicateWaitObjectException formatter deserialization is not available on this NetWasm platform.");

        public DuplicateWaitObjectException() => HResult = HResults.COR_E_DUPLICATEWAITOBJECT;
        public DuplicateWaitObjectException(string? parameterName) : base(null, parameterName)
        {
            HResult = HResults.COR_E_DUPLICATEWAITOBJECT;
        }
        public DuplicateWaitObjectException(string? parameterName, string? message) : base(message, parameterName)
        {
            HResult = HResults.COR_E_DUPLICATEWAITOBJECT;
        }
        public DuplicateWaitObjectException(string? message, Exception? innerException) : base(message, innerException)
        {
            HResult = HResults.COR_E_DUPLICATEWAITOBJECT;
        }
    }

    [Serializable]
    public class EntryPointNotFoundException : TypeLoadException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected EntryPointNotFoundException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("EntryPointNotFoundException formatter deserialization is not available on this NetWasm platform.");
        public EntryPointNotFoundException() => HResult = HResults.COR_E_ENTRYPOINTNOTFOUND; public EntryPointNotFoundException(string? message) : base(message) => HResult = HResults.COR_E_ENTRYPOINTNOTFOUND; public EntryPointNotFoundException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_ENTRYPOINTNOTFOUND;
    }
    [Serializable]
    public class FieldAccessException : MemberAccessException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected FieldAccessException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("FieldAccessException formatter deserialization is not available on this NetWasm platform.");
        public FieldAccessException() => HResult = HResults.COR_E_FIELDACCESS; public FieldAccessException(string? message) : base(message) => HResult = HResults.COR_E_FIELDACCESS; public FieldAccessException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_FIELDACCESS;
    }
    [Serializable]
    public class DllNotFoundException : TypeLoadException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected DllNotFoundException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("DllNotFoundException formatter deserialization is not available on this NetWasm platform.");
        public DllNotFoundException() => HResult = HResults.COR_E_DLLNOTFOUND; public DllNotFoundException(string? message) : base(message) => HResult = HResults.COR_E_DLLNOTFOUND; public DllNotFoundException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_DLLNOTFOUND;
    }

    [Serializable]
    public partial class MissingMemberException : MemberAccessException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected MissingMemberException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("MissingMemberException formatter deserialization is not available on this NetWasm platform.");

        protected string? ClassName;
        protected string? MemberName;
        protected byte[]? Signature;

        public MissingMemberException() => HResult = HResults.COR_E_MISSINGMEMBER;
        public MissingMemberException(string? message) : base(message) => HResult = HResults.COR_E_MISSINGMEMBER;
        public MissingMemberException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_MISSINGMEMBER;
        public MissingMemberException(string? className, string? memberName)
        {
            ClassName = className;
            MemberName = memberName;
            HResult = HResults.COR_E_MISSINGMEMBER;
        }
        public override string Message
        {
            get => ClassName is null
                ? base.Message
                : "Member '" + ClassName + "." + MemberName + "' not found.";
        }
    }
    [Serializable]
    public class MissingFieldException : MissingMemberException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected MissingFieldException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("MissingFieldException formatter deserialization is not available on this NetWasm platform.");

        public MissingFieldException() => HResult = HResults.COR_E_MISSINGFIELD;
        public MissingFieldException(string? message) : base(message) => HResult = HResults.COR_E_MISSINGFIELD;
        public MissingFieldException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_MISSINGFIELD;
        public MissingFieldException(string? className, string? fieldName) : base(className, fieldName) => HResult = HResults.COR_E_MISSINGFIELD;
        public override string Message => ClassName is null
            ? base.Message
            : "Field '" + ClassName + "." + MemberName + "' not found.";
    }
    [Serializable]
    public class MissingMethodException : MissingMemberException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected MissingMethodException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("MissingMethodException formatter deserialization is not available on this NetWasm platform.");

        public MissingMethodException() => HResult = HResults.COR_E_MISSINGMETHOD;
        public MissingMethodException(string? message) : base(message) => HResult = HResults.COR_E_MISSINGMETHOD;
        public MissingMethodException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_MISSINGMETHOD;
        public MissingMethodException(string? className, string? methodName) : base(className, methodName) => HResult = HResults.COR_E_MISSINGMETHOD;
        public override string Message => ClassName is null
            ? base.Message
            : "Method '" + ClassName + "." + MemberName + "' not found.";
    }

    [Serializable]
    public partial class NotFiniteNumberException : ArithmeticException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected NotFiniteNumberException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("NotFiniteNumberException formatter deserialization is not available on this NetWasm platform.");

        private readonly double _offendingNumber;
        public NotFiniteNumberException() => HResult = HResults.COR_E_NOTFINITENUMBER;
        public NotFiniteNumberException(double offendingNumber)
        {
            _offendingNumber = offendingNumber;
            HResult = HResults.COR_E_NOTFINITENUMBER;
        }
        public NotFiniteNumberException(string? message) : base(message) => HResult = HResults.COR_E_NOTFINITENUMBER;
        public NotFiniteNumberException(string? message, double offendingNumber) : base(message)
        {
            _offendingNumber = offendingNumber;
            HResult = HResults.COR_E_NOTFINITENUMBER;
        }
        public NotFiniteNumberException(string? message, double offendingNumber, Exception? innerException) : base(message, innerException)
        {
            _offendingNumber = offendingNumber;
            HResult = HResults.COR_E_NOTFINITENUMBER;
        }
        public NotFiniteNumberException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_NOTFINITENUMBER;
        public double OffendingNumber
        {
            get => _offendingNumber;
        }
    }

    [Serializable]
    public partial class TypeLoadException : SystemException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected TypeLoadException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("TypeLoadException formatter deserialization is not available on this NetWasm platform.");

        private readonly string? _typeName = null;
        public TypeLoadException() => HResult = HResults.COR_E_TYPELOAD;
        public TypeLoadException(string? message) : base(message) => HResult = HResults.COR_E_TYPELOAD;
        public TypeLoadException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_TYPELOAD;
        public string TypeName => _typeName ?? string.Empty;
        public override string Message
        {
            get => base.Message;
        }
    }
    [Serializable]
    public class TypeAccessException : TypeLoadException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected TypeAccessException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("TypeAccessException formatter deserialization is not available on this NetWasm platform.");
        public TypeAccessException() => HResult = HResults.COR_E_TYPEACCESS; public TypeAccessException(string? message) : base(message) => HResult = HResults.COR_E_TYPEACCESS; public TypeAccessException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_TYPEACCESS;
    }
    [Serializable]
    public sealed partial class TypeInitializationException : SystemException
    {
        private readonly string? _typeName;
        public TypeInitializationException(string? typeName, Exception? innerException) : base("The type initializer for '" + typeName + "' threw an exception.", innerException)
        {
            _typeName = typeName;
            HResult = HResults.COR_E_TYPEINITIALIZATION;
        }
        public string TypeName => _typeName ?? string.Empty;
    }
}

namespace System.Diagnostics
{
    public sealed class UnreachableException : System.Exception
    {
        public UnreachableException() { }
        public UnreachableException(string? message) : base(message) { }
        public UnreachableException(string? message, System.Exception? innerException) : base(message, innerException) { }
    }
}

namespace System.Runtime
{
    [Serializable]
    public sealed class AmbiguousImplementationException : System.Exception
    {
        public AmbiguousImplementationException() => HResult = System.HResults.COR_E_AMBIGUOUSIMPLEMENTATION;
        public AmbiguousImplementationException(string? message) : base(message) => HResult = System.HResults.COR_E_AMBIGUOUSIMPLEMENTATION;
        public AmbiguousImplementationException(string? message, System.Exception? innerException) : base(message, innerException) => HResult = System.HResults.COR_E_AMBIGUOUSIMPLEMENTATION;
    }
}

namespace System.Runtime.CompilerServices
{
    [Serializable]
    public sealed partial class RuntimeWrappedException : System.Exception
    {
        private readonly object? _wrappedException;
        public RuntimeWrappedException(object? thrownObject)
        {
            _wrappedException = thrownObject;
            HResult = System.HResults.COR_E_RUNTIMEWRAPPED;
        }
        public object? WrappedException
        {
            get => _wrappedException;
        }
    }

    [Serializable]
    public sealed partial class SwitchExpressionException : System.InvalidOperationException
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
    [Serializable]
    public class InvalidComObjectException : SystemException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected InvalidComObjectException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("InvalidComObjectException formatter deserialization is not available on this NetWasm platform.");
        public InvalidComObjectException() => HResult = HResults.COR_E_INVALIDCOMOBJECT; public InvalidComObjectException(string? message) : base(message) => HResult = HResults.COR_E_INVALIDCOMOBJECT; public InvalidComObjectException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_INVALIDCOMOBJECT;
    }
    [Serializable]
    public class InvalidOleVariantTypeException : SystemException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected InvalidOleVariantTypeException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("InvalidOleVariantTypeException formatter deserialization is not available on this NetWasm platform.");
        public InvalidOleVariantTypeException() => HResult = HResults.COR_E_INVALIDOLEVARIANTTYPE; public InvalidOleVariantTypeException(string? message) : base(message) => HResult = HResults.COR_E_INVALIDOLEVARIANTTYPE; public InvalidOleVariantTypeException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_INVALIDOLEVARIANTTYPE;
    }
    [Serializable]
    public class MarshalDirectiveException : SystemException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected MarshalDirectiveException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("MarshalDirectiveException formatter deserialization is not available on this NetWasm platform.");
        public MarshalDirectiveException() => HResult = HResults.COR_E_MARSHALDIRECTIVE; public MarshalDirectiveException(string? message) : base(message) => HResult = HResults.COR_E_MARSHALDIRECTIVE; public MarshalDirectiveException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_MARSHALDIRECTIVE;
    }
    [Serializable]
    public class SafeArrayRankMismatchException : SystemException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected SafeArrayRankMismatchException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("SafeArrayRankMismatchException formatter deserialization is not available on this NetWasm platform.");
        public SafeArrayRankMismatchException() => HResult = HResults.COR_E_SAFEARRAYRANKMISMATCH; public SafeArrayRankMismatchException(string? message) : base(message) => HResult = HResults.COR_E_SAFEARRAYRANKMISMATCH; public SafeArrayRankMismatchException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_SAFEARRAYRANKMISMATCH;
    }
    [Serializable]
    public class SafeArrayTypeMismatchException : SystemException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected SafeArrayTypeMismatchException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("SafeArrayTypeMismatchException formatter deserialization is not available on this NetWasm platform.");
        public SafeArrayTypeMismatchException() => HResult = HResults.COR_E_SAFEARRAYTYPEMISMATCH; public SafeArrayTypeMismatchException(string? message) : base(message) => HResult = HResults.COR_E_SAFEARRAYTYPEMISMATCH; public SafeArrayTypeMismatchException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_SAFEARRAYTYPEMISMATCH;
    }
}

namespace System.Security.Cryptography
{
    [Serializable]
    public class CryptographicException : SystemException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected CryptographicException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("CryptographicException formatter deserialization is not available on this NetWasm platform.");

        public CryptographicException() { }
        public CryptographicException(int hResult) => HResult = hResult;
        public CryptographicException(string? message) : base(message) { }
        public CryptographicException(string? message, Exception? innerException) : base(message, innerException) { }
        public CryptographicException(string format, string? insert) : base(string.Format(format, insert)) { }
    }
}

namespace System.Security
{
    [Serializable]
    public partial class SecurityException : SystemException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected SecurityException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("SecurityException formatter deserialization is not available on this NetWasm platform.");

        private object? _demanded;
        private object? _denySetInstance;
        private string? _grantedSet;
        private string? _permissionState;
        private Type? _permissionType;
        private object? _permitOnlySetInstance;
        private string? _refusedSet;
        private string? _url;
        private System.Reflection.AssemblyName? _failedAssemblyInfo;
        private System.Reflection.MethodInfo? _method;
        public SecurityException() => HResult = HResults.COR_E_SECURITY;
        public SecurityException(string? message) : base(message) => HResult = HResults.COR_E_SECURITY;
        public SecurityException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_SECURITY;
        public SecurityException(string? message, Type? type) : base(message)
        {
            _permissionType = type;
            HResult = HResults.COR_E_SECURITY;
        }
        public SecurityException(string? message, Type? type, string? state) : base(message)
        {
            _permissionType = type;
            _permissionState = state;
            HResult = HResults.COR_E_SECURITY;
        }
        public object? Demanded { get => _demanded; set => _demanded = value; }
        public object? DenySetInstance { get => _denySetInstance; set => _denySetInstance = value; }
        public string? GrantedSet { get => _grantedSet; set => _grantedSet = value; }
        public string? PermissionState { get => _permissionState; set => _permissionState = value; }
        public Type? PermissionType { get => _permissionType; set => _permissionType = value; }
        public object? PermitOnlySetInstance { get => _permitOnlySetInstance; set => _permitOnlySetInstance = value; }
        public string? RefusedSet { get => _refusedSet; set => _refusedSet = value; }
        public string? Url { get => _url; set => _url = value; }
        public System.Reflection.AssemblyName? FailedAssemblyInfo { get => _failedAssemblyInfo; set => _failedAssemblyInfo = value; }
        public System.Reflection.MethodInfo? Method { get => _method; set => _method = value; }

        public override string ToString()
        {
            return base.ToString();
        }
    }

    [Serializable]
    public class VerificationException : SystemException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected VerificationException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("VerificationException formatter deserialization is not available on this NetWasm platform.");
        public VerificationException() => HResult = HResults.COR_E_VERIFICATION; public VerificationException(string? message) : base(message) => HResult = HResults.COR_E_VERIFICATION; public VerificationException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_VERIFICATION;
    }
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
        // NetWasm records a stack trace when an exception is thrown; one created and returned unthrown
        // keeps none, as upstream's would before it is thrown.
        public static System.Exception SetCurrentStackTrace(System.Exception source)
        {
            ArgumentNullException.ThrowIfNull(source);
            return source;
        }

        public static ExceptionDispatchInfo Capture(System.Exception sourceException) =>
            sourceException is null ? throw new System.ArgumentNullException(nameof(sourceException)) : new(sourceException);
        public void Throw() => InternalThrow(_sourceException);
        public static void Throw(System.Exception sourceException) =>
            InternalThrow(sourceException ?? throw new System.ArgumentNullException(
                nameof(sourceException)));

        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.InternalCall)]
        private static extern void InternalThrow(System.Exception sourceException);
    }
}

namespace System.Collections.Generic
{
    [Serializable]
    public class KeyNotFoundException : SystemException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected KeyNotFoundException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("KeyNotFoundException formatter deserialization is not available on this NetWasm platform.");

        public KeyNotFoundException() => HResult = HResults.COR_E_KEYNOTFOUND;
        public KeyNotFoundException(string? message) : base(message) => HResult = HResults.COR_E_KEYNOTFOUND;
        public KeyNotFoundException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_KEYNOTFOUND;
    }
}
