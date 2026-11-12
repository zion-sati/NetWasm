using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class PortableExceptionApiCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void AggregateExceptionMatchesThePinnedThinProfile(
        bool optimized,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "AggregateExceptionFixture",
            """
            namespace AggregateExceptionFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var first = new System.Exception("one");
                    var second = new System.Exception("two");
                    var third = new System.Exception("three");
                    var fourth = new System.Exception("four");
                    var fifth = new System.Exception("five");

                    var source = new[] { first, second };
                    var fromArray = new System.AggregateException("array", source);
                    source[0] = third;
                    if (!object.ReferenceEquals(fromArray.InnerException, first) ||
                        fromArray.InnerExceptions.Count != 2 ||
                        !object.ReferenceEquals(fromArray.InnerExceptions[0], first) ||
                        !object.ReferenceEquals(fromArray.InnerExceptions, fromArray.InnerExceptions)) return -1;

                    var enumerableSource = new[] { first, second };
                    var fromEnumerable = new System.AggregateException(
                        "enumerable",
                        (System.Collections.Generic.IEnumerable<System.Exception>)enumerableSource);
                    enumerableSource[0] = third;
                    if (!object.ReferenceEquals(fromEnumerable.InnerExceptions[0], first)) return -2;

                    try
                    {
                        _ = new System.AggregateException(
                            "null",
                            (System.Collections.Generic.IEnumerable<System.Exception>)null!);
                        return -3;
                    }
                    catch (System.ArgumentNullException error)
                    {
                        if (error.ParamName != "innerExceptions") return -4;
                    }

                    try
                    {
                        _ = new System.AggregateException("null", (System.Exception[])null!);
                        return -5;
                    }
                    catch (System.ArgumentNullException error)
                    {
                        if (error.ParamName != "innerExceptions") return -6;
                    }

                    try
                    {
                        _ = new System.AggregateException("null element", first, null!);
                        return -7;
                    }
                    catch (System.ArgumentException)
                    {
                    }

                    try
                    {
                        ((System.Collections.Generic.IList<System.Exception>)fromArray.InnerExceptions).Add(third);
                        return -8;
                    }
                    catch (System.NotSupportedException)
                    {
                    }

                    var single = new System.AggregateException("single", first);
                    if (!object.ReferenceEquals(single.GetBaseException(), first)) return -9;
                    var multiple = new System.AggregateException("multiple", first, second);
                    var nestedMultiple = new System.AggregateException("nested", multiple);
                    if (!object.ReferenceEquals(nestedMultiple.GetBaseException(), multiple)) return -10;

                    var predicateCalls = 0;
                    try
                    {
                        new System.AggregateException("handle", first, second, third, fourth).Handle(error =>
                        {
                            predicateCalls++;
                            return object.ReferenceEquals(error, first) || object.ReferenceEquals(error, third);
                        });
                        return -11;
                    }
                    catch (System.AggregateException unhandled)
                    {
                        if (predicateCalls != 4 || unhandled.InnerExceptions.Count != 2 ||
                            !object.ReferenceEquals(unhandled.InnerExceptions[0], second) ||
                            !object.ReferenceEquals(unhandled.InnerExceptions[1], fourth)) return -12;
                    }

                    var deep = new System.AggregateException("deep", fourth);
                    var middle = new System.AggregateException("middle", second, deep, third);
                    var root = new System.AggregateException("root", first, middle, fifth);
                    var flattened = root.Flatten();
                    if (flattened.InnerExceptions.Count != 5 ||
                        !object.ReferenceEquals(flattened.InnerExceptions[0], first) ||
                        !object.ReferenceEquals(flattened.InnerExceptions[1], fifth) ||
                        !object.ReferenceEquals(flattened.InnerExceptions[2], second) ||
                        !object.ReferenceEquals(flattened.InnerExceptions[3], third) ||
                        !object.ReferenceEquals(flattened.InnerExceptions[4], fourth) ||
                        flattened.Message != "root (one) (five) (two) (three) (four)") return -13;

                    var derived = new DerivedAggregate(first).Flatten();
                    if (derived.Message != "derived (one)") return -14;

                    return input;
                }

                private sealed class DerivedAggregate : System.AggregateException
                {
                    public DerivedAggregate(System.Exception error) : base("base", error)
                    {
                    }

                    public override string Message => "derived";
                }
            }
            """,
            TestAssets.RepositorySdkVersion,
            "latest",
            optimized);

        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "AggregateExceptionFixture.EntryPoint",
            "Run",
            [],
            Target: target));

        Assert.Equal(
            42,
            ExecuteWithStandardWasiNode(
                compilation.ApplicationModule,
                assets.Directory,
                42,
                target,
                compilation.StaticDataEnd));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void OrdinaryExceptionStateMatchesThePinnedThinProfile(
        bool optimized,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "OrdinaryExceptionStateFixture",
            """
            namespace OrdinaryExceptionStateFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var inner = new System.Exception("inner");
                    var error = new System.Exception("outer", inner)
                    {
                        Source = "source",
                        HelpLink = "help",
                    };
                    if (error.Message != "outer" ||
                        !object.ReferenceEquals(error.InnerException, inner) ||
                        error.Source != "source" ||
                        error.HelpLink != "help") return -1;

                    var data = error.Data;
                    if (!object.ReferenceEquals(data, error.Data) || data.Count != 0) return -2;
                    data.Add("first", 11);
                    data["second"] = 22;
                    if (data.Count != 2 || (int)data["first"]! != 11 ||
                        !data.Contains("second")) return -3;

                    var copy = new object[2];
                    data.CopyTo(copy, 0);
                    if (copy[0] is not System.Collections.DictionaryEntry first ||
                        (string)first.Key != "first" || (int)first.Value! != 11 ||
                        copy[1] is not System.Collections.DictionaryEntry second ||
                        (string)second.Key != "second" || (int)second.Value! != 22) return -4;

                    var enumerator = data.GetEnumerator();
                    if (!ThrowsInvalidOperation(() => _ = enumerator.Current)) return -5;
                    if (!enumerator.MoveNext() || (string)enumerator.Key != "first") return -6;
                    data["third"] = 33;
                    if (!ThrowsInvalidOperation(() => enumerator.MoveNext())) return -7;
                    data.Remove("third");
                    data.Clear();
                    if (data.Count != 0) return -8;

                    var duplicateWithInner = new System.DuplicateWaitObjectException(
                        message: "duplicate",
                        innerException: inner);
                    if (duplicateWithInner.Message != "duplicate" ||
                        duplicateWithInner.ParamName is not null ||
                        !object.ReferenceEquals(duplicateWithInner.InnerException, inner)) return -9;

                    var duplicateWithParameter = new System.DuplicateWaitObjectException(
                        parameterName: "item",
                        message: "duplicate");
                    if (duplicateWithParameter.ParamName != "item" ||
                        duplicateWithParameter.Message != "duplicate (Parameter 'item')") return -10;

                    var missingMember = new System.MissingMemberException(
                        className: "Widget",
                        memberName: "Value");
                    var missingField = new System.MissingFieldException(
                        className: "Widget",
                        fieldName: "Value");
                    var missingMethod = new System.MissingMethodException(
                        className: "Widget",
                        methodName: "Run");
                    if (missingMember.Message != "Member 'Widget.Value' not found." ||
                        missingField.Message != "Field 'Widget.Value' not found." ||
                        missingMethod.Message != "Method 'Widget.Run' not found.") return -11;

                    if (new System.ObjectDisposedException(null).ObjectName != string.Empty ||
                        new System.ObjectDisposedException("widget").ObjectName != "widget" ||
                        new System.TypeLoadException().TypeName != string.Empty ||
                        new System.TypeInitializationException(null, inner).TypeName != string.Empty ||
                        new System.TypeInitializationException("Widget", inner).TypeName != "Widget") return -12;

                    var cryptographic = new System.Security.Cryptography.CryptographicException(
                        "Value {0}",
                        "inserted");
                    if (cryptographic.Message != "Value inserted") return -13;

                    var token = new System.Threading.CancellationToken(canceled: true);
                    var canceled = new System.OperationCanceledException("cancel", inner, token);
                    if (canceled.Message != "cancel" ||
                        !object.ReferenceEquals(canceled.InnerException, inner) ||
                        canceled.CancellationToken != token) return -14;

                    var range = new System.ArgumentOutOfRangeException("value", 42, "range");
                    var image = new System.BadImageFormatException("image", "module.wasm", inner);
                    var finite = new System.NotFiniteNumberException("finite", 12.5, inner);
                    var unmatched = new System.Runtime.CompilerServices.SwitchExpressionException(42);
                    var wrappedObject = new object();
                    var wrapped = new System.Runtime.CompilerServices.RuntimeWrappedException(wrappedObject);
                    if ((int)range.ActualValue! != 42 || range.ParamName != "value" ||
                        image.FileName != "module.wasm" ||
                        !object.ReferenceEquals(image.InnerException, inner) ||
                        finite.OffendingNumber != 12.5 ||
                        !object.ReferenceEquals(finite.InnerException, inner) ||
                        (int)unmatched.UnmatchedValue! != 42 ||
                        !object.ReferenceEquals(wrapped.WrappedException, wrappedObject)) return -15;

                    return input;
                }

                private static bool ThrowsInvalidOperation(System.Action action)
                {
                    try
                    {
                        action();
                        return false;
                    }
                    catch (System.InvalidOperationException)
                    {
                        return true;
                    }
                }
            }
            """,
            TestAssets.RepositorySdkVersion,
            "latest",
            optimized);

        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "OrdinaryExceptionStateFixture.EntryPoint",
            "Run",
            [],
            Target: target));

        Assert.Equal(
            42,
            ExecuteWithStandardWasiNode(
                compilation.ApplicationModule,
                assets.Directory,
                42,
                target,
                compilation.StaticDataEnd));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void ConcreteExceptionConstructorsAssignTheirPinnedHResults(
        bool optimized,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "ConcreteExceptionHResultFixture",
            """
            namespace ConcreteExceptionHResultFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var inner = new System.Exception("inner");
                    System.Exception[] errors =
                    {
                        new System.AccessViolationException(),
                        new System.AccessViolationException("m"),
                        new System.AccessViolationException("m", inner),
                        new System.AppDomainUnloadedException(),
                        new System.AppDomainUnloadedException("m"),
                        new System.AppDomainUnloadedException("m", inner),
                        new System.ApplicationException(),
                        new System.ApplicationException("m"),
                        new System.ApplicationException("m", inner),
                        new System.CannotUnloadAppDomainException(),
                        new System.CannotUnloadAppDomainException("m"),
                        new System.CannotUnloadAppDomainException("m", inner),
                        new System.ContextMarshalException(),
                        new System.ContextMarshalException("m"),
                        new System.ContextMarshalException("m", inner),
                        new System.DataMisalignedException(),
                        new System.DataMisalignedException("m"),
                        new System.DataMisalignedException("m", inner),
                        new System.ExecutionEngineException(),
                        new System.ExecutionEngineException("m"),
                        new System.ExecutionEngineException("m", inner),
                        new System.InsufficientExecutionStackException(),
                        new System.InsufficientExecutionStackException("m"),
                        new System.InsufficientExecutionStackException("m", inner),
                        new System.InsufficientMemoryException(),
                        new System.InsufficientMemoryException("m"),
                        new System.InsufficientMemoryException("m", inner),
                        new System.InvalidProgramException(),
                        new System.InvalidProgramException("m"),
                        new System.InvalidProgramException("m", inner),
                        new System.MemberAccessException(),
                        new System.MemberAccessException("m"),
                        new System.MemberAccessException("m", inner),
                        new System.MethodAccessException(),
                        new System.MethodAccessException("m"),
                        new System.MethodAccessException("m", inner),
                        new System.MulticastNotSupportedException(),
                        new System.MulticastNotSupportedException("m"),
                        new System.MulticastNotSupportedException("m", inner),
                        new System.NotImplementedException(),
                        new System.NotImplementedException("m"),
                        new System.NotImplementedException("m", inner),
                        new System.RankException(),
                        new System.RankException("m"),
                        new System.RankException("m", inner),
                        new System.StackOverflowException(),
                        new System.StackOverflowException("m"),
                        new System.StackOverflowException("m", inner),
                        new System.TimeoutException(),
                        new System.TimeoutException("m"),
                        new System.TimeoutException("m", inner),
                        new System.TypeUnloadedException(),
                        new System.TypeUnloadedException("m"),
                        new System.TypeUnloadedException("m", inner),
                        new System.UnauthorizedAccessException(),
                        new System.UnauthorizedAccessException("m"),
                        new System.UnauthorizedAccessException("m", inner),
                        new System.BadImageFormatException(),
                        new System.BadImageFormatException("m"),
                        new System.BadImageFormatException("m", inner),
                        new System.BadImageFormatException("m", "f"),
                        new System.BadImageFormatException("m", "f", inner),
                        new System.DuplicateWaitObjectException(),
                        new System.DuplicateWaitObjectException("p"),
                        new System.DuplicateWaitObjectException("p", inner),
                        new System.DuplicateWaitObjectException("m", "p"),
                        new System.EntryPointNotFoundException(),
                        new System.EntryPointNotFoundException("m"),
                        new System.EntryPointNotFoundException("m", inner),
                        new System.FieldAccessException(),
                        new System.FieldAccessException("m"),
                        new System.FieldAccessException("m", inner),
                        new System.DllNotFoundException(),
                        new System.DllNotFoundException("m"),
                        new System.DllNotFoundException("m", inner),
                        new System.MissingMemberException(),
                        new System.MissingMemberException("m"),
                        new System.MissingMemberException("m", inner),
                        new System.MissingMemberException("m", "member"),
                        new System.MissingFieldException(),
                        new System.MissingFieldException("m"),
                        new System.MissingFieldException("m", inner),
                        new System.MissingFieldException("m", "field"),
                        new System.MissingMethodException(),
                        new System.MissingMethodException("m"),
                        new System.MissingMethodException("m", inner),
                        new System.MissingMethodException("m", "signature"),
                        new System.NotFiniteNumberException(),
                        new System.NotFiniteNumberException(1.0),
                        new System.NotFiniteNumberException("m"),
                        new System.NotFiniteNumberException("m", 1.0),
                        new System.NotFiniteNumberException("m", 1.0, inner),
                        new System.NotFiniteNumberException("m", inner),
                        new System.TypeLoadException(),
                        new System.TypeLoadException("m"),
                        new System.TypeLoadException("m", inner),
                        new System.TypeAccessException(),
                        new System.TypeAccessException("m"),
                        new System.TypeAccessException("m", inner),
                        new System.TypeInitializationException("T", inner),
                        new System.Runtime.AmbiguousImplementationException(),
                        new System.Runtime.AmbiguousImplementationException("m"),
                        new System.Runtime.AmbiguousImplementationException("m", inner),
                        new System.Runtime.CompilerServices.RuntimeWrappedException(new object()),
                        new System.Runtime.InteropServices.InvalidComObjectException(),
                        new System.Runtime.InteropServices.InvalidComObjectException("m"),
                        new System.Runtime.InteropServices.InvalidComObjectException("m", inner),
                        new System.Runtime.InteropServices.InvalidOleVariantTypeException(),
                        new System.Runtime.InteropServices.InvalidOleVariantTypeException("m"),
                        new System.Runtime.InteropServices.InvalidOleVariantTypeException("m", inner),
                        new System.Runtime.InteropServices.MarshalDirectiveException(),
                        new System.Runtime.InteropServices.MarshalDirectiveException("m"),
                        new System.Runtime.InteropServices.MarshalDirectiveException("m", inner),
                        new System.Runtime.InteropServices.SafeArrayRankMismatchException(),
                        new System.Runtime.InteropServices.SafeArrayRankMismatchException("m"),
                        new System.Runtime.InteropServices.SafeArrayRankMismatchException("m", inner),
                        new System.Runtime.InteropServices.SafeArrayTypeMismatchException(),
                        new System.Runtime.InteropServices.SafeArrayTypeMismatchException("m"),
                        new System.Runtime.InteropServices.SafeArrayTypeMismatchException("m", inner),
                        new System.Security.SecurityException(),
                        new System.Security.SecurityException("m"),
                        new System.Security.SecurityException("m", inner),
                        new System.Security.SecurityException("m", typeof(EntryPoint)),
                        new System.Security.SecurityException("m", typeof(EntryPoint), "state"),
                        new System.Security.VerificationException(),
                        new System.Security.VerificationException("m"),
                        new System.Security.VerificationException("m", inner),
                    };

                    int[] expected =
                    {
                        E_POINTER, E_POINTER, E_POINTER,
                        COR_E_APPDOMAINUNLOADED, COR_E_APPDOMAINUNLOADED, COR_E_APPDOMAINUNLOADED,
                        COR_E_APPLICATION, COR_E_APPLICATION, COR_E_APPLICATION,
                        COR_E_CANNOTUNLOADAPPDOMAIN, COR_E_CANNOTUNLOADAPPDOMAIN, COR_E_CANNOTUNLOADAPPDOMAIN,
                        COR_E_CONTEXTMARSHAL, COR_E_CONTEXTMARSHAL, COR_E_CONTEXTMARSHAL,
                        COR_E_DATAMISALIGNED, COR_E_DATAMISALIGNED, COR_E_DATAMISALIGNED,
                        COR_E_EXECUTIONENGINE, COR_E_EXECUTIONENGINE, COR_E_EXECUTIONENGINE,
                        COR_E_INSUFFICIENTEXECUTIONSTACK, COR_E_INSUFFICIENTEXECUTIONSTACK, COR_E_INSUFFICIENTEXECUTIONSTACK,
                        COR_E_INSUFFICIENTMEMORY, COR_E_INSUFFICIENTMEMORY, COR_E_INSUFFICIENTMEMORY,
                        COR_E_INVALIDPROGRAM, COR_E_INVALIDPROGRAM, COR_E_INVALIDPROGRAM,
                        COR_E_MEMBERACCESS, COR_E_MEMBERACCESS, COR_E_MEMBERACCESS,
                        COR_E_METHODACCESS, COR_E_METHODACCESS, COR_E_METHODACCESS,
                        COR_E_MULTICASTNOTSUPPORTED, COR_E_MULTICASTNOTSUPPORTED, COR_E_MULTICASTNOTSUPPORTED,
                        E_NOTIMPL, E_NOTIMPL, E_NOTIMPL,
                        COR_E_RANK, COR_E_RANK, COR_E_RANK,
                        COR_E_STACKOVERFLOW, COR_E_STACKOVERFLOW, COR_E_STACKOVERFLOW,
                        COR_E_TIMEOUT, COR_E_TIMEOUT, COR_E_TIMEOUT,
                        COR_E_TYPEUNLOADED, COR_E_TYPEUNLOADED, COR_E_TYPEUNLOADED,
                        COR_E_UNAUTHORIZEDACCESS, COR_E_UNAUTHORIZEDACCESS, COR_E_UNAUTHORIZEDACCESS,
                        COR_E_BADIMAGEFORMAT, COR_E_BADIMAGEFORMAT, COR_E_BADIMAGEFORMAT, COR_E_BADIMAGEFORMAT, COR_E_BADIMAGEFORMAT,
                        COR_E_DUPLICATEWAITOBJECT, COR_E_DUPLICATEWAITOBJECT, COR_E_DUPLICATEWAITOBJECT, COR_E_DUPLICATEWAITOBJECT,
                        COR_E_ENTRYPOINTNOTFOUND, COR_E_ENTRYPOINTNOTFOUND, COR_E_ENTRYPOINTNOTFOUND,
                        COR_E_FIELDACCESS, COR_E_FIELDACCESS, COR_E_FIELDACCESS,
                        COR_E_DLLNOTFOUND, COR_E_DLLNOTFOUND, COR_E_DLLNOTFOUND,
                        COR_E_MISSINGMEMBER, COR_E_MISSINGMEMBER, COR_E_MISSINGMEMBER, COR_E_MISSINGMEMBER,
                        COR_E_MISSINGFIELD, COR_E_MISSINGFIELD, COR_E_MISSINGFIELD, COR_E_MISSINGFIELD,
                        COR_E_MISSINGMETHOD, COR_E_MISSINGMETHOD, COR_E_MISSINGMETHOD, COR_E_MISSINGMETHOD,
                        COR_E_NOTFINITENUMBER, COR_E_NOTFINITENUMBER, COR_E_NOTFINITENUMBER, COR_E_NOTFINITENUMBER, COR_E_NOTFINITENUMBER, COR_E_NOTFINITENUMBER,
                        COR_E_TYPELOAD, COR_E_TYPELOAD, COR_E_TYPELOAD,
                        COR_E_TYPEACCESS, COR_E_TYPEACCESS, COR_E_TYPEACCESS,
                        COR_E_TYPEINITIALIZATION,
                        COR_E_AMBIGUOUSIMPLEMENTATION, COR_E_AMBIGUOUSIMPLEMENTATION, COR_E_AMBIGUOUSIMPLEMENTATION,
                        COR_E_RUNTIMEWRAPPED,
                        COR_E_INVALIDCOMOBJECT, COR_E_INVALIDCOMOBJECT, COR_E_INVALIDCOMOBJECT,
                        COR_E_INVALIDOLEVARIANTTYPE, COR_E_INVALIDOLEVARIANTTYPE, COR_E_INVALIDOLEVARIANTTYPE,
                        COR_E_MARSHALDIRECTIVE, COR_E_MARSHALDIRECTIVE, COR_E_MARSHALDIRECTIVE,
                        COR_E_SAFEARRAYRANKMISMATCH, COR_E_SAFEARRAYRANKMISMATCH, COR_E_SAFEARRAYRANKMISMATCH,
                        COR_E_SAFEARRAYTYPEMISMATCH, COR_E_SAFEARRAYTYPEMISMATCH, COR_E_SAFEARRAYTYPEMISMATCH,
                        COR_E_SECURITY, COR_E_SECURITY, COR_E_SECURITY, COR_E_SECURITY, COR_E_SECURITY,
                        COR_E_VERIFICATION, COR_E_VERIFICATION, COR_E_VERIFICATION,
                    };

                    if (errors.Length != expected.Length) return -1;
                    for (int i = 0; i < errors.Length; i++)
                    {
                        if (errors[i].HResult != expected[i]) return i + 1;
                    }

                    return input;
                }

                private const int E_POINTER = unchecked((int)0x80004003);
                private const int E_NOTIMPL = unchecked((int)0x80004001);
                private const int COR_E_AMBIGUOUSIMPLEMENTATION = unchecked((int)0x8013106A);
                private const int COR_E_APPDOMAINUNLOADED = unchecked((int)0x80131014);
                private const int COR_E_APPLICATION = unchecked((int)0x80131600);
                private const int COR_E_BADIMAGEFORMAT = unchecked((int)0x8007000B);
                private const int COR_E_CANNOTUNLOADAPPDOMAIN = unchecked((int)0x80131015);
                private const int COR_E_CONTEXTMARSHAL = unchecked((int)0x80131504);
                private const int COR_E_DATAMISALIGNED = unchecked((int)0x80131541);
                private const int COR_E_DLLNOTFOUND = unchecked((int)0x80131524);
                private const int COR_E_DUPLICATEWAITOBJECT = unchecked((int)0x80131529);
                private const int COR_E_ENTRYPOINTNOTFOUND = unchecked((int)0x80131523);
                private const int COR_E_EXECUTIONENGINE = unchecked((int)0x80131506);
                private const int COR_E_FIELDACCESS = unchecked((int)0x80131507);
                private const int COR_E_INSUFFICIENTEXECUTIONSTACK = unchecked((int)0x80131578);
                private const int COR_E_INSUFFICIENTMEMORY = unchecked((int)0x8013153D);
                private const int COR_E_INVALIDCOMOBJECT = unchecked((int)0x80131527);
                private const int COR_E_INVALIDOLEVARIANTTYPE = unchecked((int)0x80131531);
                private const int COR_E_INVALIDPROGRAM = unchecked((int)0x8013153A);
                private const int COR_E_MARSHALDIRECTIVE = unchecked((int)0x80131535);
                private const int COR_E_MEMBERACCESS = unchecked((int)0x8013151A);
                private const int COR_E_METHODACCESS = unchecked((int)0x80131510);
                private const int COR_E_MISSINGFIELD = unchecked((int)0x80131511);
                private const int COR_E_MISSINGMEMBER = unchecked((int)0x80131512);
                private const int COR_E_MISSINGMETHOD = unchecked((int)0x80131513);
                private const int COR_E_MULTICASTNOTSUPPORTED = unchecked((int)0x80131514);
                private const int COR_E_NOTFINITENUMBER = unchecked((int)0x80131528);
                private const int COR_E_RANK = unchecked((int)0x80131517);
                private const int COR_E_RUNTIMEWRAPPED = unchecked((int)0x8013153E);
                private const int COR_E_SAFEARRAYRANKMISMATCH = unchecked((int)0x80131538);
                private const int COR_E_SAFEARRAYTYPEMISMATCH = unchecked((int)0x80131533);
                private const int COR_E_SECURITY = unchecked((int)0x8013150A);
                private const int COR_E_STACKOVERFLOW = unchecked((int)0x800703E9);
                private const int COR_E_TIMEOUT = unchecked((int)0x80131505);
                private const int COR_E_TYPEACCESS = unchecked((int)0x80131543);
                private const int COR_E_TYPEINITIALIZATION = unchecked((int)0x80131534);
                private const int COR_E_TYPELOAD = unchecked((int)0x80131522);
                private const int COR_E_TYPEUNLOADED = unchecked((int)0x80131013);
                private const int COR_E_UNAUTHORIZEDACCESS = unchecked((int)0x80070005);
                private const int COR_E_VERIFICATION = unchecked((int)0x8013150D);
            }
            """,
            TestAssets.RepositorySdkVersion,
            "latest",
            optimized);

        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ConcreteExceptionHResultFixture.EntryPoint",
            "Run",
            [],
            Target: target));

        Assert.Equal(
            42,
            ExecuteWithStandardWasiNode(
                compilation.ApplicationModule,
                assets.Directory,
                42,
                target,
                compilation.StaticDataEnd));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesPortableEnumAndHResultExceptionApis(
        bool optimized,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "PortableExceptionApiFixture",
            """
            namespace PortableExceptionApiFixture;

            public enum Mode { First, Second }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    int passed = 0;
                    var defaultError =
                        new System.ComponentModel.InvalidEnumArgumentException();
                    var inner = new System.Exception("inner");
                    var innerError =
                        new System.ComponentModel.InvalidEnumArgumentException(
                            "hello",
                            inner);
                    if (defaultError.HResult == unchecked((int)0x80070057) &&
                        defaultError.Message ==
                            "Value does not fall within the expected range." &&
                        innerError.Message == "hello" &&
                        object.ReferenceEquals(innerError.InnerException, inner)) passed |= 1;

                    var valueError =
                        new System.ComponentModel.InvalidEnumArgumentException(
                            "mode",
                            -7,
                            typeof(Mode));
                    if (valueError.ParamName == "mode" &&
                        valueError.HResult == unchecked((int)0x80070057) &&
                        valueError.Message ==
                            "The value of argument 'mode' (-7) is invalid for Enum type '" + typeof(Mode).Name +
                            "'. (Parameter 'mode')") passed |= 2;

                    System.Runtime.InteropServices.Marshal.ThrowExceptionForHR(0);
                    System.Runtime.InteropServices.Marshal.ThrowExceptionForHR(1);
                    passed |= 4;

                    if (Throws<System.ArgumentException>(unchecked((int)0x80070057))) passed |= 8;
                    if (Throws<System.NullReferenceException>(unchecked((int)0x80004003))) passed |= 16;
                    if (Throws<System.OutOfMemoryException>(unchecked((int)0x8007000E))) passed |= 32;
                    if (Throws<System.NotImplementedException>(unchecked((int)0x80004001))) passed |= 64;
                    if (Throws<System.FormatException>(unchecked((int)0x80131537))) passed |= 128;
                    if (ThrowsUnknown(unchecked((int)0x81234567))) passed |= 256;
                    if (Throws<System.UnauthorizedAccessException>(unchecked((int)0x80070005))) passed |= 512;
                    if (Throws<System.BadImageFormatException>(unchecked((int)0x8007000B))) passed |= 1024;
                    if (Throws<System.Collections.Generic.KeyNotFoundException>(unchecked((int)0x80131577))) passed |= 2048;
                    if (Throws<System.ArgumentOutOfRangeException>(unchecked((int)0x80131502))) passed |= 4096;
                    if (Throws<System.ArithmeticException>(unchecked((int)0x80070216))) passed |= 8192;
                    if (Throws<System.ArrayTypeMismatchException>(unchecked((int)0x80131503))) passed |= 16384;
                    if (Throws<System.DivideByZeroException>(unchecked((int)0x80020012))) passed |= 32768;
                    if (Throws<System.IndexOutOfRangeException>(unchecked((int)0x80131508))) passed |= 65536;
                    if (Throws<System.InvalidCastException>(unchecked((int)0x80004002))) passed |= 131072;
                    if (Throws<System.InvalidOperationException>(unchecked((int)0x80131509))) passed |= 262144;
                    if (Throws<System.NotSupportedException>(unchecked((int)0x80131515))) passed |= 524288;
                    if (Throws<System.ObjectDisposedException>(unchecked((int)0x80131622))) passed |= 1048576;
                    if (Throws<System.OperationCanceledException>(unchecked((int)0x8013153B))) passed |= 2097152;
                    if (Throws<System.OverflowException>(unchecked((int)0x80131516))) passed |= 4194304;
                    if (Throws<System.PlatformNotSupportedException>(unchecked((int)0x80131539))) passed |= 8388608;
                    return passed;
                }

                private static bool Throws<T>(int errorCode) where T : System.Exception
                {
                    try
                    {
                        System.Runtime.InteropServices.Marshal.ThrowExceptionForHR(errorCode);
                        return false;
                    }
                    catch (System.Exception error)
                    {
                        return error is T && error.HResult == errorCode;
                    }
                }

                private static bool ThrowsUnknown(int errorCode)
                {
                    try
                    {
                        System.Runtime.InteropServices.Marshal.ThrowExceptionForHR(errorCode);
                        return false;
                    }
                    catch (System.Runtime.InteropServices.COMException error)
                    {
                        return error.HResult == errorCode &&
                            error.Message == "Error 2166572391 (0x81234567)";
                    }
                }
            }
            """,
            TestAssets.RepositorySdkVersion,
            "latest",
            optimized);

        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "PortableExceptionApiFixture.EntryPoint",
            "Run",
            [],
            Target: target));

        Assert.Equal(
            16777215,
            ExecuteWithStandardWasiNode(
                compilation.ApplicationModule,
                assets.Directory,
                0,
                target,
                compilation.StaticDataEnd));
    }
}
