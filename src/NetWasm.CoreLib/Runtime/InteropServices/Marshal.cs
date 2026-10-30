// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit 474ed963699fc29809015392738a15231aaae2de.

using System.Text;

namespace System.Runtime.InteropServices
{
    public static unsafe class Marshal
    {
        public static readonly int SystemDefaultCharSize = 2;

        public static readonly int SystemMaxDBCSCharSize = GetSystemMaxDBCSCharSize();

        // Keep these values local: this file is also compiled by the native-interop
        // support fixture without the rest of NetWasm.CoreLib.
        private const int ErrorAmbiguousMatch = unchecked((int)0x8000211D);
        private const int ErrorApplication = unchecked((int)0x80131600);
        private const int ErrorArgument = unchecked((int)0x80070057);
        private const int ErrorArgumentOutOfRange = unchecked((int)0x80131502);
        private const int ErrorArithmetic = unchecked((int)0x80070216);
        private const int ErrorArrayTypeMismatch = unchecked((int)0x80131503);
        private const int ErrorBadExeFormat = unchecked((int)0x800700C1);
        private const int ErrorCustomAttributeFormat = unchecked((int)0x80131605);
        private const int ErrorDataMisaligned = unchecked((int)0x80131541);
        private const int ErrorDirectoryNotFound = unchecked((int)0x80070003);
        private const int ErrorDivideByZero = unchecked((int)0x80020012);
        private const int ErrorDllNotFound = unchecked((int)0x80131524);
        private const int ErrorDuplicateWaitObject = unchecked((int)0x80131529);
        private const int ErrorEndOfStream = unchecked((int)0x80070026);
        private const int ErrorEntryPointNotFound = unchecked((int)0x80131523);
        private const int ErrorException = unchecked((int)0x80131500);
        private const int ErrorExecutionEngine = unchecked((int)0x80131506);
        private const int ErrorFieldAccess = unchecked((int)0x80131507);
        private const int ErrorFileLoad = unchecked((int)0x80131621);
        private const int ErrorFileNotFound = unchecked((int)0x80070002);
        private const int ErrorFormat = unchecked((int)0x80131537);
        private const int ErrorIndexOutOfRange = unchecked((int)0x80131508);
        private const int ErrorInsufficientExecutionStack = unchecked((int)0x80131578);
        private const int ErrorInvalidCast = unchecked((int)0x80004002);
        private const int ErrorInvalidOperation = unchecked((int)0x80131509);
        private const int ErrorInvalidProgram = unchecked((int)0x8013153A);
        private const int ErrorIo = unchecked((int)0x80131620);
        private const int ErrorKeyNotFound = unchecked((int)0x80131577);
        private const int ErrorMarshalDirective = unchecked((int)0x80131535);
        private const int ErrorMemberAccess = unchecked((int)0x8013151A);
        private const int ErrorMethodAccess = unchecked((int)0x80131510);
        private const int ErrorMissingField = unchecked((int)0x80131511);
        private const int ErrorMissingMember = unchecked((int)0x80131512);
        private const int ErrorMissingMethod = unchecked((int)0x80131513);
        private const int ErrorMulticastNotSupported = unchecked((int)0x80131514);
        private const int ErrorNotFiniteNumber = unchecked((int)0x80131528);
        private const int ErrorNotSupported = unchecked((int)0x80131515);
        private const int ErrorObjectDisposed = unchecked((int)0x80131622);
        private const int ErrorOperationCanceled = unchecked((int)0x8013153B);
        private const int ErrorOutOfMemory = unchecked((int)0x8007000E);
        private const int ErrorOverflow = unchecked((int)0x80131516);
        private const int ErrorPathTooLong = unchecked((int)0x800700CE);
        private const int ErrorPlatformNotSupported = unchecked((int)0x80131539);
        private const int ErrorPointer = unchecked((int)0x80004003);
        private const int ErrorRank = unchecked((int)0x80131517);
        private const int ErrorReflectionTypeLoad = unchecked((int)0x80131602);
        private const int ErrorRuntimeWrapped = unchecked((int)0x8013153E);
        private const int ErrorSecurity = unchecked((int)0x8013150A);
        private const int ErrorStackOverflow = unchecked((int)0x800703E9);
        private const int ErrorSynchronizationLock = unchecked((int)0x80131518);
        private const int ErrorSystem = unchecked((int)0x80131501);
        private const int ErrorTargetInvocation = unchecked((int)0x80131604);
        private const int ErrorTargetParameterCount = unchecked((int)0x8002000E);
        private const int ErrorTypeAccess = unchecked((int)0x80131543);
        private const int ErrorTypeInitialization = unchecked((int)0x80131534);
        private const int ErrorTypeLoad = unchecked((int)0x80131522);
        private const int ErrorTypeUnloaded = unchecked((int)0x80131013);
        private const int ErrorVerification = unchecked((int)0x8013150D);
        private const int ErrorClosed = unchecked((int)0x80000013);
        private const int StorageErrorPathNotFound = unchecked((int)0x80030003);
        private const int ControlErrorPathNotFound = unchecked((int)0x800A004C);
        private const int FusionErrorInvalidName = unchecked((int)0x80131047);
        private const int FusionErrorReferenceMismatch = unchecked((int)0x80131040);
        private const int ErrorTooManyOpenFiles = unchecked((int)0x80070004);
        private const int ErrorSharingViolation = unchecked((int)0x80070020);
        private const int ErrorLockViolation = unchecked((int)0x80070021);
        private const int ErrorOpenFailed = unchecked((int)0x8007006E);
        private const int ErrorDiskCorrupt = unchecked((int)0x80070571);
        private const int ErrorUnrecognizedVolume = unchecked((int)0x800703ED);
        private const int ErrorDllInitFailed = unchecked((int)0x8007045A);
        private const int ErrorAssemblyLoadInProgress = unchecked((int)0x80131016);
        private const int ErrorFileInvalid = unchecked((int)0x800703EE);
        private const int ControlErrorFileNotFound = unchecked((int)0x800A0035);

