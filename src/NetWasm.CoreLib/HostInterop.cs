namespace System
{
    public sealed class JSException : Exception
    {
        public JSException()
        {
        }
    }
}

namespace System.Runtime.InteropServices.JavaScript
{
    public sealed class JSObject
    {
        private int _handle;

        internal JSObject()
        {
        }

        public void Dispose()
        {
            if (_handle != 0)
            {
                _handle = 0;
            }
        }
    }

    public sealed class JSSubscription
    {
        private int _handle;
        private int _callbackHandle;

        internal JSSubscription()
        {
        }

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
