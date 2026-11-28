// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.JavaScript
{
    // .NET's browser JSException (dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce,
    // src/libraries/System.Runtime.InteropServices.JavaScript/src/System/Runtime/InteropServices/JavaScript/JSException.cs),
    // holding the JavaScript error as a host handle where .NET holds a JSObject proxy.
    public sealed class JSException : Exception
    {
        // The JavaScript error this exception came from, which it gives back when it crosses to JavaScript again: what
        // a failed import threw or rejected with, or an error crossing as a value.
        private JSHandleOwner? _error;
        private string? _combinedStackTrace;

        public JSException(string msg)
            : base(msg)
        {
        }

        internal JSException(string message, int error)
            : base(message) => _error = new(error);

        // A failure the boundary found with no JavaScript value behind it, such as a result it refused.
        internal JSException()
        {
        }

        internal int Error => _error?.Handle ?? 0;

        // As .NET's: the managed frames, then the JavaScript error's stack, without the message a JavaScript engine
        // puts at its top. Reading it releases the error, as .NET disposes its proxy.
        public override string? StackTrace
        {
            get
            {
                if (_combinedStackTrace != null)
                {
                    return _combinedStackTrace;
                }
                var bs = base.StackTrace;
                if (_error == null)
                {
                    return bs;
                }
                string? jsStackTrace = JSHostError.Stack(_error.Handle);
                _error.Release();
                _error = null;
                if (jsStackTrace == null)
                {
                    _combinedStackTrace = bs;
                    return _combinedStackTrace;
                }
                else if (jsStackTrace.StartsWith(Message + "\n", StringComparison.Ordinal))
                {
                    jsStackTrace = jsStackTrace.Substring(Message.Length + 1);
                }
                _combinedStackTrace = bs != null
                    ? bs + Environment.NewLine + jsStackTrace
                    : jsStackTrace;
                return _combinedStackTrace;
            }
        }

        // As .NET's: exceptions holding the same JavaScript error are equal, and so are two that hold none.
        public override bool Equals(object? obj) =>
            obj is JSException other && (other._error == null
                ? _error == null
                : _error != null && JSHostError.Same(other._error.Handle, _error.Handle));

        public override int GetHashCode() =>
            _error == null ? base.GetHashCode() : base.GetHashCode() * _error.Handle;

        // As .NET's, which leaves out the expensive stack trace.
        public override string ToString() => Message;
    }

    public class JSObject : IDisposable
    {
        private int _handle;

        internal JSObject()
        {
        }

        // Interface dispatch needs an ordinary CIL body. Public Dispose is a
        // direct-call compiler intrinsic; its placeholder body does not release
        // the host handle. Keep this forwarding method separate from it.
        void IDisposable.Dispose() => Dispose();

        public void Dispose()
        {
            if (_handle != 0)
            {
                _handle = 0;
            }
        }

        // Dispose releases the host handle, and a live object always has one.
        public bool IsDisposed => _handle == 0;

        public bool HasProperty(string propertyName)
        {
            AssertNotDisposed();
            return JSObjectProperty.Has(this, propertyName);
        }

        public string GetTypeOfProperty(string propertyName)
        {
            AssertNotDisposed();
            return JSObjectProperty.TypeOf(this, propertyName);
        }

        public bool GetPropertyAsBoolean(string propertyName)
        {
            AssertNotDisposed();
            return JSObjectProperty.GetBoolean(this, propertyName);
        }

        public int GetPropertyAsInt32(string propertyName)
        {
            AssertNotDisposed();
            return JSObjectProperty.GetInt32(this, propertyName);
        }

        public double GetPropertyAsDouble(string propertyName)
        {
            AssertNotDisposed();
            return JSObjectProperty.GetDouble(this, propertyName);
        }

        public string? GetPropertyAsString(string propertyName)
        {
            AssertNotDisposed();
            return JSObjectProperty.GetString(this, propertyName);
        }

        public JSObject? GetPropertyAsJSObject(string propertyName)
        {
            AssertNotDisposed();
            return JSObjectProperty.GetObject(this, propertyName);
        }

        public byte[]? GetPropertyAsByteArray(string propertyName)
        {
            AssertNotDisposed();
            return JSObjectProperty.GetBytes(this, propertyName);
        }

        public void SetProperty(string propertyName, bool value)
        {
            AssertNotDisposed();
            JSObjectProperty.SetBoolean(this, propertyName, value);
        }

        public void SetProperty(string propertyName, int value)
        {
            AssertNotDisposed();
            JSObjectProperty.SetInt32(this, propertyName, value);
        }

        public void SetProperty(string propertyName, double value)
        {
            AssertNotDisposed();
            JSObjectProperty.SetDouble(this, propertyName, value);
        }

        public void SetProperty(string propertyName, string? value)
        {
            AssertNotDisposed();
            JSObjectProperty.SetString(this, propertyName, value);
        }

        public void SetProperty(string propertyName, JSObject? value)
        {
            AssertNotDisposed();
            JSObjectProperty.SetObject(this, propertyName, value);
        }

        public void SetProperty(string propertyName, byte[]? value)
        {
            AssertNotDisposed();
            JSObjectProperty.SetBytes(this, propertyName, value);
        }

        private void AssertNotDisposed() => ObjectDisposedException.ThrowIf(IsDisposed, this);
    }

    // A JavaScript object's properties, read and written as .NET's browser runtime does: a missing property reads as
    // undefined, which is a scalar's default and null otherwise. Served by the runtime's own import module.
    internal static class JSObjectProperty
    {
        private const string Module = "netwasm.host.v1";

        [JSImport("object_has_property", Module)]
        internal static extern bool Has(JSObject self, string propertyName);

        [JSImport("object_type_of_property", Module)]
        internal static extern string TypeOf(JSObject self, string propertyName);

        [JSImport("object_get_boolean", Module)]
        internal static extern bool GetBoolean(JSObject self, string propertyName);

        [JSImport("object_get_int32", Module)]
        internal static extern int GetInt32(JSObject self, string propertyName);

        [JSImport("object_get_double", Module)]
        internal static extern double GetDouble(JSObject self, string propertyName);

        [JSImport("object_get_string", Module)]
        internal static extern string? GetString(JSObject self, string propertyName);

        [JSImport("object_get_object", Module)]
        internal static extern JSObject? GetObject(JSObject self, string propertyName);

        [JSImport("object_get_bytes", Module)]
        internal static extern byte[]? GetBytes(JSObject self, string propertyName);

        [JSImport("object_set_boolean", Module)]
        internal static extern void SetBoolean(JSObject self, string propertyName, bool value);

        [JSImport("object_set_int32", Module)]
        internal static extern void SetInt32(JSObject self, string propertyName, int value);

        [JSImport("object_set_double", Module)]
        internal static extern void SetDouble(JSObject self, string propertyName, double value);

        [JSImport("object_set_string", Module)]
        internal static extern void SetString(JSObject self, string propertyName, string? value);

        [JSImport("object_set_object", Module)]
        internal static extern void SetObject(JSObject self, string propertyName, JSObject? value);

        [JSImport("object_set_bytes", Module)]
        internal static extern void SetBytes(JSObject self, string propertyName, byte[]? value);
    }

    public sealed class JSSubscription : IDisposable
    {
        private int _handle;
        private int _callbackHandle;

        internal JSSubscription()
        {
        }

        // Route using/interface/generic disposal through the same intrinsic as
        // direct calls, including host subscription and callback-root release.
        void IDisposable.Dispose() => Dispose();

        public void Dispose()
        {
            if (_handle != 0 || _callbackHandle != 0)
            {
                _handle = 0;
                _callbackHandle = 0;
            }
        }
    }

    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class JSImportAttribute : Attribute
    {
        public JSImportAttribute(string functionName)
        {
            FunctionName = functionName;
        }

        public JSImportAttribute(string functionName, string moduleName)
        {
            FunctionName = functionName;
            ModuleName = moduleName;
        }

        public string FunctionName { get; }
        public string? ModuleName { get; }
    }

    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class JSExportAttribute : Attribute
    {
        public JSExportAttribute()
        {
        }

        public JSExportAttribute(string exportName)
        {
            ExportName = exportName;
        }

        public string? ExportName { get; }
    }

    // Names how a parameter or result crosses to JavaScript, as on .NET's browser
    // runtime. NetWasm needs it only where there is a choice, a long as a Number
    // rather than a BigInt; elsewhere it is checked against the managed type.
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.ReturnValue, Inherited = false, AllowMultiple = false)]
    public sealed class JSMarshalAsAttribute<T> : Attribute
        where T : JSType
    {
        public JSMarshalAsAttribute()
        {
        }
    }

    // The JavaScript types JSMarshalAsAttribute<T> names.
    public abstract class JSType
    {
        internal JSType()
        {
        }

        public sealed class Void : JSType
        {
            internal Void()
            {
            }
        }

        public sealed class Discard : JSType
        {
            internal Discard()
            {
            }
        }

        public sealed class DiscardNoWait : JSType
        {
            internal DiscardNoWait()
            {
            }
        }

        public sealed class Boolean : JSType
        {
            internal Boolean()
            {
            }
        }

        public sealed class Number : JSType
        {
            internal Number()
            {
            }
        }

        public sealed class BigInt : JSType
        {
            internal BigInt()
            {
            }
        }

        public sealed class Date : JSType
        {
            internal Date()
            {
            }
        }

        public sealed class String : JSType
        {
            internal String()
            {
            }
        }

        public sealed class Object : JSType
        {
            internal Object()
            {
            }
        }

        public sealed class Error : JSType
        {
            internal Error()
            {
            }
        }

        public sealed class MemoryView : JSType
        {
            internal MemoryView()
            {
            }
        }

        public sealed class Array<T> : JSType
            where T : JSType
        {
            internal Array()
            {
            }
        }

        public sealed class Promise<T> : JSType
            where T : JSType
        {
            internal Promise()
            {
            }
        }

        public sealed class Function : JSType
        {
            internal Function()
            {
            }
        }

        public sealed class Function<T> : JSType
            where T : JSType
        {
            internal Function()
            {
            }
        }

        public sealed class Function<T1, T2> : JSType
            where T1 : JSType
            where T2 : JSType
        {
            internal Function()
            {
            }
        }

        public sealed class Function<T1, T2, T3> : JSType
            where T1 : JSType
            where T2 : JSType
            where T3 : JSType
        {
            internal Function()
            {
            }
        }

        public sealed class Function<T1, T2, T3, T4> : JSType
            where T1 : JSType
            where T2 : JSType
            where T3 : JSType
            where T4 : JSType
        {
            internal Function()
            {
            }
        }

        public sealed class Any : JSType
        {
            internal Any()
            {
            }
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class JSImportPromiseAttribute : Attribute
    {
        public JSImportPromiseAttribute(string functionName)
        {
            FunctionName = functionName;
        }

        public JSImportPromiseAttribute(string functionName, string moduleName)
        {
            FunctionName = functionName;
            ModuleName = moduleName;
        }

        public string FunctionName { get; }
        public string? ModuleName { get; }
    }

    public sealed class JSPromiseTaskSource
    {
        private readonly Threading.Tasks.TaskCompletionSource _completion = new();
        private JSSubscription? _subscription;

        public Threading.Tasks.Task Task => _completion.Task;

        public void SetSubscription(JSSubscription subscription) =>
            _subscription = subscription;

        public void Resolve()
        {
            Complete();
            _completion.SetResult();
        }

        public void Reject()
        {
            Complete();
            _completion.SetException(JSHostError.TakeFailure());
        }

        private void Complete()
        {
            _subscription!.Dispose();
            _subscription = null;
        }
    }

    public sealed class JSPromiseTaskSource<T>
    {
        private readonly Threading.Tasks.TaskCompletionSource<T> _completion = new();
        private JSSubscription? _subscription;

        public Threading.Tasks.Task<T> Task => _completion.Task;

        public void SetSubscription(JSSubscription subscription) =>
            _subscription = subscription;

        public void Resolve(T result)
        {
            Complete();
            _completion.SetResult(result);
        }

        public void Reject()
        {
            Complete();
            _completion.SetException(JSHostError.TakeFailure());
        }

        private void Complete()
        {
            _subscription!.Dispose();
            _subscription = null;
        }
    }
}
