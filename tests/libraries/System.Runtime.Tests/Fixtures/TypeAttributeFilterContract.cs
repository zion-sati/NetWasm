// From the compiler harness's TypeAttributeFilterContractCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Reflection;
namespace TypeAttributeFilterFixture;
public static class EntryPoint
{
 public static int Run(int input)
 {
  var method = new Method { Flags = MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.Final | MethodAttributes.Virtual | MethodAttributes.SpecialName };
  MethodAttributes[] present = { 0, MethodAttributes.Public, MethodAttributes.Static, MethodAttributes.Final, MethodAttributes.Virtual, MethodAttributes.SpecialName };
  foreach (var flag in present) if (!Type.FilterAttribute(method, (int)flag)) return 1;
  if (Type.FilterAttribute(method, (int)MethodAttributes.Private) || Type.FilterAttribute(method, (int)MethodAttributes.Abstract)) return 2;
  var constructor = new Constructor { Flags = MethodAttributes.Public | MethodAttributes.SpecialName };
  if (!Type.FilterAttribute(constructor, (int)MethodAttributes.Public) || Type.FilterAttribute(constructor, (int)MethodAttributes.Static)) return 3;
  var field = new Field { Flags = FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly | FieldAttributes.NotSerialized | FieldAttributes.PinvokeImpl };
  FieldAttributes[] fields = { 0, FieldAttributes.Public, FieldAttributes.Static, FieldAttributes.InitOnly, FieldAttributes.NotSerialized, FieldAttributes.PinvokeImpl };
  foreach (var flag in fields) if (!Type.FilterAttribute(field, (int)flag)) return 4;
  if (Type.FilterAttribute(field, (int)FieldAttributes.Private) || Type.FilterAttribute(field, (int)FieldAttributes.Literal)) return 5;
  MethodAttributes[] methodBits = { MethodAttributes.Static, MethodAttributes.Final, MethodAttributes.Virtual, MethodAttributes.Abstract, MethodAttributes.SpecialName };
  foreach (var bit in methodBits)
  {
   method.Flags = MethodAttributes.Public;
   if (Type.FilterAttribute(method, (int)bit)) return 7;
   method.Flags |= bit;
   if (!Type.FilterAttribute(method, (int)bit)) return 8;
  }
  FieldAttributes[] fieldBits = { FieldAttributes.Static, FieldAttributes.InitOnly, FieldAttributes.Literal, FieldAttributes.NotSerialized, FieldAttributes.PinvokeImpl };
  foreach (var bit in fieldBits)
  {
   field.Flags = FieldAttributes.Public;
   if (Type.FilterAttribute(field, (int)bit)) return 9;
   field.Flags |= bit;
   if (!Type.FilterAttribute(field, (int)bit)) return 10;
  }
  method.Kind = MemberTypes.Property;
  if (Type.FilterAttribute(method, 0) || Type.FilterAttribute(method, "unused")) return 11;
  method.Kind = MemberTypes.Method;
  var caught = 0;
  foreach (var member in new MemberInfo[] { method, constructor, field })
  {
   try { Type.FilterAttribute(member, null); } catch (InvalidFilterCriteriaException) { caught++; }
   try { Type.FilterAttribute(member, "bad"); } catch (InvalidFilterCriteriaException) { caught++; }
  }
  return caught == 6 ? input + 1 : 6;
 }
}
public sealed class Method : MethodInfo { public MemberTypes Kind = MemberTypes.Method; public override MemberTypes MemberType => Kind; public MethodAttributes Flags; public override MethodAttributes Attributes => Flags; public override Type ReturnType => typeof(void); public override ICustomAttributeProvider ReturnTypeCustomAttributes => null; public override MethodInfo GetBaseDefinition() => this;public override string Name => "Member";
public override Type DeclaringType => typeof(EntryPoint);
public override Type ReflectedType => typeof(EntryPoint);
public override object[] GetCustomAttributes(bool inherit) => new object[0];
public override object[] GetCustomAttributes(Type type, bool inherit) => new object[0];
public override bool IsDefined(Type type, bool inherit) => false;
public override RuntimeMethodHandle MethodHandle => default;
public override MethodImplAttributes GetMethodImplementationFlags() => 0;
public override ParameterInfo[] GetParameters() => new ParameterInfo[0];
public override object Invoke(object obj, BindingFlags flags, Binder binder, object[] parameters, System.Globalization.CultureInfo culture) => null;
}
public sealed class Constructor : ConstructorInfo { public MethodAttributes Flags; public override MethodAttributes Attributes => Flags; public override object Invoke(BindingFlags flags, Binder binder, object[] parameters, System.Globalization.CultureInfo culture) => null;public override string Name => "Member";
public override Type DeclaringType => typeof(EntryPoint);
public override Type ReflectedType => typeof(EntryPoint);
public override object[] GetCustomAttributes(bool inherit) => new object[0];
public override object[] GetCustomAttributes(Type type, bool inherit) => new object[0];
public override bool IsDefined(Type type, bool inherit) => false;
public override RuntimeMethodHandle MethodHandle => default;
public override MethodImplAttributes GetMethodImplementationFlags() => 0;
public override ParameterInfo[] GetParameters() => new ParameterInfo[0];
public override object Invoke(object obj, BindingFlags flags, Binder binder, object[] parameters, System.Globalization.CultureInfo culture) => null;
}
public sealed class Field : FieldInfo { public FieldAttributes Flags; public override FieldAttributes Attributes => Flags; public override Type FieldType => typeof(int); public override RuntimeFieldHandle FieldHandle => default; public override object GetValue(object obj) => null; public override void SetValue(object obj, object value, BindingFlags flags, Binder binder, System.Globalization.CultureInfo culture) { }public override string Name => "Member";
public override Type DeclaringType => typeof(EntryPoint);
public override Type ReflectedType => typeof(EntryPoint);
public override object[] GetCustomAttributes(bool inherit) => new object[0];
public override object[] GetCustomAttributes(Type type, bool inherit) => new object[0];
public override bool IsDefined(Type type, bool inherit) => false;
}
