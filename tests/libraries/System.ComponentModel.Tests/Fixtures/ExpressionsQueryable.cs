// From the compiler harness's ExpressionsQueryableCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Dynamic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
namespace ExpressionsQueryableFixture;

public sealed class Model
{
    public int Field;
    public int Other;
    public Model(int value) => Field = value;
    public int Property { get; set; }
    public int Get() => Field;
    public int Sum(int a, int b) => Field + a + b;
    public int Sum(int a, int b, int c) => Field + a + b + c;
    public static int Sum(int a) => a;
    public static int Sum(int a, int b, int c, int d) => a + b + c + d;
    public static int Sum(int a, int b, int c, int d, int e) => a + b + c + d + e;
}

sealed class Stranger
{
    public int Field;
    public Stranger(int value) => Field = value;
}

sealed class Binder : CallSiteBinder
{
    public override Expression Bind(object[] args, ReadOnlyCollection<ParameterExpression> parameters, LabelTarget returnLabel) =>
        Expression.Constant(args.Length + parameters.Count);

    public void Cache(Func<CallSite, int> target) => CacheTarget(target);
}

sealed class Variables : IRuntimeVariables
{
    private readonly object?[] _values = new object?[2];
    public int Count => _values.Length;
    public object? this[int index] { get => _values[index]; set => _values[index] = value; }
}

// Rewrites every int constant to ten times its value and records the node kinds it visits.
sealed class Rewriter : ExpressionVisitor
{
    public string Visited = "";
    protected override Expression VisitConstant(ConstantExpression node) =>
        node.Value is int value ? Expression.Constant(value * 10) : node;
    protected override Expression VisitTypeBinary(TypeBinaryExpression node) { Visited += "T"; return base.VisitTypeBinary(node); }
    protected override Expression VisitIndex(IndexExpression node) { Visited += "I"; return base.VisitIndex(node); }
    protected override Expression VisitListInit(ListInitExpression node) { Visited += "L"; return base.VisitListInit(node); }
    protected override Expression VisitMemberInit(MemberInitExpression node) { Visited += "M"; return base.VisitMemberInit(node); }
    protected override ElementInit VisitElementInit(ElementInit node) { Visited += "E"; return base.VisitElementInit(node); }
    protected override MemberBinding VisitMemberBinding(MemberBinding node) { Visited += "B"; return base.VisitMemberBinding(node); }
}

