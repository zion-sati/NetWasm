using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class DerivedExceptionSerializationCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void StandardDerivedConstructorContractsRemainCatchable(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "DerivedExceptionFixture", Source, "DerivedExceptionFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        #pragma warning disable SYSLIB0051
        using System;
        using System.Runtime.Serialization;
        namespace DerivedExceptionFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var caught = 0;
                try { _ = new Case0(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("AccessViolationException")) return 2; caught++; }
                try { _ = new Case1(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("AggregateException")) return 2; caught++; }
                try { _ = new Case2(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("AppDomainUnloadedException")) return 2; caught++; }
                try { _ = new Case3(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("ApplicationException")) return 2; caught++; }
                try { _ = new Case4(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("ArgumentException")) return 2; caught++; }
                try { _ = new Case5(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("ArgumentNullException")) return 2; caught++; }
                try { _ = new Case6(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("ArgumentOutOfRangeException")) return 2; caught++; }
                try { _ = new Case7(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("ArithmeticException")) return 2; caught++; }
                try { _ = new Case8(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("ArrayTypeMismatchException")) return 2; caught++; }
                try { _ = new Case9(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("BadImageFormatException")) return 2; caught++; }
                try { _ = new Case10(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("CannotUnloadAppDomainException")) return 2; caught++; }
                try { _ = new Case11(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("KeyNotFoundException")) return 2; caught++; }
                try { _ = new Case12(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("InvalidEnumArgumentException")) return 2; caught++; }
                try { _ = new Case13(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("ContextMarshalException")) return 2; caught++; }
                try { _ = new Case14(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("EventSourceException")) return 2; caught++; }
                try { _ = new Case15(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("DivideByZeroException")) return 2; caught++; }
                try { _ = new Case16(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("DllNotFoundException")) return 2; caught++; }
                try { _ = new Case17(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("DuplicateWaitObjectException")) return 2; caught++; }
                try { _ = new Case18(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("EntryPointNotFoundException")) return 2; caught++; }
                try { _ = new Case19(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("FieldAccessException")) return 2; caught++; }
                try { _ = new Case20(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("FormatException")) return 2; caught++; }
                try { _ = new Case21(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("CultureNotFoundException")) return 2; caught++; }
                try { _ = new Case22(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("DirectoryNotFoundException")) return 2; caught++; }
                try { _ = new Case23(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("EndOfStreamException")) return 2; caught++; }
                try { _ = new Case24(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("FileLoadException")) return 2; caught++; }
                try { _ = new Case25(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("FileNotFoundException")) return 2; caught++; }
                try { _ = new Case26(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("IOException")) return 2; caught++; }
                try { _ = new Case27(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("PathTooLongException")) return 2; caught++; }
                try { _ = new Case28(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("InvalidCastException")) return 2; caught++; }
                try { _ = new Case29(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("InvalidOperationException")) return 2; caught++; }
                try { _ = new Case30(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("InvalidTimeZoneException")) return 2; caught++; }
                try { _ = new Case31(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("MemberAccessException")) return 2; caught++; }
                try { _ = new Case32(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("MethodAccessException")) return 2; caught++; }
                try { _ = new Case33(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("MissingFieldException")) return 2; caught++; }
                try { _ = new Case34(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("MissingMemberException")) return 2; caught++; }
                try { _ = new Case35(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("MissingMethodException")) return 2; caught++; }
                try { _ = new Case36(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("NotFiniteNumberException")) return 2; caught++; }
                try { _ = new Case37(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("NotImplementedException")) return 2; caught++; }
                try { _ = new Case38(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("NotSupportedException")) return 2; caught++; }
                try { _ = new Case39(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("NullReferenceException")) return 2; caught++; }
                try { _ = new Case40(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("ObjectDisposedException")) return 2; caught++; }
                try { _ = new Case41(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("OperationCanceledException")) return 2; caught++; }
                try { _ = new Case42(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("OutOfMemoryException")) return 2; caught++; }
                try { _ = new Case43(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("OverflowException")) return 2; caught++; }
                try { _ = new Case44(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("PlatformNotSupportedException")) return 2; caught++; }
                try { _ = new Case45(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("RankException")) return 2; caught++; }
                try { _ = new Case46(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("CustomAttributeFormatException")) return 2; caught++; }
                try { _ = new Case47(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("InvalidFilterCriteriaException")) return 2; caught++; }
                try { _ = new Case48(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("TargetException")) return 2; caught++; }
                try { _ = new Case49(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("MissingManifestResourceException")) return 2; caught++; }
                try { _ = new Case50(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("MissingSatelliteAssemblyException")) return 2; caught++; }
                try { _ = new Case51(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("InvalidComObjectException")) return 2; caught++; }
                try { _ = new Case52(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("InvalidOleVariantTypeException")) return 2; caught++; }
                try { _ = new Case53(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("MarshalDirectiveException")) return 2; caught++; }
                try { _ = new Case54(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("SafeArrayRankMismatchException")) return 2; caught++; }
                try { _ = new Case55(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("SafeArrayTypeMismatchException")) return 2; caught++; }
                try { _ = new Case56(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("SerializationException")) return 2; caught++; }
                try { _ = new Case57(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("CryptographicException")) return 2; caught++; }
                try { _ = new Case58(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("SecurityException")) return 2; caught++; }
                try { _ = new Case59(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("VerificationException")) return 2; caught++; }
                try { _ = new Case60(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("SynchronizationLockException")) return 2; caught++; }
                try { _ = new Case61(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("TaskCanceledException")) return 2; caught++; }
                try { _ = new Case62(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("TaskSchedulerException")) return 2; caught++; }
                try { _ = new Case63(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("TimeZoneNotFoundException")) return 2; caught++; }
                try { _ = new Case64(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("TimeoutException")) return 2; caught++; }
                try { _ = new Case65(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("TypeAccessException")) return 2; caught++; }
                try { _ = new Case66(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("TypeLoadException")) return 2; caught++; }
                try { _ = new Case67(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("TypeUnloadedException")) return 2; caught++; }
                try { _ = new Case68(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("UnauthorizedAccessException")) return 2; caught++; }
                try { _ = new Case69(null, default); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("UriFormatException")) return 2; caught++; }
                return caught == 70 ? input + 1 : 3;
            }
        }
        public sealed class Case0 : System.AccessViolationException
        { public Case0(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case1 : System.AggregateException
        { public Case1(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case2 : System.AppDomainUnloadedException
        { public Case2(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case3 : System.ApplicationException
        { public Case3(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case4 : System.ArgumentException
        { public Case4(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case5 : System.ArgumentNullException
        { public Case5(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case6 : System.ArgumentOutOfRangeException
        { public Case6(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case7 : System.ArithmeticException
        { public Case7(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case8 : System.ArrayTypeMismatchException
        { public Case8(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case9 : System.BadImageFormatException
        { public Case9(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case10 : System.CannotUnloadAppDomainException
        { public Case10(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case11 : System.Collections.Generic.KeyNotFoundException
        { public Case11(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case12 : System.ComponentModel.InvalidEnumArgumentException
        { public Case12(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case13 : System.ContextMarshalException
        { public Case13(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case14 : System.Diagnostics.Tracing.EventSourceException
        { public Case14(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case15 : System.DivideByZeroException
        { public Case15(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case16 : System.DllNotFoundException
        { public Case16(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case17 : System.DuplicateWaitObjectException
        { public Case17(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case18 : System.EntryPointNotFoundException
        { public Case18(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case19 : System.FieldAccessException
        { public Case19(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case20 : System.FormatException
        { public Case20(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case21 : System.Globalization.CultureNotFoundException
        { public Case21(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case22 : System.IO.DirectoryNotFoundException
        { public Case22(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case23 : System.IO.EndOfStreamException
        { public Case23(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case24 : System.IO.FileLoadException
        { public Case24(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case25 : System.IO.FileNotFoundException
        { public Case25(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case26 : System.IO.IOException
        { public Case26(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case27 : System.IO.PathTooLongException
        { public Case27(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case28 : System.InvalidCastException
        { public Case28(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case29 : System.InvalidOperationException
        { public Case29(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case30 : System.InvalidTimeZoneException
        { public Case30(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case31 : System.MemberAccessException
        { public Case31(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case32 : System.MethodAccessException
        { public Case32(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case33 : System.MissingFieldException
        { public Case33(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case34 : System.MissingMemberException
        { public Case34(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case35 : System.MissingMethodException
        { public Case35(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case36 : System.NotFiniteNumberException
        { public Case36(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case37 : System.NotImplementedException
        { public Case37(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case38 : System.NotSupportedException
        { public Case38(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case39 : System.NullReferenceException
        { public Case39(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case40 : System.ObjectDisposedException
        { public Case40(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case41 : System.OperationCanceledException
        { public Case41(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case42 : System.OutOfMemoryException
        { public Case42(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case43 : System.OverflowException
        { public Case43(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case44 : System.PlatformNotSupportedException
        { public Case44(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case45 : System.RankException
        { public Case45(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case46 : System.Reflection.CustomAttributeFormatException
        { public Case46(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case47 : System.Reflection.InvalidFilterCriteriaException
        { public Case47(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case48 : System.Reflection.TargetException
        { public Case48(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case49 : System.Resources.MissingManifestResourceException
        { public Case49(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case50 : System.Resources.MissingSatelliteAssemblyException
        { public Case50(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case51 : System.Runtime.InteropServices.InvalidComObjectException
        { public Case51(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case52 : System.Runtime.InteropServices.InvalidOleVariantTypeException
        { public Case52(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case53 : System.Runtime.InteropServices.MarshalDirectiveException
        { public Case53(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case54 : System.Runtime.InteropServices.SafeArrayRankMismatchException
        { public Case54(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case55 : System.Runtime.InteropServices.SafeArrayTypeMismatchException
        { public Case55(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case56 : System.Runtime.Serialization.SerializationException
        { public Case56(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case57 : System.Security.Cryptography.CryptographicException
        { public Case57(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case58 : System.Security.SecurityException
        { public Case58(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case59 : System.Security.VerificationException
        { public Case59(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case60 : System.Threading.SynchronizationLockException
        { public Case60(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case61 : System.Threading.Tasks.TaskCanceledException
        { public Case61(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case62 : System.Threading.Tasks.TaskSchedulerException
        { public Case62(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case63 : System.TimeZoneNotFoundException
        { public Case63(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case64 : System.TimeoutException
        { public Case64(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case65 : System.TypeAccessException
        { public Case65(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case66 : System.TypeLoadException
        { public Case66(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case67 : System.TypeUnloadedException
        { public Case67(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case68 : System.UnauthorizedAccessException
        { public Case68(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class Case69 : System.UriFormatException
        { public Case69(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        """;
}
