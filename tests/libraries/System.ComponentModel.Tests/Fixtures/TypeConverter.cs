// From the compiler harness's TypeConverterCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Collections;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.ComponentModel.Design.Serialization;
using System.Globalization;
using System.IO;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
namespace TypeConverterFixture;

sealed class Part : Component { }

sealed class Store : SerializationStore
{
    public bool Closed;
    public override ICollection Errors => Array.Empty<object>();
    public override void Close() => Closed = true;
    public override void Save(Stream stream) => stream.WriteByte(7);
}

sealed class Loader : DesignerLoader
{
    public IDesignerLoaderHost? Host;
    public override void BeginLoad(IDesignerLoaderHost host) => Host = host;
    public override void Dispose() { }
}

sealed class Tree : ITreeDesigner
{
    public IComponent Component { get; } = new Part();
    public DesignerVerbCollection Verbs { get; } = new DesignerVerbCollection();
    public ICollection Children => new object[] { 1, 2 };
    public IDesigner? Parent => null;
    public bool Initialized;
    public void DoDefaultAction() { }
    public void Initialize(IComponent component) => Initialized = component != null;
    public void Dispose() { }
}

sealed class Dictionary : IDictionaryService
{
    readonly Hashtable values = new();
    public object? GetKey(object? value) { foreach (DictionaryEntry e in values) if (Equals(e.Value, value)) return e.Key; return null; }
    public object? GetValue(object key) => values[key];
    public void SetValue(object key, object? value) => values[key] = value;
}

sealed class Provider : TypeDescriptionProviderService
{
    public override TypeDescriptionProvider GetProvider(object instance) => null!;
    public override TypeDescriptionProvider GetProvider(Type type) => null!;
}

sealed class Disposable : IDisposable { public bool Disposed; public void Dispose() => Disposed = true; }

