// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

/*=============================================================================
**
** Class: CollectionsUtil
**
** Purpose: Creates collections that ignore the case in strings.
**
=============================================================================*/


namespace System.Collections.Specialized
{
    public class CollectionsUtil
    {
        public static Hashtable CreateCaseInsensitiveHashtable()
        {
            return new Hashtable(StringComparer.CurrentCultureIgnoreCase);
        }

        public static Hashtable CreateCaseInsensitiveHashtable(int capacity)
        {
            return new Hashtable(capacity, StringComparer.CurrentCultureIgnoreCase);
        }

        public static Hashtable CreateCaseInsensitiveHashtable(IDictionary d)
        {
            return new Hashtable(d, StringComparer.CurrentCultureIgnoreCase);
        }

        public static SortedList CreateCaseInsensitiveSortedList()
        {
            return new SortedList(CaseInsensitiveComparer.Default);
        }
    }
}
