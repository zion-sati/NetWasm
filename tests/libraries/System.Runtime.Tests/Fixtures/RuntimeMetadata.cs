// From the compiler harness's RuntimeMetadataCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Collections;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Security;
using System.Security.Permissions;
using System.Security.Principal;
[assembly: NeutralResourcesLanguage("en-US"), SatelliteContractVersion("1.0.0.0")]
[assembly: AssemblyCulture(""), AssemblyDelaySign(false), AssemblyAlgorithmId(System.Configuration.Assemblies.AssemblyHashAlgorithm.SHA1)]
[assembly: CppInlineNamespace("Outer.Inner")]
namespace RuntimeMetadataFixtureRuntimeMetadata;

[HasCopySemantics, NativeCppClass]
struct CopyStruct { public int Value; }

[ScopelessEnum]
enum Scopeless { First, Second }

sealed class Required { }

sealed class Permission : IPermission
{
    public string Name = "";
    public IPermission Copy() => new Permission { Name = Name };
    public void Demand() { }
    public IPermission? Intersect(IPermission? target) => target;
    public bool IsSubsetOf(IPermission? target) => target is Permission p && p.Name == Name;
    public IPermission? Union(IPermission? target) => this;
    public void FromXml(SecurityElement e) => Name = e.Attribute("name") ?? "";
    public SecurityElement ToXml()
    {
        var e = new SecurityElement("IPermission");
        e.AddAttribute("name", Name);
        return e;
    }
}

