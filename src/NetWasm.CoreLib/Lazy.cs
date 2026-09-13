// Portions derived from dotnet/runtime System.Private.CoreLib at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.ExceptionServices;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace System.Threading
{
    // Thread-safety modes are retained for source and metadata compatibility.
    // NetWasm executes managed code on one reactor thread, so no monitor or
    // atomic publication path is needed by the implementation.
    public enum LazyThreadSafetyMode
    {
        None = 0,
        PublicationOnly = 1,
        ExecutionAndPublication = 2,
    }
}

namespace System
{
    public class Lazy<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>
    {
        private readonly LazyThreadSafetyMode _mode;
        private Func<T>? _factory;
        private T? _value;
        private ExceptionDispatchInfo? _exception;
        private bool _isValueCreated;
        private bool _initializing;

        public Lazy()
            : this(null, LazyThreadSafetyMode.ExecutionAndPublication, true)
        {
        }

        public Lazy(T value)
        {
            _value = value;
            _isValueCreated = true;
            _mode = LazyThreadSafetyMode.ExecutionAndPublication;
        }

        public Lazy(Func<T> valueFactory)
            : this(valueFactory, LazyThreadSafetyMode.ExecutionAndPublication, false)
        {
        }

        public Lazy(bool isThreadSafe)
            : this(null, isThreadSafe ? LazyThreadSafetyMode.ExecutionAndPublication : LazyThreadSafetyMode.None, true)
        {
        }

        public Lazy(LazyThreadSafetyMode mode)
            : this(null, mode, true)
        {
        }

        public Lazy(Func<T> valueFactory, bool isThreadSafe)
            : this(valueFactory, isThreadSafe ? LazyThreadSafetyMode.ExecutionAndPublication : LazyThreadSafetyMode.None, false)
        {
        }

        public Lazy(Func<T> valueFactory, LazyThreadSafetyMode mode)
            : this(valueFactory, mode, false)
        {
        }

        private Lazy(Func<T>? valueFactory, LazyThreadSafetyMode mode, bool useDefaultConstructor)
        {
            if (mode is < LazyThreadSafetyMode.None or > LazyThreadSafetyMode.ExecutionAndPublication)
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }
            if (!useDefaultConstructor && valueFactory is null)
            {
                throw new ArgumentNullException(nameof(valueFactory));
            }

            _factory = valueFactory;
            _mode = mode;
        }

        public bool IsValueCreated => _isValueCreated;

        public T Value
        {
            get
            {
                if (_isValueCreated)
                {
                    return _value!;
                }
                if (_exception is not null)
                {
                    _exception.Throw();
                }
                return CreateValue();
            }
        }

        private T CreateValue()
        {
            if (_initializing)
            {
                var recursive = new InvalidOperationException("ValueFactory attempted to access the Value property of this instance.");
                if (_mode != LazyThreadSafetyMode.PublicationOnly)
                {
                    _exception = ExceptionDispatchInfo.Capture(recursive);
                }
                throw recursive;
            }

            _initializing = true;
            var factory = _factory;
            try
            {
                var value = factory is null ? Activator.CreateInstance<T>() : factory();
                // Activator is intentionally reflection-free in the current
                // CoreLib profile. Value types can still use their zero value;
                // reference-type default construction needs the missing
                // reflection/runtime constructor capability.
                if (factory is null && value is null)
                {
                    throw new PlatformNotSupportedException(
                        "Lazy<T> default construction requires runtime constructor activation.");
                }
                _value = value;
                _isValueCreated = true;
                _factory = null;
                return value;
            }
            catch (Exception exception)
            {
                if (_mode != LazyThreadSafetyMode.PublicationOnly)
                {
                    _factory = null;
                    _exception = ExceptionDispatchInfo.Capture(exception);
                }
                throw;
            }
            finally
            {
                _initializing = false;
            }
        }

        public override string ToString() => IsValueCreated
            ? Value!.ToString() ?? string.Empty
            : "Value is not created.";
    }

    public class Lazy<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T, TMetadata> : Lazy<T>
    {
        private readonly TMetadata _metadata;

        public Lazy(Func<T> valueFactory, TMetadata metadata)
            : base(valueFactory) => _metadata = metadata;

        public Lazy(TMetadata metadata)
            : base() => _metadata = metadata;

        public Lazy(TMetadata metadata, bool isThreadSafe)
            : base(isThreadSafe) => _metadata = metadata;

        public Lazy(Func<T> valueFactory, TMetadata metadata, bool isThreadSafe)
            : base(valueFactory, isThreadSafe) => _metadata = metadata;

        public Lazy(TMetadata metadata, LazyThreadSafetyMode mode)
            : base(mode) => _metadata = metadata;

        public Lazy(Func<T> valueFactory, TMetadata metadata, LazyThreadSafetyMode mode)
            : base(valueFactory, mode) => _metadata = metadata;

        public TMetadata Metadata => _metadata;
    }
}
