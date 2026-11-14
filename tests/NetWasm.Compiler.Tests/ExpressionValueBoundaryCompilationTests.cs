using System.Linq.Expressions;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class ExpressionValueBoundaryCompilationTests
{
    [Fact]
    public void DesktopEqualityIgnoresLiftFlagWhenNoLiftingOccurs()
    {
        var value = Expression.Parameter(typeof(int), "value");
        var builtIn = Expression.Equal(
            value,
            Expression.Constant(42),
            liftToNull: true,
            method: null);
        Expression<Func<int, bool>> carrier = candidate => Same(candidate, 42);
        var method = ((MethodCallExpression)carrier.Body).Method;
        var explicitMethod = Expression.Equal(
            value,
            Expression.Constant(42),
            liftToNull: true,
            method);

        Assert.Equal(typeof(bool), builtIn.Type);
        Assert.False(builtIn.IsLifted);
        Assert.False(builtIn.IsLiftedToNull);
        Assert.Equal(typeof(bool), explicitMethod.Type);
        Assert.False(explicitMethod.IsLifted);
        Assert.False(explicitMethod.IsLiftedToNull);
    }

    [Fact]
    public void DesktopAcceptsNullableStructIdentity()
    {
        Expression<Func<Unsupported?, Unsupported?>> identity = value => value;

        Assert.NotNull(identity.Compile()(new Unsupported(42)));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void InterpreterValueBoundaryIsFailClosed(
        bool optimized,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = optimized
            ? assets.CompileOptimizedSource("ExpressionValueBoundary", Source)
            : assets.CompileSource("ExpressionValueBoundary", Source);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ExpressionValueBoundary.EntryPoint",
            "Run",
            [],
            Target: target));

        Assert.Equal(42, ExecuteWithStandardWasiNode(
            compilation.ApplicationModule,
            assets.Directory,
            41,
            target,
            compilation.StaticDataEnd));
    }

    private static bool Same(int left, int right) => left == right;

    private readonly record struct Unsupported(int Value);

    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Linq.Expressions;

        namespace ExpressionValueBoundary;

        public readonly struct Unsupported
        {
            public Unsupported(int value) => Value = value;
            public int Value { get; }
        }

        public enum Code : long
        {
            None,
            High = 1L << 40,
        }

        public sealed class Model
        {
            public Unsupported? Value { get; set; }
        }

        public static class EntryPoint
        {
            private static bool Same(int left, int right) => left == right;

            private static int AcceptNullable(int? value) => value ?? 0;

            private static bool SameNullable(int? left, int? right) =>
                left == right;

            public static int Run(int input)
            {
                if (!LambdaExpression.CanInterpret || LambdaExpression.CanCompileToIL)
                    return 8;

                var rejected = 0;

                // Any value type is a value: a struct, DateTime and their nullable forms run as parameters, results,
                // locals and defaults.
                var unsupported = Expression.Parameter(typeof(Unsupported?), "value");
                var structIdentity = Expression.Lambda<Func<Unsupported?, Unsupported?>>(
                    unsupported,
                    unsupported).Compile();
                if (structIdentity(new Unsupported(input)) is not { Value: var held } || held != input ||
                    structIdentity(null) is not null)
                    return 9;

                var date = Expression.Parameter(typeof(DateTime?), "value");
                var dateIdentity = Expression.Lambda<Func<DateTime?, DateTime?>>(
                    date,
                    date).Compile();
                if (dateIdentity(new DateTime(input)) != new DateTime(input) || dateIdentity(null) is not null)
                    return 10;

                var integer = Expression.Parameter(typeof(int), "value");
                var local = Expression.Variable(typeof(Unsupported?), "local");
                if (Expression.Lambda<Func<int, int>>(
                        Expression.Block([local], integer),
                        integer).Compile()(input) != input)
                    return 11;

                var unselected = Expression.Condition(
                    Expression.Constant(true),
                    integer,
                    Expression.Block(
                        Expression.Default(typeof(Unsupported?)),
                        integer));
                if (Expression.Lambda<Func<int, int>>(
                        unselected,
                        integer).Compile()(input) != input)
                    return 12;

                Expression<Func<Model, Unsupported?>> member = model => model.Value;
                try
                {
                    _ = member.Compile();
                }
                catch (NotSupportedException)
                {
                    rejected++;
                }

                Expression<Func<int, int>> nullableArgument = value =>
                    AcceptNullable(value);
                try
                {
                    _ = nullableArgument.Compile();
                }
                catch (NotSupportedException)
                {
                    rejected++;
                }

                // Enum equality builds, as upstream; the interpreter rejects it.
                var code = Expression.Parameter(typeof(Code), "code");
                try
                {
                    _ = Expression.Lambda<Func<Code, bool>>(
                        Expression.Equal(code, code),
                        code).Compile();
                }
                catch (NotSupportedException)
                {
                    rejected++;
                }

                var nullableCode = Expression.Parameter(typeof(Code?), "code");
                try
                {
                    _ = Expression.Lambda<Func<Code?, bool>>(
                        Expression.Equal(nullableCode, nullableCode),
                        nullableCode).Compile();
                }
                catch (NotSupportedException)
                {
                    rejected++;
                }

                Expression<Func<int, bool>> nullableMethodCarrier = value =>
                    SameNullable(value, value);
                var nullableMethod =
                    ((MethodCallExpression)nullableMethodCarrier.Body).Method;
                var nullableInteger = Expression.Convert(integer, typeof(int?));
                try
                {
                    _ = Expression.Lambda<Func<int, bool>>(
                        Expression.Equal(
                            nullableInteger,
                            nullableInteger,
                            liftToNull: false,
                            nullableMethod),
                        integer).Compile();
                }
                catch (NotSupportedException)
                {
                    rejected++;
                }

                if (rejected != 5)
                    return 1;

                Expression<Func<Code, Code>> enumIdentity = value => value;
                if (enumIdentity.Compile()(Code.High) != Code.High)
                    return 2;

                Expression<Func<Code?, Code?>> nullableEnumIdentity = value => value;
                var nullableEnum = nullableEnumIdentity.Compile();
                if (nullableEnum(null) is not null || nullableEnum(Code.High) != Code.High)
                    return 3;

                var builtIn = Expression.Equal(
                    integer,
                    Expression.Constant(input),
                    liftToNull: true,
                    method: null);
                if (builtIn.Type != typeof(bool) || builtIn.IsLifted ||
                    builtIn.IsLiftedToNull ||
                    !Expression.Lambda<Func<int, bool>>(builtIn, integer)
                        .Compile()(input))
                    return 4;

                Expression<Func<int, bool>> methodCarrier = value => Same(value, input);
                var method = ((MethodCallExpression)methodCarrier.Body).Method;
                var explicitMethod = Expression.Equal(
                    integer,
                    Expression.Constant(input),
                    liftToNull: true,
                    method);
                if (explicitMethod.Type != typeof(bool) || explicitMethod.IsLifted ||
                    explicitMethod.IsLiftedToNull ||
                    !Expression.Lambda<Func<int, bool>>(explicitMethod, integer)
                        .Compile()(input))
                    return 5;

                var forwardedArrayCall = Expression.Call(
                    method,
                    integer,
                    Expression.Constant(input));
                if (!Expression.Lambda<Func<int, bool>>(
                        forwardedArrayCall,
                        integer).Compile()(input))
                    return 9;

                IEnumerable<Expression> forwardedArguments =
                    new Expression[] { integer, Expression.Constant(input) };
                var forwardedEnumerableCall = Expression.Call(
                    method,
                    forwardedArguments);
                if (!Expression.Lambda<Func<int, bool>>(
                        forwardedEnumerableCall,
                        integer).Compile()(input))
                    return 10;

                Expression<Func<double, bool>> nan = value => value == double.NaN;
                if (nan.Compile()(double.NaN))
                    return 6;

                Expression<Func<double, bool>> zero = value => value == 0D;
                if (!zero.Compile()(-0D))
                    return 7;

                return 42;
            }
        }
        """;
}