        private static int GetSystemMaxDBCSCharSize() => 3;

        public static IntPtr AllocHGlobal(int cb) => AllocHGlobal((nint)cb);

        public static IntPtr AllocHGlobal(nint cb) =>
            (nint)NativeMemory.Alloc((nuint)cb);

        public static void FreeHGlobal(IntPtr hglobal) =>
            NativeMemory.Free((void*)(nint)hglobal);

        public static IntPtr AllocCoTaskMem(int cb) =>
            AllocHGlobal((nint)(uint)cb);

        public static void FreeCoTaskMem(IntPtr ptr) =>
            FreeHGlobal(ptr);

        public static void ThrowExceptionForHR(int errorCode)
        {
            if (errorCode >= 0)
            {
                return;
            }

            Exception exception = errorCode switch
            {
                ErrorAmbiguousMatch => new Reflection.AmbiguousMatchException(),
                ErrorApplication => new ApplicationException(),
                unchecked((int)0x80004001) => new NotImplementedException(),
                unchecked((int)0x80070005) => new UnauthorizedAccessException(),
                unchecked((int)0x8007000B) => new BadImageFormatException(),
                ErrorArgument => new ArgumentException(),
                ErrorArgumentOutOfRange => new ArgumentOutOfRangeException(),
                ErrorArithmetic => new ArithmeticException(),
                ErrorArrayTypeMismatch => new ArrayTypeMismatchException(),
                ErrorBadExeFormat => new BadImageFormatException(),
                ErrorCustomAttributeFormat =>
                    new Reflection.CustomAttributeFormatException(),
                ErrorDataMisaligned => new DataMisalignedException(),
                ErrorDirectoryNotFound or StorageErrorPathNotFound or
                    ControlErrorPathNotFound => new IO.DirectoryNotFoundException(),
                ErrorDivideByZero => new DivideByZeroException(),
                ErrorDllNotFound => new DllNotFoundException(),
                ErrorDuplicateWaitObject => new DuplicateWaitObjectException(),
                ErrorEndOfStream => new IO.EndOfStreamException(),
                ErrorEntryPointNotFound => new EntryPointNotFoundException(),
                ErrorException => new Exception(),
#pragma warning disable CS0618
                ErrorExecutionEngine => new ExecutionEngineException(),
#pragma warning restore CS0618
                ErrorFieldAccess => new FieldAccessException(),
                ErrorFileLoad or FusionErrorInvalidName or
                    FusionErrorReferenceMismatch or ErrorTooManyOpenFiles or
                    ErrorSharingViolation or ErrorLockViolation or
                    ErrorOpenFailed or ErrorDiskCorrupt or
                    ErrorUnrecognizedVolume or ErrorDllInitFailed or
                    ErrorAssemblyLoadInProgress or ErrorFileInvalid =>
                    new IO.FileLoadException(),
                ErrorFileNotFound or ControlErrorFileNotFound =>
                    new IO.FileNotFoundException(),
                ErrorFormat => new FormatException(),
                ErrorIndexOutOfRange => new IndexOutOfRangeException(),
                ErrorInsufficientExecutionStack =>
                    new InsufficientExecutionStackException(),
                ErrorInvalidCast => new InvalidCastException(),
                ErrorInvalidOperation => new InvalidOperationException(),
                ErrorInvalidProgram => new InvalidProgramException(),
                ErrorIo => new IO.IOException(),
                ErrorKeyNotFound =>
                    new Collections.Generic.KeyNotFoundException(),
                ErrorMarshalDirective => new MarshalDirectiveException(),
                ErrorMemberAccess => new MemberAccessException(),
                ErrorMethodAccess => new MethodAccessException(),
                ErrorMissingField => new MissingFieldException(),
                ErrorMissingMember => new MissingMemberException(),
                ErrorMissingMethod or ErrorReflectionTypeLoad or
                    ErrorRuntimeWrapped or ErrorTargetInvocation =>
                    new MissingMethodException(),
                ErrorMulticastNotSupported =>
                    new MulticastNotSupportedException(),
                ErrorNotFiniteNumber => new NotFiniteNumberException(),
                ErrorNotSupported => new NotSupportedException(),
                ErrorObjectDisposed or ErrorClosed =>
                    new ObjectDisposedException(null),
                ErrorOperationCanceled => new OperationCanceledException(),
                ErrorOutOfMemory => new OutOfMemoryException(),
                ErrorOverflow => new OverflowException(),
                ErrorPathTooLong => new IO.PathTooLongException(),
                ErrorPlatformNotSupported =>
                    new PlatformNotSupportedException(),
                ErrorPointer => new NullReferenceException(),
                ErrorRank => new RankException(),
                ErrorSecurity => new Security.SecurityException(),
                ErrorStackOverflow => new StackOverflowException(),
                ErrorSynchronizationLock =>
                    new Threading.SynchronizationLockException(),
                ErrorSystem => new SystemException(),
                ErrorTargetParameterCount =>
                    new Reflection.TargetParameterCountException(),
                ErrorTypeAccess => new TypeAccessException(),
                ErrorTypeInitialization =>
                    new TypeInitializationException(null, null),
                ErrorTypeLoad => new TypeLoadException(),
                ErrorTypeUnloaded => new TypeUnloadedException(),
                ErrorVerification => new Security.VerificationException(),
                _ => new COMException(
                    "Error " + (uint)errorCode + " (0x" +
                        ((uint)errorCode).ToString("X8") + ")",
                    errorCode),
            };
            exception.HResult = errorCode;
            throw exception;
        }

        public static string? PtrToStringUTF8(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero)
            {
                return null;
            }

            return Encoding.UTF8.GetString(
                MemoryMarshal.CreateReadOnlySpanFromNullTerminated((byte*)ptr));
        }

        public static string PtrToStringUTF8(IntPtr ptr, int byteLen)
        {
            if (ptr == IntPtr.Zero)
            {
                throw new ArgumentNullException(nameof(ptr));
            }
            ArgumentOutOfRangeException.ThrowIfNegative(byteLen);
            return Encoding.UTF8.GetString(new ReadOnlySpan<byte>((byte*)ptr, byteLen));
        }
    }
}
