// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System
{
    public static class Environment
    {
        private static Runtime.InteropServices.TimeZones.EnvironmentVariable[]? s_environment;

        public static int CurrentManagedThreadId => 1;

        public static bool Is64BitProcess => IntPtr.Size == 8;

        // NetWasm currently supports a single managed execution thread.
        public static int ProcessorCount => 1;

        // Like .NET Core, NetWasm does not run finalizers during shutdown.
        public static bool HasShutdownStarted => false;

        public static string MachineName => throw new PlatformNotSupportedException();

        public static string UserName => throw new PlatformNotSupportedException();

        public static string UserDomainName => throw new PlatformNotSupportedException();

        public static int ProcessId => throw new PlatformNotSupportedException();

        public static bool Is64BitOperatingSystem => throw new PlatformNotSupportedException();

        public static string NewLine => "\n";

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
                if (candidate.Name != variable)
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
                result.Add(variable.Name, variable.Value);
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
                if (variables[candidate].Name == variable)
                {
                    index = candidate;
                    break;
                }
            }
            if (index >= 0)
            {
                if (value != null)
                {
                    variables[index] = new Runtime.InteropServices.TimeZones.EnvironmentVariable(
                        variable,
                        TrimStringOnFirstZero(value));
                    return;
                }
                var reduced = new Runtime.InteropServices.TimeZones.EnvironmentVariable[
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
            var expanded = new Runtime.InteropServices.TimeZones.EnvironmentVariable[
                variables.Length + 1];
            for (var source = 0; source < variables.Length; source++)
            {
                expanded[source] = variables[source];
            }
            expanded[variables.Length] = new Runtime.InteropServices.TimeZones.EnvironmentVariable(
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
            var variables = Runtime.InteropServices.WasiPlatformServicesComposition
                .CreateInitialEnvironmentVariableSource()
                .Read();
            s_environment = variables;
        }

        private static void ValidateNoDuplicateNames()
        {
            var variables = s_environment!;
            for (var current = 0; current < variables.Length; current++)
            {
                for (var previous = 0; previous < current; previous++)
                {
                    if (variables[previous].Name == variables[current].Name)
                    {
                        throw new PlatformNotSupportedException(
                            "WASI environment contains duplicate '" + variables[current].Name + "' entries.");
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

        public static long TickCount64 => unchecked((long)
            Runtime.InteropServices.PlatformServices.MonotonicClock
                .GetMonotonicTime() / 1_000_000);
    }
}
