// From the compiler harness's BinderContractCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Globalization;
using System.Reflection;
namespace BinderFixture;
public static class EntryPoint
{
    public static int Run(int input)
    {
        var modifier = new ParameterModifier(2);
        if (modifier[0] || modifier[1]) return 1;
        modifier[1] = true;
        var copy = modifier;
        copy[0] = true;
        if (!modifier[0] || !modifier[1]) return 2;
        var caught = 0;
        try { new ParameterModifier(0); } catch (ArgumentException) { caught++; }
        try { new ParameterModifier(-1); } catch (ArgumentException) { caught++; }
        try { _ = modifier[-1]; } catch (IndexOutOfRangeException) { caught++; }
        try { modifier[2] = false; } catch (IndexOutOfRangeException) { caught++; }
        var empty = default(ParameterModifier);
        try { _ = empty[0]; } catch (NullReferenceException) { caught++; }
        try { empty[0] = true; } catch (NullReferenceException) { caught++; }
        if (caught != 6) return 3;

        var implementation = new CustomBinder();
        Binder binder = implementation;
        var flags = BindingFlags.Public | BindingFlags.Static;
        var culture = CultureInfo.InvariantCulture;
        var value = new object();
        if (binder.BindToField(flags, new FieldInfo[0], value, culture) != null) return 4;
        var args = new object[] { value };
        var modifiers = new[] { modifier };
        if (binder.BindToMethod(flags, new MethodBase[0], ref args, modifiers,
            culture, new[] { "item" }, out var state) != null) return 5;
        if (args.Length != 2 || !ReferenceEquals(args[0], value) || !ReferenceEquals(args[1], state)) return 6;
        if (!ReferenceEquals(binder.ChangeType(value, typeof(object), culture), value)) return 7;
        binder.ReorderArgumentArray(ref args, state);
        if (args.Length != 1 || !ReferenceEquals(args[0], state)) return 8;
        if (binder.SelectMethod(flags, new MethodBase[0], new[] { typeof(object) }, modifiers) != null) return 9;
        if (binder.SelectProperty(flags, new PropertyInfo[0], typeof(object), new[] { typeof(int) }, modifiers) != null) return 10;
        return implementation.Checks == 6 ? input + 1 : 11;
    }
    private sealed class CustomBinder : Binder
    {
        public int Checks;
        public override FieldInfo BindToField(BindingFlags flags, FieldInfo[] match, object value, CultureInfo culture)
        {
            if (flags == (BindingFlags.Public | BindingFlags.Static) && match.Length == 0 && value != null && culture == CultureInfo.InvariantCulture) Checks++;
            return null;
        }
        public override MethodBase BindToMethod(BindingFlags flags, MethodBase[] match, ref object[] args,
            ParameterModifier[] modifiers, CultureInfo culture, string[] names, out object state)
        {
            if (flags == (BindingFlags.Public | BindingFlags.Static) && match.Length == 0 && args.Length == 1 && modifiers[0][1] && culture == CultureInfo.InvariantCulture && names[0] == "item") Checks++;
            state = new object();
            args = new[] { args[0], state };
            return null;
        }
        public override object ChangeType(object value, Type type, CultureInfo culture)
        {
            if (type == typeof(object) && culture == CultureInfo.InvariantCulture) Checks++;
            return value;
        }
        public override void ReorderArgumentArray(ref object[] args, object state)
        {
            if (args.Length == 2 && ReferenceEquals(args[1], state)) Checks++;
            args = new[] { state };
        }
        public override MethodBase SelectMethod(BindingFlags flags, MethodBase[] match, Type[] types, ParameterModifier[] modifiers)
        {
            if (flags == (BindingFlags.Public | BindingFlags.Static) && match.Length == 0 && types[0] == typeof(object) && modifiers[0][0]) Checks++;
            return null;
        }
        public override PropertyInfo SelectProperty(BindingFlags flags, PropertyInfo[] match, Type returnType, Type[] indexes, ParameterModifier[] modifiers)
        {
            if (flags == (BindingFlags.Public | BindingFlags.Static) && match.Length == 0 && returnType == typeof(object) && indexes[0] == typeof(int) && modifiers[0][1]) Checks++;
            return null;
        }
    }
}
