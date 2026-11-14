using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// new D(M).Method names M's MethodInfo when the compiler sees M (DelegateMethodRewriter): the same descriptor an
// expression lambda gets from ldtoken, per instantiation, for methods of generic types too. A delegate whose
// target the compiler cannot see keeps Delegate.Method's PlatformNotSupportedException.
public sealed class DelegateMethodCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ADelegateOfAKnownMethodNamesItsMethodInfo(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var application = assets.CompileSource("DelegateMethodProbe", Source);
        var result = NetWasmCompiler.Compile(new(application, [assets.CoreLib, .. TestAssets.FrameworkAssemblies(assets.Root)],
            "DelegateMethodProbe.EntryPoint", "Run", [new("run", "DelegateMethodProbe.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform@1.0.0/platform"));

        Assert.Equal(42, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 41, target, result.StaticDataEnd));
    }

    // Queryable's operators build their expression with new Func<...>(Operator).Method, so a custom provider
    // receives a well-formed query tree. This provider reads the tree itself (Where x > k, Select x * m, Sum).
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void QueryableBuildsTheQueryTreeACustomProviderReads(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var application = assets.CompileSource("QueryableProbe", QueryableSource);
        var result = NetWasmCompiler.Compile(new(application, [assets.CoreLib, .. TestAssets.FrameworkAssemblies(assets.Root)],
            "QueryableProbe.EntryPoint", "Run", [new("run", "QueryableProbe.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform@1.0.0/platform"));

        Assert.Equal(42, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string QueryableSource = """
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using System.Linq;
        using System.Linq.Expressions;
        namespace QueryableProbe;
        public sealed class Query<T> : IQueryable<T>
        {
            private readonly Provider _provider;
            public Query(Provider provider, Expression? expression = null) { _provider = provider; Expression = expression ?? Expression.Constant(this); }
            public Type ElementType => typeof(T);
            public Expression Expression { get; }
            public IQueryProvider Provider => _provider;
            public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_provider.Execute(Expression)!).GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
        public sealed class Provider : IQueryProvider
        {
            private readonly int[] _data;
            public Provider(int[] data) => _data = data;
            public IQueryable CreateQuery(Expression expression) => throw new NotSupportedException();
            public IQueryable<TElement> CreateQuery<TElement>(Expression expression) => new Query<TElement>(this, expression);
            public object? Execute(Expression expression) => Evaluate(expression);
            public TResult Execute<TResult>(Expression expression) => (TResult)Evaluate(expression)!;
            private object Evaluate(Expression expression) => expression switch
            {
                ConstantExpression => _data,
                MethodCallExpression { Method.Name: "Where" } call =>
                    Sequence(call).Where(x => x > Operand(call)),
                MethodCallExpression { Method.Name: "Select" } call =>
                    Sequence(call).Select(x => x * Operand(call)),
                MethodCallExpression { Method.Name: "Sum" } call => Sequence(call).Sum(),
                _ => throw new NotSupportedException(),
            };
            private IEnumerable<int> Sequence(MethodCallExpression call) => (IEnumerable<int>)Evaluate(call.Arguments[0]);
            // The constant on the right of the quoted lambda's body: k in x > k, m in x * m.
            private static int Operand(MethodCallExpression call) =>
                (int)((ConstantExpression)((BinaryExpression)((LambdaExpression)((UnaryExpression)call.Arguments[1]).Operand).Body).Right).Value!;
        }
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var numbers = new Query<int>(new Provider([1, 2, 3, 4, 5]));
                var query = numbers.Where(x => x > 2).Select(x => x * 10);
                var checks = 0;
                if (query.Expression is MethodCallExpression { Method.Name: "Select" } select &&
                    select.Method.DeclaringType == typeof(Queryable) &&
                    select.Arguments[0] is MethodCallExpression { Method.Name: "Where", Arguments.Count: 2 }) checks++;
                if (query.Sum() == 120) checks++;
                if (query.ToList() is [30, 40, 50]) checks++;
                var strings = new Query<string>(new Provider([]));
                if (!ReferenceEquals(((MethodCallExpression)numbers.Where(x => x > 0).Expression).Method,
                    ((MethodCallExpression)strings.Where(s => s.Length > 0).Expression).Method)) checks++;
                return checks == 4 ? input + 1 : -checks;
            }
        }
        """;

    private const string Source = """
        using System;
        using System.Linq.Expressions;
        using System.Reflection;
        namespace DelegateMethodProbe;
        public static class Box<T>
        {
            public static T Get() => default!;
        }
        public static class EntryPoint
        {
            public static int Square(int x) => x * x;
            public static T Identity<T>(T value) => value;
            private static MethodInfo FromExpression(Expression<Func<int, int>> lambda) => ((MethodCallExpression)lambda.Body).Method;
            private static MethodInfo Generic<T>() => new Func<T, T>(Identity<T>).Method;
            private static int Opaque(Func<int, int> function)
            {
                try { return function.Method is null ? 0 : -100; }
                catch (PlatformNotSupportedException) { return 1; }
            }
            public static int Run(int input)
            {
                var square = new Func<int, int>(Square).Method;
                var checks = 0;
                if (square.Name == "Square") checks++;
                if (square.IsStatic) checks++;
                if (ReferenceEquals(square, FromExpression(x => Square(x)))) checks++;
                if (Generic<int>().Name == "Identity" && !ReferenceEquals(Generic<int>(), Generic<string>())) checks++;
                if (ReferenceEquals(Generic<int>(), Generic<int>())) checks++;
                if (new Func<int>(Box<int>.Get).Method.Name == "Get") checks++;
                checks += Opaque(Square);
                return checks == 7 ? input + 1 : -checks;
            }
        }
        """;
}
