// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

namespace System.Runtime.Serialization.Formatters.Binary
{
    [Obsolete(Obsoletions.BinaryFormatterMessage, DiagnosticId = Obsoletions.BinaryFormatterDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
    public sealed partial class BinaryFormatter : IFormatter
    {
        internal ISurrogateSelector? _surrogates;
        internal StreamingContext _context;
        internal SerializationBinder? _binder;
        internal FormatterTypeStyle _typeFormat = FormatterTypeStyle.TypesAlways; // For version resiliency, always put out types
        internal FormatterAssemblyStyle _assemblyFormat = FormatterAssemblyStyle.Simple;
        internal TypeFilterLevel _securityLevel = TypeFilterLevel.Full;

        public FormatterTypeStyle TypeFormat { get { return _typeFormat; } set { _typeFormat = value; } }
        public FormatterAssemblyStyle AssemblyFormat { get { return _assemblyFormat; } set { _assemblyFormat = value; } }
        public TypeFilterLevel FilterLevel { get { return _securityLevel; } set { _securityLevel = value; } }
        public ISurrogateSelector? SurrogateSelector { get { return _surrogates; } set { _surrogates = value; } }
        public SerializationBinder? Binder { get { return _binder; } set { _binder = value; } }
        public StreamingContext Context { get { return _context; } set { _context = value; } }

        public BinaryFormatter() : this(null, new StreamingContext(StreamingContextStates.All))
        {
        }

        public BinaryFormatter(ISurrogateSelector? selector, StreamingContext context)
        {
            _surrogates = selector;
            _context = context;
        }
    }
}
