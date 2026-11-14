// From the compiler harness's ExpressionControlFlowCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
namespace ExpressionControlFlowFixture;

public sealed class Inner
{
    public int Value;
}

public sealed class Model
{
    // A constructor with an argument: C# then builds Expression.New from the ConstructorInfo
    // instead of looking the parameterless constructor up by type, which NetWasm cannot.
    public Model(int seed) => Child.Value = seed;
    public List<int> Items = new List<int>();
    public Inner Child = new Inner();
}

public sealed class NullGenerator : DebugInfoGenerator
{
    public override void MarkSequencePoint(LambdaExpression method, int ilOffset, DebugInfoExpression sequencePoint)
    {
    }
}

public sealed class IdentityVisitor : ExpressionVisitor
{
}

public static class EntryPoint
{
    private static bool Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
            return false;
        }
        catch (T)
        {
            return true;
        }
    }

    public static int Run(int input)
    {
        var x = Expression.Parameter(typeof(int), "x");
        var one = Expression.Constant(1);

        // Compound assignments carry their node type, reduce to an assignment and run through the interpreter.
        var addAssign = Expression.AddAssign(x, one);
        if (addAssign.NodeType != ExpressionType.AddAssign || addAssign.Type != typeof(int) || !addAssign.CanReduce ||
            addAssign.Reduce() is not BinaryExpression { NodeType: ExpressionType.Assign } ||
            addAssign.ToString() != "(x += 1)") return 1;
        if (Expression.MakeBinary(ExpressionType.LeftShiftAssign, x, one).ToString() != "(x <<= 1)" ||
            Expression.PowerAssign(Expression.Parameter(typeof(double), "d"), Expression.Constant(2.0)).Method?.Name != "Pow") return 2;
        if (Expression.Lambda<Func<int, int>>(addAssign, x).Compile()(input) != input + 1) return 3;
        if (!Throws<InvalidOperationException>(() => Expression.AddAssign(x, one, null,
            Expression.Lambda<Func<int, int>>(x, x)))) return 4;
        if (!Throws<ArgumentException>(() => Expression.AddAssign(one, one))) return 5;

        var pre = Expression.PreIncrementAssign(x);
        var post = Expression.PostIncrementAssign(x);
        if (pre.ToString() != "++x" || post.ToString() != "x++" || !post.CanReduce ||
            post.Reduce() is not BlockExpression) return 6;
        if (Expression.Lambda<Func<int, int>>(pre, x).Compile()(input) != input + 1 ||
            Expression.Lambda<Func<int, int>>(post, x).Compile()(input) != input) return 7;

        // Coalesce keeps its conversion lambda.
        var p = Expression.Parameter(typeof(int), "p");
        var convert = Expression.Lambda<Func<int, long>>(Expression.Convert(p, typeof(long)), p);
        var coalesce = Expression.Coalesce(Expression.Constant(null, typeof(int?)), Expression.Constant(0L), convert);
        if (coalesce.Conversion != convert || coalesce.Type != typeof(long)) return 8;

        // Throw, labels, goto, loop, switch and try.
        var rethrow = Expression.Rethrow();
        if (rethrow.NodeType != ExpressionType.Throw || rethrow.Operand != null || rethrow.Type != typeof(void)) return 9;
        if (!Throws<ArgumentException>(() => Expression.Throw(one))) return 10;
        var end = Expression.Label(typeof(void), "end");
        var loop = Expression.Loop(Expression.Break(end), end);
        if (loop.Type != typeof(void) || loop.BreakLabel != end || loop.ToString() != "loop { ... }" ||
            Expression.Break(end).ToString() != "break end" || Expression.Label(end).ToString() != "{ ... } end:") return 11;
        if (Expression.Goto(Expression.Label()).ToString() != "goto UnnamedLabel_0") return 12;
        if (!Throws<ArgumentException>(() => Expression.Goto(Expression.Label(typeof(int))))) return 13;

        var switchExpression = Expression.Switch(x, Expression.Constant("other"),
            Expression.SwitchCase(Expression.Constant("two"), Expression.Constant(2)));
        if (switchExpression.Type != typeof(string) || switchExpression.Cases.Count != 1 ||
            switchExpression.ToString() != "switch (x) { ... }" ||
            switchExpression.Cases[0].ToString() != "case (2): ...") return 14;
        if (!Throws<ArgumentException>(() => Expression.Switch(x,
            Expression.SwitchCase(Expression.Constant("two"), Expression.Constant(2))))) return 15;

        var error = Expression.Parameter(typeof(Exception), "error");
        var tryCatch = Expression.TryCatch(one, Expression.Catch(error, Expression.Constant(2)));
        if (tryCatch.Type != typeof(int) || tryCatch.Handlers.Count != 1 || tryCatch.ToString() != "try { ... }" ||
            // A handler's text names the caught type (issue 225: without type names a type's name reads its ID).
            tryCatch.Handlers[0].ToString() != "catch (" + typeof(Exception).Name + " error) { ... }") return 16;
        if (!Throws<ArgumentException>(() => Expression.MakeTry(null, one, null, one, new[] { Expression.Catch(typeof(Exception), one) })) ||
            !Throws<ArgumentException>(() => Expression.MakeTry(null, one, null, null, null))) return 17;

        // Runtime variables and debug information.
        var variables = Expression.RuntimeVariables(x);
        if (variables.Type != typeof(IRuntimeVariables) || variables.ToString() != "(x)") return 18;
        var document = Expression.SymbolDocument("a.cs");
        var debugInfo = Expression.DebugInfo(document, 1, 2, 3, 4);
        var cleared = Expression.ClearDebugInfo(document);
        if (debugInfo.IsClear || !cleared.IsClear || cleared.StartLine != 0xfeefee ||
            debugInfo.ToString() != "<DebugInfo(a.cs: 1, 2, 3, 4)>" || document.Language != Guid.Empty) return 19;
        if (!Throws<ArgumentException>(() => Expression.DebugInfo(document, 2, 1, 1, 1))) return 20;

        // Nested member initializers bind, print and reduce.
        Expression<Func<Model>> init = () => new Model(0) { Items = { 1, 2 }, Child = { Value = 3 } };
        var memberInit = (MemberInitExpression)init.Body;
        if (memberInit.Bindings[0] is not MemberListBinding { Initializers.Count: 2 } ||
            memberInit.Bindings[1] is not MemberMemberBinding { Bindings.Count: 1 } ||
            memberInit.Bindings[1].ToString() != "Child = {Value = 3}" ||
            memberInit.Reduce() is not BlockExpression) return 21;

        // The visitor walks every node and keeps an unchanged tree.
        var tree = Expression.Block(addAssign, pre, post, coalesce, loop, Expression.Label(end), switchExpression,
            tryCatch, variables, debugInfo, memberInit, Expression.TryFinally(one, rethrow));
        if (new IdentityVisitor().Visit(tree) != tree) return 22;

        // Delegate types that need no run-time instantiation resolve; Compile ignores a debug-info generator.
        if (Expression.GetActionType() != typeof(Action) || Expression.GetDelegateType(typeof(void)) != typeof(Action) ||
            Expression.TryGetFuncType(new Type[1], out _)) return 23;
        Expression<Func<int, int>> increment = value => value + 1;
        if (increment.Compile(new NullGenerator())(input) != input + 1 ||
            ((Func<int, int>)((LambdaExpression)increment).Compile(new NullGenerator()))(input) != input + 1) return 24;

        // GetFuncType builds a delegate type the program names, as this comparison does.
        if (Expression.GetFuncType(typeof(int)) != typeof(Func<int>)) return 25;
#if !DESKTOP_ORACLE
        // Expression<TDelegate> for a delegate type known only at run time needs runtime generic instantiation.
        if (!Throws<PlatformNotSupportedException>(() => Expression.Lambda(one))) return 27;
#else
        if (Expression.Lambda(one) is not Expression<Func<int>>) return 27;
#endif
#if !DESKTOP_ORACLE
        // The interpreter does not run a coalesce whose operand needs a conversion.
        if (!Throws<NotSupportedException>(() => Expression.Lambda<Func<int, long>>(
                Expression.Coalesce(Expression.Convert(x, typeof(int?)), Expression.Constant(0L), convert), x).Compile())) return 26;
#endif

        return input + 1;
    }
}
