// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Generated from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce resources;
// only the strings this namespace references are included.

namespace System.IO.IsolatedStorage
{
    internal static class SR
    {
        internal const string IsolatedStorage_ApplicationUndefined =
            "Application cannot be determined on an Assembly or Domain store.";
        internal const string IsolatedStorage_AssemblyUndefined =
            "Assembly cannot be determined for an Application store.";
        internal const string IsolatedStorage_CurrentSizeUndefined =
            "Current size cannot be determined for this store.";
        internal const string IsolatedStorage_Exception =
            "An error occurred while accessing IsolatedStorage.";
        internal const string IsolatedStorage_PlatformNotSupported =
            "System.IO.IsolatedStorage is not supported on this platform.";
        internal const string IsolatedStorage_QuotaIsUndefined =
            "{0} is not defined for this store. An operation was performed that requires access to {0}. Stores obtained using enumeration APIs do not have a well-defined {0}, since partial evidence is used to open the store.";
        internal const string IsolatedStorage_Scope_Invalid =
            "Invalid scope.";

        internal static string Format(string format, object? arg0) =>
            string.Format(format, arg0);

        internal static string Format(string format, object? arg0, object? arg1) =>
            string.Format(format, arg0, arg1);

        internal static string Format(string format, object? arg0, object? arg1, object? arg2) =>
            string.Format(format, arg0, arg1, arg2);
        internal const string IsolatedStorage_Init = "Initialization failed.";
        internal const string IsolatedStorage_CreateDirectory = "Unable to create directory.";
        internal const string IsolatedStorage_DeleteDirectories = "Unable to delete; directory or files in the directory could be in use.";
        internal const string IsolatedStorage_DeleteDirectory = "Unable to delete, directory not empty or does not exist.";
        internal const string IsolatedStorage_DeleteFile = "Unable to delete file.";
        internal const string IsolatedStorage_NotValidOnDesktop = "The Site scope is currently not supported.";
        internal const string IsolatedStorage_Operation = "Operation not permitted.";
        internal const string IsolatedStorage_Scope_U_R_M = "Invalid scope, expected User, User|Roaming or Machine.";
        internal const string IsolatedStorage_StoreNotOpen = "Store must be open for this operation.";
        internal const string PathNotFound_Path = "Could not find a part of the path '{0}'.";
        internal const string PlatformNotSupported_CAS = "Code Access Security is not supported on this platform.";
        internal const string IsolatedStorage_FileOpenMode = "Invalid mode, see System.IO.FileMode.";
        internal const string IsolatedStorage_Operation_ISFS = "Operation not permitted on IsolatedStorageFileStream.";
        internal const string IsolatedStorage_Path = "Path must be a valid file name.";
    }
}
