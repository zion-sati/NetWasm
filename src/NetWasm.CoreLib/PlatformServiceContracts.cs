namespace System.Runtime.InteropServices.WebAssembly
{
    using System.Collections.Generic;

    // The platform a program runs on: the system services behind .NET's APIs, one facade per area.
    // A program has one platform, bound when it compiles. A component or JsModule runs on
    // WasiPlatform unless it declares its own with NetWasmPlatformAttribute; a plain core module
    // runs on a platform that provides nothing unless it declares one. A platform derives from
    // NetWasmPlatform and overrides the facades its host provides; how a facade is implemented is
    // its own business.
    public abstract class NetWasmPlatform
    {
        // Console's standard output, error and input, and whether each is a terminal.
        public virtual IStandardStreams StandardStreams => new UnsupportedStandardStreams();

        // DateTime.UtcNow, Stopwatch, Environment.TickCount64 and TimeProvider.
        public virtual IClock Clock => new UnsupportedClock();

        // RandomNumberGenerator, Guid.NewGuid and the seeding of Random.
        public virtual IRandomSource Random => new UnsupportedRandomSource();

        // The command line, the environment and the initial current directory.
        public virtual IProcessEnvironment ProcessEnvironment => new UnsupportedProcessEnvironment();

        // Environment.Exit, and a Main whose exit code is not zero.
        public virtual IProcessExit ProcessExit => new UnsupportedProcessExit();

        // Task.Delay, timers and the other delayed work of a program without the Component Model's
        // event loop. A program with WASI runs its timers on that loop and may not provide these.
        public virtual ITimerScheduler Timers => new UnsupportedTimerScheduler();

        // File, FileInfo, Directory, DirectoryInfo, FileStream and their enumeration.
        public virtual IFileSystem FileSystem => new UnsupportedFileSystem();

        // Socket, TcpClient, TcpListener, UdpClient, NetworkStream and Dns.
        public virtual INetwork Network => new UnsupportedNetwork();

        private sealed class UnsupportedStandardStreams : PlatformStandardStreams
        {
        }

        private sealed class UnsupportedClock : PlatformClock
        {
        }

        private sealed class UnsupportedRandomSource : PlatformRandomSource
        {
        }

        private sealed class UnsupportedProcessEnvironment : PlatformProcessEnvironment
        {
        }

        private sealed class UnsupportedProcessExit : PlatformProcessExit
        {
        }

        private sealed class UnsupportedTimerScheduler : PlatformTimerScheduler
        {
        }

        private sealed class UnsupportedFileSystem : PlatformFileSystem
        {
        }

        private sealed class UnsupportedNetwork : PlatformNetwork
        {
        }
    }

    // NetWasm's WASI 0.3 platform, the default for components and JsModule. A platform may derive
    // from it to replace or wrap a facade (base.FileSystem, say) and keep the rest. Each facade's
    // WASI calls are reached only when the program uses them.
    public class WasiPlatform : NetWasmPlatform
    {
        public override IStandardStreams StandardStreams => new WasiStandardStreams();

        public override IClock Clock => new WasiClock();

        public override IRandomSource Random =>
            new Cryptography.WasiCryptographicallySecureRandomByteFiller(new Cryptography.WasiRandomBytesInvoker());

        public override IProcessEnvironment ProcessEnvironment => new WasiProcessEnvironment();
        public override IProcessExit ProcessExit => new WasiProcessExit();

        public override IFileSystem FileSystem => new global::System.Runtime.InteropServices.FileSystem.WasiFileSystem();

        public override INetwork Network => global::System.Runtime.InteropServices.Network.WasiNetworkComposition.Create();
    }

    // Declares the program's platform: a public class deriving from NetWasmPlatform with a public
    // parameterless constructor. A program declares at most one.
    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class NetWasmPlatformAttribute : Attribute
    {
        public NetWasmPlatformAttribute(Type platform)
        {
            Platform = platform;
        }

        public Type Platform { get; }
    }

    // The standard streams a program can ask about.
    public enum StandardStream
    {
        Input,
        Output,
        Error,
    }

    public interface IStandardStreams
    {
        void WriteOutput(ReadOnlySpan<byte> bytes);

        void WriteError(ReadOnlySpan<byte> bytes);

        // Copies the next bytes of standard input into the destination, waiting until there is at
        // least one, and returns how many it copied: 0 only at the end of input or for an empty
        // destination.
        int ReadInput(Span<byte> destination);

        // Whether the stream is a terminal, rather than redirected from or to a file or a pipe.
        bool IsTerminal(StandardStream stream);
    }

    public interface IClock
    {
        // The current time, as a UTC DateTime.
        DateTime GetUtcNow();

        // Nanoseconds from an arbitrary start; never decreases.
        long GetTimestamp();
    }

    public interface IRandomSource
    {
        // Fills the destination with cryptographically secure random bytes.
        void Fill(Span<byte> destination);
    }

    public interface IProcessEnvironment
    {
        // The program's arguments, its name first.
        string[] GetArguments();

        // The environment the program starts with.
        KeyValuePair<string, string>[] GetVariables();

        // The absolute directory the program starts in, or null for the root.
        string? GetInitialDirectory();
    }

    public interface IProcessExit
    {
        // Ends the process with the exit code, zero for success; it does not return.
        void Exit(int exitCode);
    }

    public interface ITimerScheduler
    {
        // Calls the callback once the delay has passed, unless the returned schedule is disposed.
        IDisposable Schedule(Action callback, TimeSpan delay);
    }

    // The facades' bases: every member throws PlatformNotSupportedException until overridden, so a
    // platform implements only what its host supports.
    public abstract class PlatformStandardStreams : IStandardStreams
    {
        public virtual void WriteOutput(ReadOnlySpan<byte> bytes) => throw PlatformServiceErrors.Unavailable("Standard output", "StandardStreams");

        public virtual void WriteError(ReadOnlySpan<byte> bytes) => throw PlatformServiceErrors.Unavailable("Standard error", "StandardStreams");

        public virtual int ReadInput(Span<byte> destination) => throw PlatformServiceErrors.Unavailable("Standard input", "StandardStreams");

        public virtual bool IsTerminal(StandardStream stream) => throw PlatformServiceErrors.Unavailable("Terminal detection", "StandardStreams");
    }

    public abstract class PlatformClock : IClock
    {
        public virtual DateTime GetUtcNow() => throw PlatformServiceErrors.Unavailable("The system clock", "Clock");

        public virtual long GetTimestamp() => throw PlatformServiceErrors.Unavailable("The monotonic clock", "Clock");
    }

    public abstract class PlatformRandomSource : IRandomSource
    {
        public virtual void Fill(Span<byte> destination) => throw PlatformServiceErrors.Unavailable("Secure random bytes", "Random");
    }

    public abstract class PlatformProcessEnvironment : IProcessEnvironment
    {
        public virtual string[] GetArguments() => throw PlatformServiceErrors.Unavailable("The command line", "ProcessEnvironment");

        public virtual KeyValuePair<string, string>[] GetVariables() => throw PlatformServiceErrors.Unavailable("The environment", "ProcessEnvironment");

        public virtual string? GetInitialDirectory() => throw PlatformServiceErrors.Unavailable("The working directory", "ProcessEnvironment");
    }

    public abstract class PlatformProcessExit : IProcessExit
    {
        public virtual void Exit(int exitCode) => throw PlatformServiceErrors.Unavailable("Process exit", "ProcessExit");
    }

    public abstract class PlatformTimerScheduler : ITimerScheduler
    {
        public virtual IDisposable Schedule(Action callback, TimeSpan delay) => throw PlatformServiceErrors.Unavailable("A delay", "Timers");
    }

    internal static class PlatformServiceErrors
    {
        // Names the service, the program's platform and the facade that would provide it. The
        // NetWasm compiler supplies a declared platform's description.
        internal static PlatformNotSupportedException Unavailable(string service, string facade) => new(
            PlatformServiceBindings.DescribeDeclaredPlatform() is { } platform
                ? $"{service} is not available: the program's platform, {platform}, does not provide it (NetWasmPlatform.{facade})."
                : PlatformWorld.ProvidesWasi()
                    ? $"{service} is not available: WasiPlatform, the program's default platform, does not provide it (NetWasmPlatform.{facade})."
                    : $"{service} is not available: a plain core module has no WASI, so its default platform provides nothing. " +
                        $"Declare a platform with NetWasmPlatformAttribute whose {facade} provides it, typically over the world's imports.");
    }
}
