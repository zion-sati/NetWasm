// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// NetWasm adaptation: only the .NET Standard 2.1 members are ported. The newer launch settings
// (the IEnumerable<string> constructor, StartDetached, StartSuspended, the standard-handle
// properties, InheritedHandles and KillOnParentExit) and their ThrowIfInvalid checks are omitted.
// UseShellExecute stores its value: upstream's setter prepares the shell-execute launcher, and
// NetWasm has no launcher (Process.Start throws PlatformNotSupportedException). BuildArguments,
// which only the launcher used, is omitted, so the debugger display shows Arguments.

using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace System.Diagnostics
{
    /// <devdoc>
    ///     A set of values used to specify a process to start.  This is
    ///     used in conjunction with the <see cref='System.Diagnostics.Process'/>
    ///     component.
    /// </devdoc>
    [DebuggerDisplay("FileName = {FileName}, Arguments = {Arguments}, WorkingDirectory = {WorkingDirectory}")]
    public sealed partial class ProcessStartInfo
    {
        private string? _fileName;
        private string? _arguments;
        private string? _directory;
        private string? _userName;
        private string? _verb;
        private Collection<string>? _argumentList;
        private ProcessWindowStyle _windowStyle;

        internal DictionaryWrapper? _environmentVariables;

        /// <devdoc>
        ///     Default constructor.  At least the <see cref='System.Diagnostics.ProcessStartInfo.FileName'/>
        ///     property must be set before starting the process.
        /// </devdoc>
        public ProcessStartInfo()
        {
        }

        /// <devdoc>
        ///     Specifies the name of the application or document that is to be started.
        /// </devdoc>
        public ProcessStartInfo(string fileName)
        {
            _fileName = fileName;
        }

        /// <devdoc>
        ///     Specifies the name of the application that is to be started, as well as a set
        ///     of command line arguments to pass to the application.
        /// </devdoc>
        public ProcessStartInfo(string fileName, string? arguments)
        {
            _fileName = fileName;
            _arguments = arguments;
        }

        /// <devdoc>
        ///     Specifies the set of command line arguments to use when starting the application.
        /// </devdoc>
        [AllowNull]
        public string Arguments
        {
            get => _arguments ?? string.Empty;
            set => _arguments = value;
        }

        public Collection<string> ArgumentList => _argumentList ??= new Collection<string>();

        internal bool HasArgumentList => _argumentList is not null && _argumentList.Count != 0;

        public bool CreateNoWindow { get; set; }

        [Editor("System.Diagnostics.Design.StringDictionaryEditor, System.Design, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a",
                "System.Drawing.Design.UITypeEditor, System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a")]
        public StringDictionary EnvironmentVariables => new StringDictionaryWrapper((Environment as DictionaryWrapper)!);

        public IDictionary<string, string?> Environment
        {
            get
            {
                if (_environmentVariables == null)
                {
                    IDictionary envVars = System.Environment.GetEnvironmentVariables();

                    _environmentVariables = new DictionaryWrapper(new Dictionary<string, string?>(
                        envVars.Count,
                        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal));

                    // Manual use of IDictionaryEnumerator instead of foreach to avoid DictionaryEntry box allocations.
                    IDictionaryEnumerator e = envVars.GetEnumerator();
                    Debug.Assert(!(e is IDisposable), "Environment.GetEnvironmentVariables should not be IDisposable.");
                    while (e.MoveNext())
                    {
                        DictionaryEntry entry = e.Entry;
                        _environmentVariables.Add((string)entry.Key, (string?)entry.Value);
                    }
                }
                return _environmentVariables;
            }
        }

        public bool RedirectStandardInput { get; set; }
        public bool RedirectStandardOutput { get; set; }
        public bool RedirectStandardError { get; set; }

        public Encoding? StandardInputEncoding { get; set; }

        public Encoding? StandardErrorEncoding { get; set; }

        public Encoding? StandardOutputEncoding { get; set; }

        /// <devdoc>
        ///    <para>
        ///       Returns or sets the application, document, or URL that is to be launched.
        ///    </para>
        /// </devdoc>
        [Editor("System.Diagnostics.Design.StartFileNameEditor, System.Design, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a",
                "System.Drawing.Design.UITypeEditor, System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a")]
        [AllowNull]
        public string FileName
        {
            get => _fileName ?? string.Empty;
            set => _fileName = value;
        }

        /// <devdoc>
        ///     Returns or sets the initial directory for the process that is started.
        ///     Specify "" to if the default is desired.
        /// </devdoc>
        [Editor("System.Diagnostics.Design.WorkingDirectoryEditor, System.Design, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a",
                "System.Drawing.Design.UITypeEditor, System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a")]
        [AllowNull]
        public string WorkingDirectory
        {
            get => _directory ?? string.Empty;
            set => _directory = value;
        }

        public bool ErrorDialog { get; set; }
        public IntPtr ErrorDialogParentHandle { get; set; }

        public bool UseShellExecute { get; set; }

        [AllowNull]
        public string UserName
        {
            get => _userName ?? string.Empty;
            set => _userName = value;
        }

        [DefaultValue("")]
        [AllowNull]
        public string Verb
        {
            get => _verb ?? string.Empty;
            set => _verb = value;
        }

        [DefaultValueAttribute(System.Diagnostics.ProcessWindowStyle.Normal)]
        public ProcessWindowStyle WindowStyle
        {
            get => _windowStyle;
            set
            {
                if (!Enum.IsDefined(value))
                {
                    throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(ProcessWindowStyle));
                }

                _windowStyle = value;
            }
        }

        internal void ThrowIfInvalid(out bool anyRedirection)
        {
            if (FileName.Length == 0)
            {
                throw new InvalidOperationException(SR.FileNameMissing);
            }
            if (StandardInputEncoding != null && !RedirectStandardInput)
            {
                throw new InvalidOperationException(SR.StandardInputEncodingNotAllowed);
            }
            if (StandardOutputEncoding != null && !RedirectStandardOutput)
            {
                throw new InvalidOperationException(SR.StandardOutputEncodingNotAllowed);
            }
            if (StandardErrorEncoding != null && !RedirectStandardError)
            {
                throw new InvalidOperationException(SR.StandardErrorEncodingNotAllowed);
            }
            if (!string.IsNullOrEmpty(Arguments) && HasArgumentList)
            {
                throw new InvalidOperationException(SR.ArgumentAndArgumentListInitialized);
            }
            if (HasArgumentList)
            {
                int argumentCount = ArgumentList.Count;
                for (int i = 0; i < argumentCount; i++)
                {
                    if (ArgumentList[i] is null)
                    {
                        throw new ArgumentNullException($"ArgumentList[{i}]");
                    }
                }
            }

            anyRedirection = RedirectStandardInput || RedirectStandardOutput || RedirectStandardError;
            if (UseShellExecute && anyRedirection)
            {
                throw new InvalidOperationException(SR.CantRedirectStreams);
            }
        }
    }
}
