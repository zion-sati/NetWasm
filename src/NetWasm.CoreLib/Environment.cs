// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System
{
    public static partial class Environment
    {
        private static Collections.Generic.KeyValuePair<string, string>[]? s_environment;

        public static int CurrentManagedThreadId => 1;

        public static bool Is64BitProcess => IntPtr.Size == 8;

        // NetWasm currently supports a single managed execution thread.
        public static int ProcessorCount => 1;

        // As upstream's Environment.cs.
        internal static bool IsSingleProcessor => ProcessorCount == 1;

        private static string? s_currentDirectory;

        // Starts as the host's initial directory (wasi:cli's get-initial-cwd), or the root when it
        // has none. Setting it resolves the path and requires a directory, as chdir does.
        public static string CurrentDirectory
        {
            get => s_currentDirectory ??= InitialDirectory();
            set
            {
                ArgumentException.ThrowIfNullOrEmpty(value);
                var fullPath = IO.Path.GetFullPath(value);
                if (Interop.Sys.Stat(fullPath, out var status) < 0)
                {
                    throw Interop.GetExceptionForIoErrno(Interop.Sys.GetLastErrorInfo(), value, isDirError: true);
                }

                if ((status.Mode & Interop.Sys.FileTypes.S_IFMT) != Interop.Sys.FileTypes.S_IFDIR)
                {
                    throw Interop.GetExceptionForIoErrno(Interop.Error.ENOTDIR.Info(), value, isDirError: true);
                }

                s_currentDirectory = IO.Path.TrimEndingDirectorySeparator(fullPath);
            }
        }

        private static string InitialDirectory()
        {
            var directory = Runtime.InteropServices.PlatformServices.ProcessEnvironment.GetInitialDirectory();
            return string.IsNullOrEmpty(directory) || directory[0] != '/'
                ? "/"
                : IO.Path.TrimEndingDirectorySeparator(directory);
        }

        // Like .NET Core, NetWasm does not run finalizers during shutdown.
        public static bool HasShutdownStarted => false;

        // The process's exit code once Main returns, unless an int Main returns its own.
        public static int ExitCode { get; set; }

        // Ends the process with the exit code, whatever it is, through the program's platform.
        [Diagnostics.CodeAnalysis.DoesNotReturn]
        public static void Exit(int exitCode) =>
            Runtime.InteropServices.PlatformServices.ExitProcess(exitCode);

        [Diagnostics.CodeAnalysis.DoesNotReturn]
        public static void FailFast(string? message) => FailFast(message, null);

        // .NET's report of a fail-fast on standard error, without the call stack NetWasm does not
        // capture: the message, or .NET's text for none, then the exception.
        [Diagnostics.CodeAnalysis.DoesNotReturn]
        public static void FailFast(string? message, Exception? exception) =>
            Runtime.InteropServices.PlatformServices.FailProcess(
                "Process terminated." + NewLineConst +
                (message ?? "Runtime operation halted by call to System.Environment.FailFast(). (0x80131623)") +
                NewLineConst + (exception is null ? string.Empty : exception + NewLineConst));

        // A guest has no host name of its own; upstream's browser build answers the same.
        public static string MachineName => "localhost";

        /// <summary>The time the process has spent on the CPU, as upstream's (.NET 9).</summary>
        public readonly struct ProcessCpuUsage
        {
            public TimeSpan UserTime { get; internal init; }

            public TimeSpan PrivilegedTime { get; internal init; }

            public TimeSpan TotalTime => UserTime + PrivilegedTime;
        }

        // WASI has no process CPU clock; upstream marks this member unsupported on browser.
        public static ProcessCpuUsage CpuUsage => throw new PlatformNotSupportedException(
            "Process CPU usage is not available on WASI.");

        // The guest has no host user or process identity, so these name themselves rather than invent one.
        public static string UserName => throw new PlatformNotSupportedException(
            "Environment.UserName is not available on NetWasm: the guest has no host user.");

        public static string UserDomainName => throw new PlatformNotSupportedException(
            "Environment.UserDomainName is not available on NetWasm: the guest has no host user.");

        public static int ProcessId => throw new PlatformNotSupportedException(
            "Environment.ProcessId is not available on NetWasm: the guest has no host process.");

        // Upstream's on Unix, browser and WASI (Environment.UnixOrBrowser.cs): a 32-bit process has no 64-bit host.
        public static bool Is64BitOperatingSystem => Is64BitProcess;

        // Environment.UnixOrBrowser.cs: WASI and the browser use Unix line endings.
        internal const string NewLineConst = "\n";

        public static string NewLine => NewLineConst;

        public static string[] GetCommandLineArgs() =>
            Runtime.InteropServices.PlatformServices.ReadCommandLineArguments();

        public static string? GetEnvironmentVariable(string variable)
        {
            ArgumentNullException.ThrowIfNull(variable);
            variable = TrimStringOnFirstZero(variable);
            EnsureEnvironmentCached();
            string? selected = null;
            foreach (var candidate in s_environment!)
            {
                if (candidate.Key != variable)
                {
                    continue;
                }
                if (selected != null)
                {
                    throw new PlatformNotSupportedException(
                        "WASI environment contains duplicate '" + variable + "' entries.");
                }
                selected = candidate.Value;
            }
            return selected;
        }

        public static Collections.IDictionary GetEnvironmentVariables()
        {
            EnsureEnvironmentCached();
            ValidateNoDuplicateNames();
            var result = new Collections.Hashtable();
            foreach (var variable in s_environment!)
            {
                result.Add(variable.Key, variable.Value);
            }
            return result;
        }

        public static void SetEnvironmentVariable(string variable, string? value)
        {
            ValidateVariable(variable);
            EnsureEnvironmentCached();
            ValidateNoDuplicateNames();
            variable = TrimStringOnFirstZero(variable);
            var variables = s_environment!;
            var index = -1;
            for (var candidate = 0; candidate < variables.Length; candidate++)
            {
                if (variables[candidate].Key == variable)
                {
                    index = candidate;
                    break;
                }
            }
            if (index >= 0)
            {
                if (value != null)
                {
                    variables[index] = new Collections.Generic.KeyValuePair<string, string>(
                        variable,
                        TrimStringOnFirstZero(value));
                    return;
                }
                var reduced = new Collections.Generic.KeyValuePair<string, string>[
                    variables.Length - 1];
                for (var source = 0; source < index; source++)
                {
                    reduced[source] = variables[source];
                }
                for (var source = index + 1; source < variables.Length; source++)
                {
                    reduced[source - 1] = variables[source];
                }
                s_environment = reduced;
                return;
            }
            if (value == null)
            {
                return;
            }
            var expanded = new Collections.Generic.KeyValuePair<string, string>[
                variables.Length + 1];
            for (var source = 0; source < variables.Length; source++)
            {
                expanded[source] = variables[source];
            }
            expanded[variables.Length] = new Collections.Generic.KeyValuePair<string, string>(
                variable,
                TrimStringOnFirstZero(value));
            s_environment = expanded;
        }

        public static string? GetEnvironmentVariable(string variable, EnvironmentVariableTarget target)
        {
            ArgumentNullException.ThrowIfNull(variable);
            ValidateTarget(target);
            return GetEnvironmentVariable(variable);
        }

        public static Collections.IDictionary GetEnvironmentVariables(EnvironmentVariableTarget target)
        {
            ValidateTarget(target);
            return GetEnvironmentVariables();
        }

        public static void SetEnvironmentVariable(string variable, string? value, EnvironmentVariableTarget target)
        {
            ValidateVariable(variable);
            ValidateTarget(target);
            SetEnvironmentVariable(variable, value);
        }

        private static void ValidateTarget(EnvironmentVariableTarget target)
        {
            if (target == EnvironmentVariableTarget.Process)
            {
                return;
            }
            if (target is EnvironmentVariableTarget.User or EnvironmentVariableTarget.Machine)
            {
                throw new PlatformNotSupportedException();
            }
            throw new ArgumentOutOfRangeException(nameof(target));
        }

        private static void ValidateVariable(string variable)
        {
            ArgumentException.ThrowIfNullOrEmpty(variable);
            if (variable[0] == '\0' || variable.IndexOf('=') >= 0)
            {
                throw new ArgumentException("Invalid environment variable name.", nameof(variable));
            }
        }

        private static string TrimStringOnFirstZero(string value)
        {
            var index = value.IndexOf('\0');
            return index >= 0 ? value.Substring(0, index) : value;
        }

        private static void EnsureEnvironmentCached()
        {
            if (s_environment != null)
            {
                return;
            }
            var variables = Runtime.InteropServices.PlatformServices.ProcessEnvironment.GetVariables() ??
                throw new InvalidOperationException("The environment service returned no variables.");
            foreach (var variable in variables)
            {
                if (variable.Key is null || variable.Value is null)
                {
                    throw new InvalidOperationException(
                        "The environment service returned a variable without a name or value.");
                }
            }
            s_environment = variables;
        }

        private static void ValidateNoDuplicateNames()
        {
            var variables = s_environment!;
            for (var current = 0; current < variables.Length; current++)
            {
                for (var previous = 0; previous < current; previous++)
                {
                    if (variables[previous].Key == variables[current].Key)
                    {
                        throw new PlatformNotSupportedException(
                            "WASI environment contains duplicate '" + variables[current].Key + "' entries.");
                    }
                }
            }
        }

        public static string ExpandEnvironmentVariables(string name)
        {
            ArgumentNullException.ThrowIfNull(name);
            if (name.Length == 0)
            {
                return name;
            }

            // Preserve the upstream Unix expansion algorithm, using the
            // available managed builder instead of ValueStringBuilder.
            var result = new Text.StringBuilder();
            var lastPos = 0;
            var pos = 0;
            while (lastPos < name.Length && (pos = name.IndexOf('%', lastPos + 1)) >= 0)
            {
                if (name[lastPos] == '%')
                {
                    var key = name.Substring(lastPos + 1, pos - lastPos - 1);
                    var value = GetEnvironmentVariable(key);
                    if (value != null)
                    {
                        result.Append(value);
                        lastPos = pos + 1;
                        continue;
                    }
                }
                result.Append(name.AsSpan(lastPos, pos - lastPos));
                lastPos = pos;
            }
            result.Append(name.AsSpan(lastPos));
            return result.ToString();
        }

        public static int TickCount => unchecked((int)TickCount64);

        public static long TickCount64 =>
            Runtime.InteropServices.PlatformServices.Clock.GetTimestamp() / 1_000_000;
    }
}