public static class EntryPoint
{
    static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; } catch (T) { return true; }
    }

    public static int Run(int input)
    {
        // Calling-convention and C++/CLI markers are passive types.
        if (new CallConvFastcall() == null || new CallConvStdcall() == null || new CallConvThiscall() == null || new CallConvCdecl() == null) return 1;
        if (new RequiredAttributeAttribute(typeof(int)).RequiredContract != typeof(int) || new CopyStruct { Value = 3 }.Value != 3 || Scopeless.Second != (Scopeless)1) return 2;
        if (new HasCopySemanticsAttribute() == null || new NativeCppClassAttribute() == null || new ScopelessEnumAttribute() == null || new CppInlineNamespaceAttribute("a.b") == null || new Required() == null) return 3;

        // Assembly identity attributes keep their constructor values.
#pragma warning disable CS0618, SYSLIB0003
        if (new AssemblyAlgorithmIdAttribute(System.Configuration.Assemblies.AssemblyHashAlgorithm.SHA256).AlgorithmId != 0x800C || new AssemblyAlgorithmIdAttribute(7u).AlgorithmId != 7) return 4;
        if (new AssemblyCultureAttribute("fr").Culture != "fr" || !new AssemblyDelaySignAttribute(true).DelaySign) return 5;
        var flags = new AssemblyFlagsAttribute(AssemblyNameFlags.PublicKey | AssemblyNameFlags.Retargetable);
        if (flags.AssemblyFlags != 0x101 || flags.Flags != 0x101u || new AssemblyFlagsAttribute(4).AssemblyFlags != 4 || new AssemblyFlagsAttribute(8u).Flags != 8u) return 6;
        if (new AssemblyKeyFileAttribute("k.snk").KeyFile != "k.snk" || new AssemblyKeyNameAttribute("container").KeyName != "container") return 7;
        var signature = new AssemblySignatureKeyAttribute("public", "counter");
        if (signature.PublicKey != "public" || signature.Countersignature != "counter") return 8;

        // Resource metadata validates its arguments.
        var neutral = new NeutralResourcesLanguageAttribute("en-US");
        if (neutral.CultureName != "en-US" || neutral.Location != UltimateResourceFallbackLocation.MainAssembly) return 9;
        if (new NeutralResourcesLanguageAttribute("de", UltimateResourceFallbackLocation.Satellite).Location != UltimateResourceFallbackLocation.Satellite) return 10;
        try { _ = new NeutralResourcesLanguageAttribute(null!); return 11; } catch (ArgumentNullException e) { if (e.ParamName != "cultureName") return 11; }
        try { _ = new NeutralResourcesLanguageAttribute("en", (UltimateResourceFallbackLocation)5); return 12; }
        catch (ArgumentException e) { if (e.ParamName != null || !e.Message.Contains("\"5\"")) return 12; }
        if (new SatelliteContractVersionAttribute("2.0.0.0").Version != "2.0.0.0") return 13;
        try { _ = new SatelliteContractVersionAttribute(null!); return 14; } catch (ArgumentNullException e) { if (e.ParamName != "version") return 14; }

        // Legacy permissions are inert data and user-implemented interfaces.
        var permission = new SecurityPermissionAttribute(SecurityAction.Demand) { UnmanagedCode = true, Flags = SecurityPermissionFlag.Execution, Unrestricted = true };
        if (!permission.UnmanagedCode || permission.Flags != SecurityPermissionFlag.Execution || !permission.Unrestricted || permission.Action != default || permission.CreatePermission() != null) return 15;
        if ((int)SecurityAction.Demand != 2 || (int)SecurityAction.RequestRefuse != 10 || (int)SecurityPermissionFlag.UnmanagedCode != 2 || (int)SecurityPermissionFlag.AllFlags != 16383) return 16;
        if ((int)TokenImpersonationLevel.Anonymous != 1 || (int)TokenImpersonationLevel.Delegation != 4 || (int)TokenImpersonationLevel.None != 0) return 17;
        IPermission granted = new Permission { Name = "files" };
        ISecurityEncodable encodable = (ISecurityEncodable)granted;
        var restored = new Permission();
        restored.FromXml(encodable.ToXml());
        if (restored.Name != "files" || !granted.IsSubsetOf(granted.Copy()) || granted.Union(null) != granted) return 18;
#pragma warning restore CS0618, SYSLIB0003

        // SecurityElement trees, attributes, escaping and copying.
        var root = new SecurityElement("Perm");
        if (root.ToString() != "<Perm/>" + Environment.NewLine || root.Attributes != null || root.Children != null || root.Text != null) return 19;
        root.AddAttribute("class", "x");
        root.AddAttribute("version", "1");
        var child = new SecurityElement("Child", "a&amp;b");
        root.AddChild(child);
        string nl = Environment.NewLine;
        if (root.ToString() != "<Perm class=\"x\"" + nl + "version=\"1\">" + nl + "<Child>a&amp;b</Child>" + nl + "</Perm>" + nl) return 20;
        if (root.Attribute("class") != "x" || root.Attribute("missing") != null || root.SearchForChildByTag("Child") != child || root.SearchForTextOfTag("Child") != "a&b" || child.Text != "a&b") return 21;
        Hashtable attributes = root.Attributes!;
        ArrayList children = root.Children!;
        if (attributes.Count != 2 || (string)attributes["version"]! != "1" || children.Count != 1 || children[0] != child) return 22;
        if (!Throws<ArgumentException>(() => root.AddAttribute("class", "y")) || !Throws<ArgumentException>(() => root.AddAttribute("bad name", "v")) || !Throws<ArgumentException>(() => root.AddAttribute("n", "\""))) return 23;
        if (!Throws<ArgumentException>(() => new SecurityElement("a b")) || !Throws<ArgumentException>(() => new SecurityElement("t", "<")) || !Throws<ArgumentNullException>(() => root.AddChild(null!))) return 24;
        if (SecurityElement.Escape("<a&'\">") != "&lt;a&amp;&apos;&quot;&gt;" || SecurityElement.Escape(null) != null || SecurityElement.Escape("plain") != "plain") return 25;
        if (SecurityElement.IsValidTag("a<") || !SecurityElement.IsValidTag("a") || SecurityElement.IsValidText(">") || SecurityElement.IsValidAttributeValue("\"") || !SecurityElement.IsValidAttributeName("n")) return 26;
        var copy = root.Copy();
        if (!root.Equal(copy) || copy == root || copy.Children![0] != child || root.Equal(new SecurityElement("Perm")) || root.Equal(null)) return 27;
        copy.Tag = "Other";
        if (root.Tag != "Perm" || !Throws<ArgumentNullException>(() => copy.Tag = null!) || !Throws<ArgumentException>(() => copy.Tag = "x y")) return 28;
        if (SecurityElement.FromString("<a/>") != null || !Throws<ArgumentNullException>(() => SecurityElement.FromString(null!))) return 29;
        var replaced = new SecurityElement("R") { Attributes = new Hashtable { ["k"] = "v" }, Children = new ArrayList { new SecurityElement("C") } };
        if (replaced.Attribute("k") != "v" || replaced.SearchForChildByTag("C") == null) return 30;
        return input + 1;
    }
}
