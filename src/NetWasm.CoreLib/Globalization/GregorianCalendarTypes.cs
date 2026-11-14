// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

namespace System.Globalization
{
    // Note: The values of the members of this enum must match the corresponding values
    // in the CalendarId enum (since we cast between GregorianCalendarTypes and CalendarId).
    public enum GregorianCalendarTypes
    {
        Localized = CalendarId.GREGORIAN,
        USEnglish = CalendarId.GREGORIAN_US,
        MiddleEastFrench = CalendarId.GREGORIAN_ME_FRENCH,
        Arabic = CalendarId.GREGORIAN_ARABIC,
        TransliteratedEnglish = CalendarId.GREGORIAN_XLIT_ENGLISH,
        TransliteratedFrench = CalendarId.GREGORIAN_XLIT_FRENCH,
    }
}