public static class EntryPoint
{
    static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; } catch (T) { return true; }
    }

    public static int Run(int input)
    {
        // AsQueryable wraps an enumerable in an EnumerableQuery that is its own provider.
        var list = new List<int> { 4, 5, 6 };
        IQueryable<int> query = list.AsQueryable();
        if (query is not EnumerableQuery<int> || query.ElementType != typeof(int)) return 1;
        if (!ReferenceEquals(query.AsQueryable(), query) || !ReferenceEquals(((IEnumerable)query).AsQueryable(), query)) return 2;
        IQueryProvider provider = query.Provider;
        if (!ReferenceEquals(provider, query) || query is not IOrderedQueryable<int> ordered || ordered.ElementType != typeof(int)) return 3;
        if (query.Expression is not ConstantExpression source || !ReferenceEquals(source.Value, query)) return 4;
        int sum = 0, count = 0;
        foreach (int value in query) sum += value;
        foreach (object value in (IEnumerable)query) count++;
        if (sum != 15 || count != 3 || query.ToString() != list.ToString()) return 5;
        IQueryable<int> created = provider.CreateQuery<int>(query.Expression);
        if (created is not EnumerableQuery<int> || !ReferenceEquals(created.Expression, query.Expression) ||
            created.ToString() != query.Expression.ToString()) return 6;
        if (!Throws<ArgumentException>(() => provider.CreateQuery<string>(query.Expression))) return 7;
        if (!Throws<ArgumentNullException>(() => provider.CreateQuery<int>(null!))) return 8;
        if (!Throws<ArgumentNullException>(() => Queryable.AsQueryable<int>(null!))) return 9;
        if (new EnumerableQuery<int>(Expression.Constant(list)).ToString() != Expression.Constant(list).ToString()) return 10;
        _ = new EnumerableExecutor<int>(Expression.Constant(1));

        // Call-site binders keep their caller overrides, null BindDelegate and rule cache.
        var binder = new Binder();
        var noParameters = new ReadOnlyCollection<ParameterExpression>(new ParameterExpression[0]);
        if (binder.Bind(new object[] { 1, 2 }, noParameters, null!) is not ConstantExpression bound || (int)bound.Value! != 2) return 11;
        if (binder.BindDelegate<Func<CallSite, int>>(null!, new object[0]) != null) return 12;
        binder.Cache(site => 1);
        binder.Cache(site => 2);
        if (!Throws<ArgumentNullException>(() => CallSite.Create(null!, binder))) return 13;
        if (!Throws<ArgumentException>(() => CallSite.Create(typeof(string), binder))) return 14;
        if (!Throws<ArgumentException>(() => CallSite<string>.Create(binder)) || !Throws<ArgumentNullException>(() => CallSite<Func<CallSite, int>>.Create(null!))) return 15;
        if (!Throws<PlatformNotSupportedException>(() => DebugInfoGenerator.CreatePdbGenerator())) return 16;

        // CallInfo compares by count and names.
        var info = new CallInfo(3, "a", "b");
        if (info.ArgumentCount != 3 || info.ArgumentNames.Count != 2 || info.ArgumentNames[1] != "b") return 17;
        if (!info.Equals(new CallInfo(3, new List<string> { "a", "b" })) || info.GetHashCode() != new CallInfo(3, "a", "b").GetHashCode()) return 18;
        if (info.Equals(new CallInfo(3, "a")) || !Throws<ArgumentException>(() => new CallInfo(1, "a", "b"))) return 19;
        if (new DynamicAttribute().TransformFlags.Count != 1 || !new DynamicAttribute(new[] { false, true }).TransformFlags[1]) return 20;
        var variables = new Variables();
        variables[1] = "x";
        if (variables.Count != 2 || (string?)variables[1] != "x") return 21;

        // ReadOnlyCollectionBuilder grows, edits and hands over its storage.
        var builder = new ReadOnlyCollectionBuilder<int>(1) { 3, 1 };
        builder.Insert(1, 2);
        builder.Add(4);
        if (builder.Count != 4 || builder.IndexOf(2) != 1 || !builder.Contains(4) || builder.Capacity < 4) return 22;
        builder.Remove(4);
        builder.Reverse();
        if (builder[0] != 1 || builder.ToArray().Length != 3) return 23;
        if (!Throws<InvalidOperationException>(() => { foreach (int entry in builder) builder.Add(entry); })) return 24;
        if (!Throws<ArgumentException>(() => ((IList)builder).Add("x"))) return 25;
        ReadOnlyCollection<int> frozen = builder.ToReadOnlyCollection();
        if (frozen.Count != 4 || builder.Count != 0 || frozen[3] != 1) return 26;

        // ExpandoObject keeps upstream's dictionary and notification behaviour.
        var expando = new ExpandoObject();
        IDictionary<string, object?> members = expando;
        string changed = "";
        ((INotifyPropertyChanged)expando).PropertyChanged += (sender, e) => changed += e.PropertyName;
        members["a"] = 1;
        members.Add("b", 2);
        members["a"] = 3;
        if (members.Count != 2 || !members.ContainsKey("b") || !members.TryGetValue("a", out object? a) || (int)a! != 3) return 27;
        if (!Throws<ArgumentException>(() => members.Add("b", 4)) || !Throws<KeyNotFoundException>(() => _ = members["c"])) return 28;
        if (changed != "aba" || !members.Remove("a") || members.Remove("a") || members.Keys.Count != 1 || changed != "abaa") return 29;
        var pairs = new KeyValuePair<string, object?>[1];
        members.CopyTo(pairs, 0);
        if (pairs[0].Key != "b" || !members.Contains(new KeyValuePair<string, object?>("b", 2))) return 30;
        members.Clear();
        if (members.Count != 0 || changed != "abaab") return 31;

        // Type tests, conversions and array length build upstream's nodes.
        ParameterExpression item = Expression.Parameter(typeof(object), "o");
        ParameterExpression numbers = Expression.Parameter(typeof(int[]), "a");
        TypeBinaryExpression isString = Expression.TypeIs(item, typeof(string));
        TypeBinaryExpression isExactly = Expression.TypeEqual(item, typeof(string));
        if (isString.NodeType != ExpressionType.TypeIs || isString.Type != typeof(bool) || isString.TypeOperand != typeof(string) ||
            isExactly.NodeType != ExpressionType.TypeEqual || isString.Update(item) != isString) return 32;
        // A type test's text names the type (issue 225: without type names a type's name reads its ID).
        if (isString.ToString() != $"(o Is {typeof(string).Name})" || isExactly.ToString() != $"(o TypeEqual {typeof(string).Name})") return 33;
        UnaryExpression asString = Expression.TypeAs(item, typeof(string));
        UnaryExpression unboxed = Expression.Unbox(item, typeof(int));
        UnaryExpression length = Expression.ArrayLength(numbers);
        if (asString.Type != typeof(string) || unboxed.NodeType != ExpressionType.Unbox || length.Type != typeof(int)) return 34;
        if (asString.ToString() != $"(o As {typeof(string).Name})" || unboxed.ToString() != "Unbox(o)" || length.ToString() != "ArrayLength(a)") return 35;
        if (asString.CanReduce || asString.Reduce() != asString || Expression.Add(Expression.Constant(1), Expression.Constant(2)).CanReduce) return 36;
        if ((int)GotoExpressionKind.Continue != 3 || GotoExpressionKind.Goto != default) return 73;
        if (Expression.TypeAs(item, typeof(int?)).Type != typeof(int?) || !Throws<ArgumentException>(() => Expression.TypeAs(item, typeof(int)))) return 37;
        if (!Throws<ArgumentException>(() => Expression.Unbox(Expression.Constant("s"), typeof(int))) ||
            !Throws<ArgumentException>(() => Expression.Unbox(item, typeof(string)))) return 38;
        if (!Throws<ArgumentException>(() => Expression.ArrayLength(item)) ||
            !Throws<ArgumentException>(() => Expression.ArrayLength(Expression.Parameter(typeof(int[,]))))) return 39;
        Expression<Func<int[], int>> lengthTree = values => values.Length;
        if (lengthTree.Body.NodeType != ExpressionType.ArrayLength) return 40;

        // Array indexing and creation validate their shapes.
        if (!Throws<ArgumentException>(() => Expression.ArrayIndex(numbers, Expression.Constant(0L))) ||
            !Throws<ArgumentException>(() => Expression.ArrayIndex(item, Expression.Constant(0))) ||
            !Throws<ArgumentException>(() => Expression.ArrayIndex(Expression.Parameter(typeof(int[,])), Expression.Constant(0))) ||
            !Throws<ArgumentException>(() => Expression.ArrayIndex(numbers, Expression.Constant(0), Expression.Constant(1)))) return 41;
        if (!Throws<ArgumentException>(() => Expression.NewArrayInit(typeof(void))) ||
            !Throws<ArgumentException>(() => Expression.NewArrayBounds(typeof(int))) ||
            !Throws<ArgumentException>(() => Expression.NewArrayBounds(typeof(int), Expression.Constant("1")))) return 43;
        IndexExpression element = Expression.ArrayAccess(numbers, Expression.Constant(1));
        if (element.NodeType != ExpressionType.Index || element.Object != numbers || element.Indexer != null ||
            ((IArgumentProvider)element).ArgumentCount != 1 || ((IArgumentProvider)element).GetArgument(0) is not ConstantExpression || element.ToString() != "a[1]") return 44;
        if (element.Update(numbers, element.Arguments) != element || !Throws<ArgumentException>(() => Expression.ArrayAccess(numbers)) ||
            !Throws<ArgumentException>(() => Expression.ArrayAccess(numbers, Expression.Constant("1")))) return 45;

        // Indexed property access over a property without index parameters.
        Expression<Func<Model, int>> propertyTree = model => model.Property;
        var property = (PropertyInfo)((MemberExpression)propertyTree.Body).Member;
        ParameterExpression modelParameter = propertyTree.Parameters[0];
        IndexExpression indexed = Expression.Property(modelParameter, property, new Expression[0]);
        if (indexed.Indexer != property || indexed.Type != typeof(int) || indexed.Arguments.Count != 0 ||
            Expression.MakeIndex(modelParameter, property, null) is not IndexExpression || indexed.ToString() != "model.Property[]") return 46;
        if (!Throws<ArgumentException>(() => Expression.Property(modelParameter, property, Expression.Constant(1)))) return 47;

        // Collection initializers: compiler-emitted and hand-built.
        Expression<Func<int, List<int>>> listTree = capacity => new List<int>(capacity) { 1, 2 };
        var listInit = (ListInitExpression)listTree.Body;
        MethodInfo add = listInit.Initializers[0].AddMethod;
        if (listInit.NodeType != ExpressionType.ListInit || listInit.Type != typeof(List<int>) || listInit.Initializers.Count != 2 ||
            add.Name != "Add" || ((IArgumentProvider)listInit.Initializers[1]).ArgumentCount != 1 || !listInit.CanReduce) return 48;
        string expected = "new " + typeof(List<int>).Name + "(capacity) {" + add + "(1), " + add + "(2)}";
        if (listInit.ToString() != expected || listInit.Initializers[0].ToString() != add + "(1)") return 49;
        ListInitExpression manual = Expression.ListInit(listInit.NewExpression, add, Expression.Constant(7));
        if (manual.Initializers.Count != 1 || manual.Initializers[0].Arguments[0] is not ConstantExpression seven || (int)seven.Value! != 7) return 50;
        if (listInit.Update(listInit.NewExpression, listInit.Initializers) != listInit ||
            listInit.Reduce() is not BlockExpression reducedList || reducedList.Type != typeof(List<int>)) return 51;
        Expression<Action<List<int>>> clearTree = values => values.Clear();
        MethodInfo clear = ((MethodCallExpression)clearTree.Body).Method;
        if (!Throws<ArgumentException>(() => Expression.ElementInit(clear)) ||
            !Throws<ArgumentException>(() => Expression.ElementInit(add, Expression.Constant("x")))) return 67;

        // Member initializers bind fields; MemberBinding and MemberAssignment carry the binding.
        Expression<Func<int, Model>> initTree = value => new Model(value) { Other = 5 };
        var memberInit = (MemberInitExpression)initTree.Body;
        var assignment = (MemberAssignment)memberInit.Bindings[0];
        if (memberInit.NodeType != ExpressionType.MemberInit || memberInit.Type != typeof(Model) ||
            assignment.BindingType != MemberBindingType.Assignment || assignment.Member.Name != "Other" ||
            assignment.Expression is not ConstantExpression) return 52;
        if (memberInit.ToString() != "new " + typeof(Model).Name + "(value) {Other = 5}" || assignment.ToString() != "Other = 5") return 53;
        if (assignment.Update(assignment.Expression) != assignment ||
            memberInit.Update(memberInit.NewExpression, memberInit.Bindings) != memberInit ||
            memberInit.Reduce() is not BlockExpression reducedInit || reducedInit.Type != typeof(Model)) return 54;
        Expression<Func<Stranger, int>> strangerTree = stranger => stranger.Field;
        MemberInfo strangerField = ((MemberExpression)strangerTree.Body).Member;
        if (!Throws<ArgumentException>(() => Expression.MemberInit(memberInit.NewExpression, Expression.Bind(strangerField, Expression.Constant(1)))) ||
            !Throws<ArgumentException>(() => Expression.Bind(assignment.Member, Expression.Constant("x")))) return 55;

        // Property initializers bind through the setter's MethodInfo, Bind(MethodInfo, Expression).
        Expression<Func<int, Model>> propertyInitTree = value => new Model(value) { Property = 7 };
        var propertyInit = (MemberInitExpression)propertyInitTree.Body;
        var propertyAssignment = (MemberAssignment)propertyInit.Bindings[0];
        if (propertyAssignment.Member is not PropertyInfo boundProperty || boundProperty.Name != "Property" ||
            boundProperty.PropertyType != typeof(int) || propertyInit.ToString() != "new " + typeof(Model).Name + "(value) {Property = 7}") return 74;
        Expression<Func<Model, int>> getCall = target => target.Get();
        MethodInfo notAccessor = ((MethodCallExpression)getCall.Body).Method;
        try { Expression.Bind(notAccessor, Expression.Constant(1)); return 75; }
        catch (ArgumentException e) when (e is not ArgumentNullException && e.ParamName == "propertyAccessor") { }
        if (!Throws<ArgumentNullException>(() => Expression.Bind((MethodInfo)null!, Expression.Constant(1)))) return 76;

        // The visitor rewrites through the new nodes' Update methods.
        var rewriter = new Rewriter();
        var rewrittenList = (ListInitExpression)rewriter.Visit(listInit);
        var rewrittenInit = (MemberInitExpression)rewriter.Visit(memberInit);
        Expression rewrittenType = rewriter.Visit(isString);
        Expression rewrittenIndex = rewriter.Visit(element);
        if (rewriter.Visited != "LEEMBTI" || rewrittenType != isString) return 56;
        if (rewrittenList.Initializers[1].Arguments[0] is not ConstantExpression twenty || (int)twenty.Value! != 20) return 57;
        if (((MemberAssignment)rewrittenInit.Bindings[0]).Expression is not ConstantExpression fifty || (int)fifty.Value! != 50) return 58;
        if (rewrittenIndex is not IndexExpression rewrittenElement || rewrittenElement.Arguments[0] is not ConstantExpression ten || (int)ten.Value! != 10) return 59;

        // Existing call and construction nodes now expose IArgumentProvider.
        Expression<Func<int, Model>> newTree = value => new Model(value);
        if (((IArgumentProvider)newTree.Body).ArgumentCount != 1 ||
            ((IArgumentProvider)clearTree.Body).ArgumentCount != 0) return 60;

        // Fixed-arity Call, Block and New overloads build the same nodes as the array forms.
        Expression<Func<Model, int>> getTree = target => target.Get();
        Expression<Func<Model, int>> sum2Tree = target => target.Sum(1, 2);
        Expression<Func<Model, int>> sum3Tree = target => target.Sum(1, 2, 3);
        Expression<Func<int>> sum1Tree = () => Model.Sum(1);
        Expression<Func<int>> sum4Tree = () => Model.Sum(1, 2, 3, 4);
        Expression<Func<int>> sum5Tree = () => Model.Sum(1, 2, 3, 4, 5);
        ParameterExpression instance = getTree.Parameters[0];
        ConstantExpression one = Expression.Constant(1), two = Expression.Constant(2), three = Expression.Constant(3);
        ConstantExpression four = Expression.Constant(4), five = Expression.Constant(5);
        MethodInfo Method(LambdaExpression tree) => ((MethodCallExpression)tree.Body).Method;
        int Evaluate(Expression body) => Expression.Lambda<Func<Model, int>>(body, instance).Compile()(new Model(100));
        if (Evaluate(Expression.Call(instance, Method(getTree))) != 100 ||
            Evaluate(Expression.Call(instance, Method(sum2Tree), one, two)) != 103 ||
            Evaluate(Expression.Call(instance, Method(sum3Tree), one, two, three)) != 106) return 68;
        if (Evaluate(Expression.Call(Method(sum1Tree), one)) != 1 ||
            Evaluate(Expression.Call(Method(sum4Tree), one, two, three, four)) != 10 ||
            Evaluate(Expression.Call(Method(sum5Tree), one, two, three, four, five)) != 15) return 69;
        if (!Throws<ArgumentException>(() => Expression.Call(Method(sum4Tree), one, two)) ||
            !Throws<ArgumentNullException>(() => Expression.Call(Method(sum1Tree), (Expression)null!))) return 70;
        if (Expression.Block(one, two).Expressions.Count != 2 || Evaluate(Expression.Block(one, two, three)) != 3 ||
            Evaluate(Expression.Block(one, two, three, four)) != 4 || Evaluate(Expression.Block(one, two, three, four, five)) != 5) return 71;
        if (!Throws<ArgumentException>(() => Expression.New(((NewExpression)newTree.Body).Constructor))) return 72;
        // Queryable's operators build their query tree; only executing it in memory needs reflection.
        if (query.Where(value => value > 4).Expression is not MethodCallExpression { Method.Name: "Where", Arguments.Count: 2 }) return 77;

#if !DESKTOP_ORACLE
        // NetWasm boundaries: in-memory query execution and non-generic wrappers need run-time
        // code generation or reflection; dynamic call sites need the DLR.
        if (!Throws<PlatformNotSupportedException>(() => provider.Execute<int>(Expression.Constant(1))) ||
            !Throws<PlatformNotSupportedException>(() => provider.Execute(Expression.Constant(1))) ||
            !Throws<PlatformNotSupportedException>(() => provider.CreateQuery(query.Expression))) return 61;
        if (!Throws<PlatformNotSupportedException>(() => { foreach (int value in created) { } }) ||
            !Throws<PlatformNotSupportedException>(() => ((IEnumerable)list).AsQueryable()) ||
            !Throws<PlatformNotSupportedException>(() => query.Count())) return 62;
        if (!Throws<PlatformNotSupportedException>(() => CallSite.Create(typeof(Func<CallSite, int>), binder)) ||
            !Throws<PlatformNotSupportedException>(() => CallSite<Func<CallSite, int>>.Create(binder)) ||
            !Throws<PlatformNotSupportedException>(() => ((IDynamicMetaObjectProvider)expando).GetMetaObject(item))) return 63;
        // Add and property discovery by name need runtime member lookup.
        if (!Throws<PlatformNotSupportedException>(() => Expression.ListInit(listInit.NewExpression, Expression.Constant(1))) ||
            !Throws<PlatformNotSupportedException>(() => Expression.Property(modelParameter, "Property", Expression.Constant(1)))) return 65;
#else
        if (provider.Execute<int>(Expression.Constant(1)) != 1 || query.Where(value => value > 4).Count() != 2) return 61;
#endif
        // The interpreter runs ListInit and MemberInit, as desktop does.
        if (listTree.Compile()(4).Count != 2 || initTree.Compile()(3).Other != 5) return 66;
        // Array element types and array types come from the type table, and the interpreter runs type tests and
        // array lengths, as on desktop.
        if (Expression.ArrayIndex(numbers, Expression.Constant(0)).Type != typeof(int) ||
            Expression.NewArrayInit(typeof(int), Expression.Constant(1)).Type != typeof(int[]) ||
            Expression.NewArrayBounds(typeof(int), Expression.Constant(1)).Type != typeof(int[]) ||
            element.Type != typeof(int)) return 64;
        if (!Expression.Lambda<Func<object, bool>>(isString, item).Compile()("text") ||
            lengthTree.Compile()(new[] { 1, 2 }) != 2) return 67;
        return input + 1;
    }
}
