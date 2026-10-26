namespace System
{
    public sealed class JSException : Exception
    {
        // A JavaScript error this exception came from, which it gives back when it
        // crosses to JavaScript again. Only an error crossing as a value has one, so
        // an exception from a failed import pays for no finalizer.
        private readonly System.Runtime.InteropServices.JavaScript.JSHandleOwner? _error;

        public JSException()
        {
        }

        public JSException(string msg)
            : base(msg)
        {
        }

        internal JSException(string message, int error)
            : base(message) => _error = new(error);

        internal int Error => _error?.Handle ?? 0;
    }
}

namespace System.Runtime.InteropServices.JavaScript
{
    public sealed class JSObject : IDisposable
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

    [AttributeUsage(AttributeTargets.Method)]
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

    [AttributeUsage(AttributeTargets.Method)]
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
            _completion.SetException(new JSException());
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
            _completion.SetException(new JSException());
        }

        private void Complete()
        {
            _subscription!.Dispose();
            _subscription = null;
        }
    }
}
