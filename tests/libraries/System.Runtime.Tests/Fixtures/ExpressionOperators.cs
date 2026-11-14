// From the compiler harness's ExpressionOperatorsCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Linq.Expressions;
using System.Reflection;
namespace ExpressionOperatorsFixture;

public sealed class Money
{
    public int Value;
    public Money(int value) => Value = value;
    public static Money operator +(Money a, Money b) => new Money(a.Value + b.Value);
    public static Money operator -(Money a) => new Money(-a.Value);
}

public sealed class Probe
{
    public static int Calls;
    public Probe() { }
    public static int Touch(int value) { Calls++; return value; }
}

public struct Point
{
    public int X;
}

public enum Color
{
    Red,
    Green,
}

#pragma warning disable CS0618
public sealed class LegacyNode : Expression
{
    public LegacyNode() : base(ExpressionType.Extension, typeof(int)) { }
}
#pragma warning restore CS0618

public sealed class BareNode : Expression
{
}

public sealed class ConstantDoubler : ExpressionVisitor
{
    protected override Expression VisitConstant(ConstantExpression node) =>
        node.Value is int value ? Expression.Constant(value * 2) : node;
}

public static class EntryPoint
{
    static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; } catch (T) { return true; }
    }

    // The interpreter's rejection, not a PlatformNotSupportedException from construction.
    static bool Rejected(Action action)
    {
        try { action(); return false; }
        catch (NotSupportedException e) { return e.GetType() == typeof(NotSupportedException); }
    }

    static int Arithmetic(int x) => (x - 1) * 3 / 2 % 50 + (x & 6) - (x | 1) + (x ^ 3) + (x << 2) - (x >> 1) + -x + ~x + +x;

    static long Apply(ExpressionType kind, int a, int b) => kind switch
    {
        ExpressionType.Add => a + b,
        ExpressionType.AddChecked => checked(a + b),
        ExpressionType.Subtract => a - b,
        ExpressionType.SubtractChecked => checked(a - b),
        ExpressionType.Multiply => a * b,
        ExpressionType.MultiplyChecked => checked(a * b),
        ExpressionType.Divide => a / b,
        ExpressionType.Modulo => a % b,
        ExpressionType.And => a & b,
        ExpressionType.Or => a | b,
        ExpressionType.ExclusiveOr => a ^ b,
        ExpressionType.LeftShift => a << b,
        ExpressionType.RightShift => a >> b,
        ExpressionType.Equal => a == b ? 1 : 0,
        ExpressionType.NotEqual => a != b ? 1 : 0,
        ExpressionType.LessThan => a < b ? 1 : 0,
        ExpressionType.LessThanOrEqual => a <= b ? 1 : 0,
        ExpressionType.GreaterThan => a > b ? 1 : 0,
        _ => a >= b ? 1 : 0,
    };

    static long ApplyUnary(ExpressionType kind, int a) => kind switch
    {
        ExpressionType.Negate => -a,
        ExpressionType.NegateChecked => checked(-a),
        ExpressionType.UnaryPlus => +a,
        ExpressionType.Not => ~a,
        ExpressionType.OnesComplement => ~a,
        ExpressionType.Increment => a + 1,
        _ => a - 1,
    };

    public static int Run(int input)
    {
        // C#-compiled trees for built-in operators execute through Compile().
        Expression<Func<int, int>> arithmetic = x => (x - 1) * 3 / 2 % 50 + (x & 6) - (x | 1) + (x ^ 3) + (x << 2) - (x >> 1) + -x + ~x + +x;
        if (arithmetic.Compile()(input) != Arithmetic(input)) return 1;
        Expression<Func<long, bool>> comparisons = v => v >= 41 && v <= 41 && v != 40 || v > 100;
        if (!comparisons.Compile()(input) || comparisons.Compile()(40)) return 2;
        Expression<Func<double, double>> floating = d => d * 2.5 - d / 4 + d % 3;
        if (floating.Compile()(8.0) != 8.0 * 2.5 - 8.0 / 4 + 8.0 % 3) return 3;
        Expression<Func<bool, bool>> logic = b => !b | (b & true) ^ false || b;
        if (!logic.Compile()(true) || !logic.Compile()(false)) return 4;
        Expression<Func<int, bool>> shortCircuit = x => x > 100 && Probe.Touch(x) > 0 || x < 0 && Probe.Touch(x) > 0;
        if (shortCircuit.Compile()(input) || Probe.Calls != 0) return 5;

        // Nullable lifting and coalescing.
        Expression<Func<int?, int?>> lifted = n => n + 1 - n * 2;
        if (lifted.Compile()(input) != input + 1 - input * 2 || lifted.Compile()(null) != null) return 6;
        Expression<Func<int?, bool>> liftedCompare = n => n >= 41 && n != null;
        if (!liftedCompare.Compile()(input) || liftedCompare.Compile()(null)) return 7;
        Expression<Func<int?, int>> coalesce = n => n ?? -1;
        if (coalesce.Compile()(input) != input || coalesce.Compile()(null) != -1) return 8;
        Expression<Func<string?, string>> coalesceReference = s => s ?? "none";
        if (coalesceReference.Compile()("a") != "a" || coalesceReference.Compile()(null) != "none") return 9;
        if (coalesce.Body.NodeType != ExpressionType.Coalesce || ((BinaryExpression)lifted.Body).IsLiftedToNull != true) return 10;

        // Checked operators overflow.
        Expression<Func<int, int>> checkedMultiply = x => checked(x * 1000000000);
        if (checkedMultiply.Body.NodeType != ExpressionType.MultiplyChecked || !Throws<OverflowException>(() => checkedMultiply.Compile()(input))) return 11;
        Expression<Func<long, int>> checkedConvert = v => checked((int)v);
        if (checkedConvert.Body.NodeType != ExpressionType.ConvertChecked || checkedConvert.Compile()(input) != input ||
            !Throws<OverflowException>(() => checkedConvert.Compile()(long.MaxValue))) return 12;

        // Operators that carry a MethodInfo run as calls: string concatenation and user-defined operators.
        Expression<Func<string, string>> concat = s => s + "!";
        if (((BinaryExpression)concat.Body).Method == null || concat.Compile()("hi") != "hi!") return 13;
        Expression<Func<Money, Money>> userOperators = m => m + -m + m;
        if (((BinaryExpression)userOperators.Body).Method == null || userOperators.Compile()(new Money(input)).Value != input) return 14;

        // C# compiles new on a struct without a parameterless constructor to New(Type), which
        // represents the default value; the interpreter executes it for admitted value types.
        var y = Expression.Parameter(typeof(int), "y");
        var created = Expression.New(typeof(int));
        if (created.Constructor != null || created.Type != typeof(int) ||
            Expression.Lambda<Func<int, int>>(Expression.Add(y, created), y).Compile()(input) != input) return 15;
        Expression<Func<int, Point>> structNew = x => new Point();
        if (structNew.Body is not NewExpression { Constructor: null } pointNew || pointNew.Type != typeof(Point)) return 16;

        // Hand-built factories, with MakeBinary and MakeUnary across the operator matrix.
        var x = Expression.Parameter(typeof(int), "x");
        var three = Expression.Constant(3);
        ExpressionType[] arithmeticKinds =
        {
            ExpressionType.Add, ExpressionType.AddChecked, ExpressionType.Subtract, ExpressionType.SubtractChecked,
            ExpressionType.Multiply, ExpressionType.MultiplyChecked, ExpressionType.Divide, ExpressionType.Modulo,
            ExpressionType.And, ExpressionType.Or, ExpressionType.ExclusiveOr, ExpressionType.LeftShift, ExpressionType.RightShift,
        };
        foreach (var kind in arithmeticKinds)
        {
            var made = Expression.MakeBinary(kind, x, three);
            if (made.NodeType != kind || made.Type != typeof(int) || made.Method != null || made.IsLifted) return 17;
            if (Expression.Lambda<Func<int, int>>(made, x).Compile()(input) != Apply(kind, input, 3)) return 18;
        }
        ExpressionType[] comparisonKinds =
        {
            ExpressionType.Equal, ExpressionType.NotEqual, ExpressionType.LessThan,
            ExpressionType.LessThanOrEqual, ExpressionType.GreaterThan, ExpressionType.GreaterThanOrEqual,
        };
        foreach (var kind in comparisonKinds)
        {
            var made = Expression.MakeBinary(kind, x, Expression.Constant(41), false, null);
            if (made.NodeType != kind || made.Type != typeof(bool)) return 19;
            if ((Expression.Lambda<Func<int, bool>>(made, x).Compile()(input) ? 1 : 0) != Apply(kind, input, 41)) return 20;
        }
        ExpressionType[] unaryKinds =
        {
            ExpressionType.Negate, ExpressionType.NegateChecked, ExpressionType.UnaryPlus, ExpressionType.Not,
            ExpressionType.OnesComplement, ExpressionType.Increment, ExpressionType.Decrement,
        };
        foreach (var kind in unaryKinds)
        {
            var made = Expression.MakeUnary(kind, x, typeof(int));
            if (made.NodeType != kind || made.Type != typeof(int) || made.IsLifted) return 21;
            if (Expression.Lambda<Func<int, int>>(made, x).Compile()(input) != ApplyUnary(kind, input)) return 22;
        }
        if (!Throws<OverflowException>(() => Expression.Lambda<Func<int, int>>(Expression.NegateChecked(x), x).Compile()(int.MinValue))) return 23;

        // Lifted factories, IsTrue/IsFalse and comparisons lifted to null.
        var n = Expression.Parameter(typeof(int?), "n");
        var liftedAdd = Expression.Add(n, Expression.Convert(Expression.Constant(1), typeof(int?)));
        if (!liftedAdd.IsLifted || !liftedAdd.IsLiftedToNull || liftedAdd.Type != typeof(int?)) return 24;
        var liftedEqual = Expression.Equal(n, Expression.Constant(null, typeof(int?)));
        if (!liftedEqual.IsLifted || liftedEqual.IsLiftedToNull || liftedEqual.Type != typeof(bool)) return 25;
        var toNull = Expression.LessThanOrEqual(n, Expression.Convert(Expression.Constant(41), typeof(int?)), true, null);
        var toNullTree = Expression.Lambda<Func<int?, bool?>>(toNull, n).Compile();
        if (toNull.Type != typeof(bool?) || toNullTree(input) != true || toNullTree(null) != null) return 26;
        var notEqualNull = Expression.Lambda<Func<int?, bool>>(Expression.NotEqual(n, Expression.Constant(null, typeof(int?))), n).Compile();
        if (!notEqualNull(input) || notEqualNull(null)) return 27;
        var b = Expression.Parameter(typeof(bool), "b");
        var truth = Expression.Lambda<Func<bool, bool>>(Expression.AndAlso(Expression.IsTrue(b), Expression.IsFalse(Expression.Not(b))), b).Compile();
        if (!truth(true) || truth(false)) return 28;
        var nb = Expression.Parameter(typeof(bool?), "nb");
        var threeValued = Expression.Lambda<Func<bool?, bool?>>(Expression.And(nb, Expression.Convert(Expression.Constant(false), typeof(bool?))), nb).Compile();
        if (threeValued(null) != false || threeValued(true) != false) return 29;
        var liftedNot = Expression.Lambda<Func<bool?, bool?>>(Expression.Not(nb), nb).Compile();
        if (liftedNot(null) != null || liftedNot(true) != false) return 30;

        // Power uses Math.Pow; Coalesce, reference equality and checked conversion factories.
        var d = Expression.Parameter(typeof(double), "d");
        var power = Expression.Power(d, Expression.Constant(2.0));
        if (power.Method == null) return 31;
        if (power.Method.Name != "Pow") return 51;
        if (Expression.Lambda<Func<double, double>>(power, d).Compile()(3.0) != Math.Pow(3.0, 2.0)) return 52;
        var s = Expression.Parameter(typeof(string), "s");
        var other = new string('a', 2);
        var valueEqual = Expression.Equal(s, Expression.Constant(other));
        if (!Expression.Lambda<Func<string, bool>>(valueEqual, s).Compile()(new string('a', 2)) ||
            !Expression.Lambda<Func<string, bool>>(valueEqual.Update(s, null, Expression.Constant(new string('a', 2))), s).Compile()(new string('a', 2))) return 32;
        var referenceEqual = Expression.ReferenceEqual(s, Expression.Constant(other));
        if (referenceEqual.Method != null || Expression.Lambda<Func<string, bool>>(referenceEqual, s).Compile()(new string('a', 2)) ||
            !Expression.Lambda<Func<string, bool>>(Expression.ReferenceNotEqual(s, Expression.Constant(other)), s).Compile()(new string('a', 2))) return 33;
        var coalesced = Expression.Coalesce(n, Expression.Constant(7));
        if (coalesced.Type != typeof(int) || coalesced.IsLifted || Expression.Lambda<Func<int?, int>>(coalesced, n).Compile()(null) != 7) return 34;
        var narrow = Expression.Lambda<Func<int, byte>>(Expression.ConvertChecked(x, typeof(byte)), x).Compile();
        if (narrow(input) != 41 || !Throws<OverflowException>(() => narrow(300))) return 35;
        if (Expression.ConvertChecked(s, typeof(object)).NodeType != ExpressionType.Convert) return 36;

        // Typed blocks and conditionals, IfThen and IfThenElse.
        var v = Expression.Variable(typeof(int), "v");
        var statements = Expression.Block(new[] { v },
            Expression.IfThenElse(Expression.GreaterThan(x, Expression.Constant(10)), Expression.Assign(v, Expression.Constant(1)), Expression.Assign(v, Expression.Constant(2))),
            Expression.IfThen(Expression.Equal(x, Expression.Constant(41)), Expression.Assign(v, Expression.Add(v, Expression.Constant(10)))),
            Expression.Block(typeof(void), Expression.Assign(v, Expression.Increment(v))),
            v);
        if (Expression.Lambda<Func<int, int>>(statements, x).Compile()(input) != 12) return 37;
        var typedBlock = Expression.Block(typeof(object), Expression.Constant("s"));
        var typedCondition = Expression.Condition(Expression.GreaterThan(x, Expression.Constant(0)), Expression.Constant("a"), Expression.Constant(null, typeof(object)), typeof(object));
        if (typedBlock.Type != typeof(object) || typedCondition.Type != typeof(object) ||
            (string?)Expression.Lambda<Func<int, object>>(typedCondition, x).Compile()(input) != "a" ||
            (string?)Expression.Lambda<Func<int, object>>(typedBlock, x).Compile()(input) != "s") return 38;
        var ifThen = Expression.IfThen(Expression.Constant(true), Expression.Empty());
        if (ifThen.Type != typeof(void) || ifThen.IfFalse.NodeType != ExpressionType.Default ||
            Expression.Block(typeof(void)).Type != typeof(void)) return 39;

        // Validation errors keep upstream's exception types.
        var max = ((MethodCallExpression)((Expression<Func<int, int>>)(q => Math.Max(q, q))).Body).Method;
        if (!Throws<InvalidOperationException>(() => Expression.Subtract(x, Expression.Constant(1L))) ||
            !Throws<InvalidOperationException>(() => Expression.And(d, d)) ||
            !Throws<InvalidOperationException>(() => Expression.Add(Expression.Constant((byte)1), Expression.Constant((byte)2))) ||
            !Throws<InvalidOperationException>(() => Expression.OnesComplement(d)) ||
            !Throws<InvalidOperationException>(() => Expression.Coalesce(x, x)) ||
            !Throws<InvalidOperationException>(() => Expression.ReferenceEqual(x, x)) ||
            !Throws<InvalidOperationException>(() => Expression.OrElse(x, x))) return 40;
        if (!Throws<ArgumentException>(() => Expression.Condition(x, x, x, typeof(int))) ||
            !Throws<ArgumentException>(() => Expression.Block(typeof(string), Expression.Constant(1))) ||
            !Throws<ArgumentException>(() => Expression.MakeBinary((ExpressionType)999, x, x)) ||
            !Throws<ArgumentException>(() => Expression.MakeUnary((ExpressionType)999, x, typeof(int))) ||
            !Throws<ArgumentException>(() => Expression.Lambda(typeof(int), x, x)) ||
#if DESKTOP_ORACLE
            !Throws<ArgumentException>(() => Expression.Lambda(typeof(Func<int, int>), x)) ||
#else
            // A delegate Type known only at run time has no Invoke metadata on NetWasm, so the lambda is rejected
            // before its parameters are checked.
            !Throws<PlatformNotSupportedException>(() => Expression.Lambda(typeof(Func<int, int>), x)) ||
#endif
            !Throws<ArgumentException>(() => Expression.New(typeof(void))) ||
            !Throws<ArgumentException>(() => Expression.Negate(x, max))) return 41;

        // Inspection, Update, visiting and formatting.
        var subtract = Expression.Subtract(x, Expression.Constant(1));
        if (subtract.ToString() != "(x - 1)" || Expression.Not(b).ToString() != "Not(b)" ||
            Expression.Coalesce(s, Expression.Constant("t")).ToString() != "(s ?? \"t\")" ||
            // A conversion's text names the target type (issue 225: without type names a type's name reads its ID).
            Expression.ConvertChecked(x, typeof(long)).ToString() != "ConvertChecked(x, " + typeof(long).Name + ")") return 43;
        if (!ReferenceEquals(subtract.Update(subtract.Left, null, subtract.Right), subtract) ||
            subtract.Update(three, null, subtract.Right).NodeType != ExpressionType.Subtract) return 44;
        var doubled = (Expression<Func<int, int>>)new ConstantDoubler().Visit(Expression.Lambda<Func<int, int>>(Expression.Multiply(Expression.Increment(x), three), x));
        if (doubled.Compile()(input) != (input + 1) * 6) return 45;
        if (new LegacyNode().NodeType != ExpressionType.Extension || new LegacyNode().Type != typeof(int) ||
            !Throws<InvalidOperationException>(() => _ = new BareNode().NodeType)) return 46;

        // Anonymous types keep their members.
        Expression<Func<int, object>> anonymous = value => new { A = value, B = value + 1 };
        var anonymousNew = anonymous.Body as NewExpression ?? (NewExpression)((UnaryExpression)anonymous.Body).Operand;
        if (anonymousNew.Members == null || anonymousNew.Members.Count != 2 || anonymousNew.Members[0] is not PropertyInfo member ||
            member.PropertyType != typeof(int)) return 47;
        if (Expression.Equal(Expression.Constant(Color.Red), Expression.Constant(Color.Green)).Type != typeof(bool)) return 48;

#if !DESKTOP_ORACLE
        // NetWasm cannot find string's operator by name, so string equality keeps Method null
        // and compares values.
        if (valueEqual.Method != null) return 54;
        // NetWasm boundaries: operator and true/false lookup by name need runtime member discovery;
        // Expression<TDelegate> for a run-time delegate Type needs runtime generic instantiation.
        var money = Expression.Parameter(typeof(Money), "m");
        if (!Throws<PlatformNotSupportedException>(() => Expression.Add(money, money)) ||
            !Throws<PlatformNotSupportedException>(() => Expression.Negate(money)) ||
            !Throws<PlatformNotSupportedException>(() => Expression.AndAlso(money, money, ((BinaryExpression)userOperators.Body).Method)) ||
            !Throws<PlatformNotSupportedException>(() => Expression.Lambda(typeof(Func<int, int>), x, x))) return 49;
        // New(typeof(C)) of a class, as C# builds new C(), gets C's constructor from the compiler.
        if (Expression.New(typeof(Probe)).Constructor == null) return 56;
        // The interpreter rejects valid trees outside its profile before running anything: enum equality without an
        // operator method.
        if (!Rejected(() => Expression.Lambda<Func<int, bool>>(Expression.Equal(Expression.Constant(Color.Red), Expression.Constant(Color.Green)), x).Compile())) return 55;
#else
        if (valueEqual.Method == null) return 54;
        var money = Expression.Parameter(typeof(Money), "m");
        if (Expression.Add(money, money).Method == null || Expression.New(typeof(Probe)).Constructor == null ||
            Expression.Lambda(typeof(Func<int, int>), x, x) is not Expression<Func<int, int>>) return 49;
#endif
        // Decimal operators (C# builds them without their MethodInfo) and a struct's New(Type) run, as on desktop.
        Expression<Func<decimal, decimal>> money2 = m => m + 1m;
        if (money2.Compile()(1m) != 2m || structNew.Compile()(1).X != 0) return 50;
        return input + 1;
    }
}
