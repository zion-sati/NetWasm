namespace System.Runtime.InteropServices
{
    using WebAssembly;

    internal sealed class WasiStreamBackend : IPlatformStreams
    {
        private readonly IPlatformOutputStreamHandleSource _handles;
        private readonly IPlatformOutputStreamFactory _outputFactory;

        internal WasiStreamBackend(
            IPlatformOutputStreamHandleSource handles,
            IPlatformOutputStreamFactory outputFactory)
        {
            _handles = handles ?? throw new ArgumentNullException();
            _outputFactory = outputFactory ?? throw new ArgumentNullException();
        }

        public IPlatformOutputStream GetStandardOutput() =>
            _outputFactory.Create(_handles.GetStandardOutput());

        public IPlatformOutputStream GetStandardError() =>
            _outputFactory.Create(_handles.GetStandardError());
    }

    internal sealed class WasiOutputStreamFactory : IPlatformOutputStreamFactory
    {
        private readonly IPlatformOutputReadinessSource _readiness;
        private readonly IPlatformOutputWriterSource _writer;
        private readonly IPlatformBlockingOutputWriterSource _blockingWriter;
        private readonly IPlatformOutputResourceSource _resource;

        internal WasiOutputStreamFactory(
            IPlatformOutputReadinessSource readiness,
            IPlatformOutputWriterSource writer,
            IPlatformBlockingOutputWriterSource blockingWriter,
            IPlatformOutputResourceSource resource)
        {
            _readiness = readiness ?? throw new ArgumentNullException();
            _writer = writer ?? throw new ArgumentNullException();
            _blockingWriter = blockingWriter ?? throw new ArgumentNullException();
            _resource = resource ?? throw new ArgumentNullException();
        }

        public IPlatformOutputStream Create(int handle)
        {
            var resource = new WasiOutputStreamResource(handle, _resource);
            return new WasiOutputStream(
                new WasiOutputReadiness(resource, _readiness),
                new WasiOutputWriter(resource, _writer),
                new WasiBlockingOutputWriter(resource, _blockingWriter),
                resource);
        }
    }

    internal sealed class WasiOutputStream : IPlatformOutputStream
    {
        private readonly IPlatformOutputReadiness _readiness;
        private readonly IPlatformOutputWriter _writer;
        private readonly IPlatformBlockingOutputWriter _blockingWriter;
        private readonly IDisposable _lifetime;

        internal WasiOutputStream(
            IPlatformOutputReadiness readiness,
            IPlatformOutputWriter writer,
            IPlatformBlockingOutputWriter blockingWriter,
            IDisposable lifetime)
        {
            _readiness = readiness ?? throw new ArgumentNullException();
            _writer = writer ?? throw new ArgumentNullException();
            _blockingWriter = blockingWriter ?? throw new ArgumentNullException();
            _lifetime = lifetime ?? throw new ArgumentNullException();
        }

        ulong IPlatformOutputReadiness.CheckWrite() => _readiness.CheckWrite();

        void IPlatformOutputWriter.Write(byte[] contents) => _writer.Write(contents);

        void IPlatformBlockingOutputWriter.WriteBlocking(byte[] contents) =>
            _blockingWriter.WriteBlocking(contents);

        void IDisposable.Dispose() => _lifetime.Dispose();
    }

    internal sealed class WasiOutputStreamResource : IDisposable
    {
        private readonly IPlatformOutputResourceSource _resource;
        private int _handle;
        private bool _alive = true;

        internal WasiOutputStreamResource(
            int handle,
            IPlatformOutputResourceSource resource)
        {
            _resource = resource ?? throw new ArgumentNullException();
            _handle = handle;
        }

        ~WasiOutputStreamResource() => Release();

        internal int Handle
        {
            get
            {
                EnsureAlive();
                return _handle;
            }
        }

        public void Dispose()
        {
            Release();
            GC.SuppressFinalize(this);
        }

        private void EnsureAlive()
        {
            if (!_alive)
            {
                throw new ObjectDisposedException(null);
            }
        }

        private void Release()
        {
            if (!_alive)
            {
                return;
            }
            _alive = false;
            _resource?.Drop(_handle);
            _handle = 0;
        }
    }

    internal sealed class WasiOutputReadiness : IPlatformOutputReadiness
    {
        private readonly WasiOutputStreamResource _resource;
        private readonly IPlatformOutputReadinessSource _readiness;

        internal WasiOutputReadiness(
            WasiOutputStreamResource resource,
            IPlatformOutputReadinessSource readiness)
        {
            _resource = resource ?? throw new ArgumentNullException();
            _readiness = readiness ?? throw new ArgumentNullException();
        }

        public ulong CheckWrite() => _readiness.CheckWrite(_resource.Handle);
    }

    internal sealed class WasiOutputWriter : IPlatformOutputWriter
    {
        private readonly WasiOutputStreamResource _resource;
        private readonly IPlatformOutputWriterSource _writer;

        internal WasiOutputWriter(
            WasiOutputStreamResource resource,
            IPlatformOutputWriterSource writer)
        {
            _resource = resource ?? throw new ArgumentNullException();
            _writer = writer ?? throw new ArgumentNullException();
        }

        public void Write(byte[] contents)
        {
            if (contents == null)
            {
                throw new ArgumentNullException();
            }
            _writer.Write(_resource.Handle, contents);
        }
    }

    internal sealed class WasiOutputStreamHandleSource : IPlatformOutputStreamHandleSource
    {
        public int GetStandardOutput() => WasiStreamImports.GetStandardOutput();

        public int GetStandardError() => WasiStreamImports.GetStandardError();
    }

    internal sealed class WasiOutputReadinessSource : IPlatformOutputReadinessSource
    {
        private readonly IPlatformOutputErrorThrower _errorThrower;

        internal WasiOutputReadinessSource(IPlatformOutputErrorThrower errorThrower)
        {
            _errorThrower = errorThrower ?? throw new ArgumentNullException();
        }

        public ulong CheckWrite(int handle)
        {
            var result = CanonicalAbi.Allocate(16, 8);
            try
            {
                WasiStreamImports.CheckWrite(handle, result);
                if (CanonicalAbi.ReadByte(result, 0) != 0)
                {
                    _errorThrower.Throw(result, 8);
                }
                return unchecked((ulong)CanonicalAbi.ReadInt64(result, 8));
            }
            finally
            {
                CanonicalAbi.Free(result);
            }
        }
    }

    internal sealed class WasiOutputWriterSource : IPlatformOutputWriterSource
    {
        private readonly IPlatformOutputErrorThrower _errorThrower;

        internal WasiOutputWriterSource(IPlatformOutputErrorThrower errorThrower)
        {
            _errorThrower = errorThrower ?? throw new ArgumentNullException();
        }

        public void Write(int handle, byte[] contents)
        {
            if (contents == null)
            {
                throw new ArgumentNullException();
            }
            using var buffer = CanonicalAbi.AllocateElements(
                unchecked((nuint)contents.Length),
                1,
                1);
            for (var index = 0; index < contents.Length; index++)
            {
                CanonicalAbi.WriteByte(buffer.Address, unchecked((nuint)index), contents[index]);
            }

            var result = CanonicalAbi.Allocate(12, 4);
            try
            {
                WasiStreamImports.Write(
                    handle,
                    buffer.Address,
                    buffer.Length,
                    result);
                if (CanonicalAbi.ReadByte(result, 0) != 0)
                {
                    _errorThrower.Throw(result, 4);
                }
            }
            finally
            {
                CanonicalAbi.Free(result);
            }
        }
    }

    internal sealed class WasiOutputResourceSource : IPlatformOutputResourceSource
    {
        public void Drop(int handle) => WasiStreamImports.DropOutputStream(handle);
    }

    internal sealed class WasiOutputErrorThrower : IPlatformOutputErrorThrower
    {
        private readonly IPlatformOutputErrorResource _resource;

        internal WasiOutputErrorThrower(IPlatformOutputErrorResource resource)
        {
            _resource = resource ?? throw new ArgumentNullException();
        }

        public void Throw(nuint result, nuint payloadOffset)
        {
            var tag = CanonicalAbi.ReadByte(result, payloadOffset);
            if (tag == 0)
            {
                _resource.Drop(CanonicalAbi.ReadInt32(result, payloadOffset + 4));
            }
            else if (tag != 1)
            {
                throw new ArgumentException();
            }
            throw new InvalidOperationException();
        }
    }

    internal sealed class WasiOutputErrorResource : IPlatformOutputErrorResource
    {
        public void Drop(int handle) => WasiStreamImports.DropError(handle);
    }

}
