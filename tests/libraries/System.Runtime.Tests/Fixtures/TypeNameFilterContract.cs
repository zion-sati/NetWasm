// From the compiler harness's TypeNameFilterContractCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Reflection;
namespace TypeNameFilterFixture;
public static class EntryPoint
{
    public static int Run(int input)
    {
        var member = new Named("AlphaBeta", MemberTypes.Method);
        if (!Type.FilterName(member, "AlphaBeta") || !Type.FilterName(member, " Alpha* ") || !Type.FilterName(member, "*")) return 1;
        if (Type.FilterName(member, "alphabeta") || Type.FilterName(member, "*Beta") || Type.FilterName(member, "Alpha") || Type.FilterName(member, "")) return 2;
        if (!Type.FilterNameIgnoreCase(member, "alphabeta") || !Type.FilterNameIgnoreCase(member, " aLPHa* ")) return 3;
        var nested = new Named("Outer+Inner", MemberTypes.NestedType);
        if (!Type.FilterName(nested, "Inner") || !Type.FilterNameIgnoreCase(nested, "in*")) return 4;
        if (Type.FilterName(nested, "Outer+Inner")) return 5;
        var caught = 0;
        try { Type.FilterName(member, null); } catch (InvalidFilterCriteriaException) { caught++; }
        try { Type.FilterName(member, 42); } catch (InvalidFilterCriteriaException) { caught++; }
        try { Type.FilterNameIgnoreCase(member, null); } catch (InvalidFilterCriteriaException) { caught++; }
        try { Type.FilterNameIgnoreCase(member, 42); } catch (InvalidFilterCriteriaException) { caught++; }
        return caught == 4 ? input + 1 : 6;
    }
    private sealed class Named : MemberInfo
    {
        private readonly string name;
        private readonly MemberTypes kind;
        public Named(string name, MemberTypes kind) { this.name = name; this.kind = kind; }
        public override string Name => name;
        public override MemberTypes MemberType => kind;
        public override Type DeclaringType => null;
        public override Type ReflectedType => null;
        public override object[] GetCustomAttributes(bool inherit) => new object[0];
        public override object[] GetCustomAttributes(Type type, bool inherit) => new object[0];
        public override bool IsDefined(Type type, bool inherit) => false;
    }
}
