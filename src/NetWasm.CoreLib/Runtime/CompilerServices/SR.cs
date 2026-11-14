// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Generated from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce resources;
// only the strings this namespace references are included.

namespace System.Runtime.CompilerServices
{
    internal static class SR
    {
        internal const string InvalidOperation_HandleIsNotInitialized =
            "Handle is not initialized.";
        internal const string ArgumentNull_Type =
            "Type cannot be null.";
        internal const string Argument_InvalidValue =
            "Value was invalid.";
        internal const string Acc_CreateGeneric =
            "Cannot create a type for which Type.ContainsGenericParameters is true.";
        internal const string Acc_CreateAbst =
            "Cannot create an abstract class.";
        internal const string Argument_NoUninitializedStrings =
            "Uninitialized Strings cannot be created.";
        internal const string NotSupported_ByRefLike =
            "Cannot create boxed ByRef-like values.";
        // NetWasm: the type was never named or allocated by the compiled program.
        internal const string PlatformNotSupported_UninitializedObject =
            "GetUninitializedObject can only create types that the compiled program names or allocates.";
        internal const string Arg_EnumIllegalVal =
            "Illegal enum value: {0}.";
        internal const string AssertionFailed =
            "Assertion failed.";
        internal const string AssertionFailed_Cnd =
            "Assertion failed: {0}";
        internal const string AssumptionFailed =
            "Assumption failed.";
        internal const string AssumptionFailed_Cnd =
            "Assumption failed: {0}";
        internal const string InvariantFailed =
            "Invariant failed.";
        internal const string InvariantFailed_Cnd =
            "Invariant failed: {0}";
        internal const string PostconditionFailed =
            "Postcondition failed.";
        internal const string PostconditionFailed_Cnd =
            "Postcondition failed: {0}";
        internal const string PostconditionOnExceptionFailed =
            "Postcondition failed after throwing an exception.";
        internal const string PostconditionOnExceptionFailed_Cnd =
            "Postcondition failed after throwing an exception: {0}";
        internal const string PreconditionFailed =
            "Precondition failed.";
        internal const string PreconditionFailed_Cnd =
            "Precondition failed: {0}";

        // NetWasm strings for its dynamic call site boundaries.
        internal const string PlatformNotSupported_CallSiteCreation =
            "NetWasm has no dynamic language runtime, so dynamic call sites cannot be created.";
        internal const string PlatformNotSupported_CallSiteUpdate =
            "NetWasm has no dynamic language runtime, so dynamic call sites have no update delegate.";

        internal static string Format(string format, object? arg0) =>
            string.Format(format, arg0);

        internal static string Format(string format, object? arg0, object? arg1) =>
            string.Format(format, arg0, arg1);

        internal static string Format(string format, object? arg0, object? arg1, object? arg2) =>
            string.Format(format, arg0, arg1, arg2);
    }
}