public static class EntryPoint
{
    static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; } catch (T) { return true; }
    }

    public static int Run(int input)
    {
        // Design event data and delegates.
        var part = new Part();
        var changed = new ComponentChangedEventArgs(part, null, 1, 2);
        if (changed.Component != part || changed.Member != null || (int)changed.OldValue! != 1 || (int)changed.NewValue! != 2) return 1;
        var rename = new ComponentRenameEventArgs(part, "a", "b");
        if (rename.OldName != "a" || rename.NewName != "b" || rename.Component != part) return 2;
        var close = new DesignerTransactionCloseEventArgs(true, false);
        if (!close.TransactionCommitted || close.LastTransaction) return 3;
        if (new ComponentEventArgs(part).Component != part || new ComponentChangingEventArgs(part, null).Component != part) return 4;
        var seen = 0;
        ComponentEventHandler handler = (_, e) => seen += e.Component == part ? 1 : 100;
        handler += (_, e) => seen += 10;
        handler(null, new ComponentEventArgs(part));
        if (seen != 11) return 5;
        if (new ActiveDesignerEventArgs(null, null).NewDesigner != null || new DesignerEventArgs(null).Designer != null) return 6;

        // DesignerCollection wraps arrays and lists.
        var designers = new DesignerCollection(new IDesignerHost[0]);
        if (designers.Count != 0 || ((ICollection)designers).IsSynchronized) return 7;
        var listed = new DesignerCollection((IList?)null);
        if (listed.Count != 0) return 8;

        // Interfaces dispatch through ordinary implementations.
        ITreeDesigner tree = new Tree();
        tree.Initialize(new Part());
        if (!((Tree)tree).Initialized || tree.Children.Count != 2 || tree.Parent != null || tree.Verbs.Count != 0) return 9;
        IDictionaryService dictionary = new Dictionary();
        dictionary.SetValue("k", "v");
        if ((string?)dictionary.GetValue("k") != "v" || (string?)dictionary.GetKey("v") != "k") return 10;

        // ServiceContainer registration, callbacks, removal and disposal.
        var container = new ServiceContainer();
        if (container.GetService(typeof(IServiceContainer)) != container || container.GetService(typeof(ServiceContainer)) != container) return 11;
        var disposable = new Disposable();
        container.AddService(typeof(Disposable), disposable);
        var created = 0;
        container.AddService(typeof(string), (c, t) => { created++; return "made"; });
        if (container.GetService(typeof(Disposable)) != disposable || (string?)container.GetService(typeof(string)) != "made" || (string?)container.GetService(typeof(string)) != "made" || created != 1) return 12;
        if (!Throws<ArgumentException>(() => container.AddService(typeof(Disposable), new Disposable()))) return 13;
        if (!Throws<ArgumentException>(() => container.AddService(typeof(int), "wrong type"))) return 14;
        var parent = new ServiceContainer();
        var child = new ServiceContainer(parent);
        child.AddService(typeof(Uri), new Uri("http://x/"), promote: true);
        if (parent.GetService(typeof(Uri)) == null || child.GetService(typeof(Uri)) == null) return 15;
        container.RemoveService(typeof(string));
        if (container.GetService(typeof(string)) != null) return 16;
        container.Dispose();
        if (!disposable.Disposed) return 17;

        // Enums and attributes keep upstream's values and text.
        if ((int)SelectionTypes.Replace != 0x2 || (int)SelectionTypes.Primary != 0x10 || (int)ViewTechnology.Default != 2 || (int)HelpContextType.Window != 1 || (int)HelpKeywordType.GeneralKeyword != 1) return 18;
        var help = new HelpKeywordAttribute(typeof(Part));
        // The keyword is the type's FullName, which reads the type's ID where a program carries no type names (issue 225).
        if (help.HelpKeyword != typeof(Part).FullName || HelpKeywordAttribute.Default.HelpKeyword != null || !HelpKeywordAttribute.Default.IsDefaultAttribute() || !help.Equals(new HelpKeywordAttribute(typeof(Part).FullName))) return 19;
        var root = new RootDesignerSerializerAttribute("S", "B", true);
        if (root.SerializerTypeName != "S" || root.SerializerBaseTypeName != "B" || !root.Reloadable) return 20;
        if (new DefaultSerializationProviderAttribute("P").ProviderTypeName != "P") return 21;

        // Abstract design bases.
        var store = new Store();
        ((IDisposable)store).Dispose();
        if (!store.Closed) return 22;
        var loader = new Loader();
        if (loader.Loading) return 23;
        loader.Flush();
        if (new Provider().GetProvider(typeof(int)) != null) return 24;

        // Design-time license context and its BinaryWriter serialization.
        var license = new DesigntimeLicenseContext();
        if (license.UsageMode != LicenseUsageMode.Designtime || license.GetSavedLicenseKey(typeof(int), null) != null) return 25;
        var bytes = new MemoryStream();
        DesigntimeLicenseContextSerializer.Serialize(bytes, "key", license);
        if (bytes.Length < 6 || bytes.ToArray()[0] != 255) return 26;

        // MaskedTextProvider editing, display and validation.
        var mask = new MaskedTextProvider("(999) 000-0000");
        if (mask.Mask != "(999) 000-0000" || mask.Length != 14 || mask.EditPositionCount != 10 || mask.MaskCompleted || mask.AssignedEditPositionCount != 0) return 27;
        if (!mask.Add("123", out int last, out MaskedTextResultHint hint) || last != 3 || hint != MaskedTextResultHint.Success) return 28;
        if (mask.ToString() != "(123)    -" || mask.ToDisplayString() != "(123) ___-____") return 29;
        if (mask.Set("1234567890") == false || !mask.MaskCompleted || mask.ToString() != "(123) 456-7890") return 30;
        if (mask.ToString(false, false) != "1234567890" || mask.VerifyChar('x', 6, out hint) || hint != MaskedTextResultHint.DigitExpected) return 31;
        if (!mask.Remove() || mask.MaskFull || mask.LastAssignedPosition != 12) return 32;
        var clone = (MaskedTextProvider)mask.Clone();
        if (clone.ToString() != mask.ToString() || clone.Mask != mask.Mask) return 33;
        if (!mask.Replace('9', 1) || mask.ToString(true, true) != "(923) 456-789_" && mask.ToString(true, true) != "(923) 456-789") return 34;
        if (mask.VerifyString("12a", out int at, out hint) || at != 3 || hint != MaskedTextResultHint.DigitExpected) return 35;
        if (!Throws<ArgumentException>(() => new MaskedTextProvider("") )) return 36;
        if ((int)MaskedTextResultHint.UnavailableEditPosition != -53 || (int)MaskedTextResultHint.PositionOutOfRange != -55 || MaskedTextProvider.InvalidIndex != -1) return 37;

        // System.Timers.Timer state.
        var timer = new System.Timers.Timer(250);
        if (timer.Interval != 250 || timer.Enabled || !timer.AutoReset) return 38;
        if (!Throws<ArgumentException>(() => new System.Timers.Timer(0)) || !Throws<ArgumentException>(() => timer.Interval = -1)) return 39;
        timer.BeginInit();
        timer.Enabled = true;
        timer.EndInit();
        timer.Stop();
        if (timer.Enabled) return 40;
        timer.Dispose();
        var at2 = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        if (new System.Timers.ElapsedEventArgs(at2).SignalTime != at2.ToLocalTime() && new System.Timers.ElapsedEventArgs(at2).SignalTime != at2) return 41;
        if (new System.Timers.TimersDescriptionAttribute("described").Description != "described") return 43;

        // CultureInfoConverter in invariant mode.
        var cultures = new CultureInfoConverter();
        if (cultures.ConvertFrom(null, CultureInfo.InvariantCulture, "") != CultureInfo.InvariantCulture) return 44;
        if ((string?)cultures.ConvertTo(null, CultureInfo.InvariantCulture, CultureInfo.InvariantCulture, typeof(string)) != "(Default)") return 45;
        if (!cultures.GetStandardValuesSupported(null) || cultures.GetStandardValuesExclusive(null) || cultures.GetStandardValues(null)!.Count < 1) return 46;
        if (!cultures.CanConvertFrom(typeof(string)) || !cultures.CanConvertTo(typeof(string))) return 47;

        // Both key on the type's AssemblyQualifiedName, which NetWasm's runtime type table answers too. A design-time
        // context saves keys only to serialize them, so it reads none back.
        if (new DefaultSerializationProviderAttribute(typeof(Part)).ProviderTypeName != typeof(Part).AssemblyQualifiedName) return 48;
        license.SetSavedLicenseKey(typeof(Part), "k");
        if (license.GetSavedLicenseKey(typeof(Part), null) != null) return 49;
        return input + 1;
    }
}
