// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


// Adapted from the pinned runtime: legacy formatter serialization is outside
// this ordinary exception contract slice. Messages use invariant resources.
namespace System.Globalization
{
    [Serializable]
    [System.Runtime.CompilerServices.TypeForwardedFrom("mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
    public partial class CultureNotFoundException : ArgumentException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected CultureNotFoundException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("CultureNotFoundException formatter deserialization is not available on this NetWasm platform.");

        private readonly string? _invalidCultureName; // unrecognized culture name
        private readonly int? _invalidCultureId;     // unrecognized culture Lcid

        public CultureNotFoundException()
            : base(DefaultMessage)
        {
        }

        public CultureNotFoundException(string? message)
            : base(message ?? DefaultMessage)
        {
        }

        public CultureNotFoundException(string? paramName, string? message)
            : base(message ?? DefaultMessage, paramName)
        {
        }

        public CultureNotFoundException(string? message, Exception? innerException)
            : base(message ?? DefaultMessage, innerException)
        {
        }

        public CultureNotFoundException(string? paramName, string? invalidCultureName, string? message)
            : base(message ?? DefaultMessage, paramName)
        {
            _invalidCultureName = invalidCultureName;
        }

        public CultureNotFoundException(string? message, string? invalidCultureName, Exception? innerException)
            : base(message ?? DefaultMessage, innerException)
        {
            _invalidCultureName = invalidCultureName;
        }

        public CultureNotFoundException(string? message, int invalidCultureId, Exception? innerException)
            : base(message ?? DefaultMessage, innerException)
        {
            _invalidCultureId = invalidCultureId;
        }

        public CultureNotFoundException(string? paramName, int invalidCultureId, string? message)
            : base(message ?? DefaultMessage, paramName)
        {
            _invalidCultureId = invalidCultureId;
        }



        public virtual int? InvalidCultureId => _invalidCultureId;

        public virtual string? InvalidCultureName => _invalidCultureName;

        private static string DefaultMessage => "Culture is not supported.";

        private string? FormattedInvalidCultureId =>
            InvalidCultureId != null ?
                string.Format(CultureInfo.InvariantCulture, "{0} (0x{0:x4})", (int)InvalidCultureId) :
                InvalidCultureName;

        public override string Message
        {
            get
            {
                string s = base.Message;
                if (_invalidCultureId != null || _invalidCultureName != null)
                {
                    string valueMessage = string.Format("{0} is an invalid culture identifier.", FormattedInvalidCultureId);
                    if (s == null)
                    {
                        return valueMessage;
                    }

                    return s + Environment.NewLine + valueMessage;
                }
                return s;
            }
        }
    }
}
