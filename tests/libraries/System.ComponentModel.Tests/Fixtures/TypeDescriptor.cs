// From the compiler harness's TypeDescriptorCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.ComponentModel.Design.Serialization;
using System.Drawing;
using System.Globalization;
using System.Resources;
using DisplayNameAttribute = System.ComponentModel.DisplayNameAttribute;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
namespace TypeDescriptorFixture;

enum State { Idle, Ready }
interface IShape { }
sealed class Plain { }
sealed class Widget { public int Size; }
sealed class Licensed { }
class EditorBase { }
sealed class IntEditor : EditorBase { }
sealed class Button : Component { public string? Text; }

sealed class Prop : PropertyDescriptor
{
    private readonly Type _type;
    public object? Value;
    public Prop(string name, Type type, params Attribute[] attributes) : base(name, attributes) => _type = type;
    public override Type ComponentType => typeof(Widget);
    public override bool IsReadOnly => false;
    public override Type PropertyType => _type;
    public override bool CanResetValue(object component) => false;
    public override object? GetValue(object? component) => Value;
    public override void ResetValue(object component) { }
    public override void SetValue(object? component, object? value) => Value = value;
    public override bool ShouldSerializeValue(object component) => false;
}

sealed class TextProp : PropertyDescriptor
{
    public TextProp() : base("Text", null) { }
    public override Type ComponentType => typeof(Button);
    public override bool IsReadOnly => false;
    public override Type PropertyType => typeof(string);
    public override bool CanResetValue(object component) => false;
    public override object? GetValue(object? component) => ((Button)component!).Text;
    public override void ResetValue(object component) { }
    public override void SetValue(object? component, object? value) => ((Button)component!).Text = (string?)value;
    public override bool ShouldSerializeValue(object component) => false;
}

sealed class Evt : EventDescriptor
{
    public Evt(string name) : base(name, null) { }
    public override Type ComponentType => typeof(Widget);
    public override Type EventType => typeof(EventHandler);
    public override bool IsMulticast => true;
    public override void AddEventHandler(object component, Delegate value) { }
    public override void RemoveEventHandler(object component, Delegate value) { }
}

sealed class WidgetDescriptor : CustomTypeDescriptor
{
    public WidgetDescriptor(ICustomTypeDescriptor? parent) : base(parent) { }
    public override AttributeCollection GetAttributes() => new AttributeCollection(new DescriptionAttribute("widget"));
    public override PropertyDescriptorCollection GetProperties() => new PropertyDescriptorCollection(new PropertyDescriptor[] { EntryPoint.SizeProp, EntryPoint.NameProp });
    public override PropertyDescriptorCollection GetProperties(Attribute[]? attributes) => GetProperties();
    public override EventDescriptorCollection GetEvents() => new EventDescriptorCollection(new EventDescriptor[] { EntryPoint.Changed });
    public override EventDescriptorCollection GetEvents(Attribute[]? attributes) => GetEvents();
    public override TypeConverter GetConverter() => EntryPoint.WidgetConverter;
    public override PropertyDescriptor? GetDefaultProperty() => EntryPoint.SizeProp;
    public override EventDescriptor? GetDefaultEvent() => EntryPoint.Changed;
    public override string? GetClassName() => "Widget!";
    public override object? GetEditor(Type editorBaseType) => editorBaseType == typeof(EditorBase) ? EntryPoint.Editor : null;
}

sealed class WidgetProvider : TypeDescriptionProvider
{
    public WidgetProvider() { }
    public WidgetProvider(TypeDescriptionProvider parent) : base(parent) { }
    public override ICustomTypeDescriptor? GetTypeDescriptor(Type objectType, object? instance) =>
        new WidgetDescriptor(base.GetTypeDescriptor(objectType, instance));
    public override object? CreateInstance(IServiceProvider? provider, Type objectType, Type[]? argTypes, object?[]? args) =>
        objectType == typeof(Widget) ? new Widget { Size = args is { Length: 1 } ? (int)args[0]! : 0 } : null;
}

sealed class ButtonDescriptor : CustomTypeDescriptor
{
    public override PropertyDescriptorCollection GetProperties() => new PropertyDescriptorCollection(new PropertyDescriptor[] { new TextProp() });
    public override PropertyDescriptorCollection GetProperties(Attribute[]? attributes) => GetProperties();
}

sealed class ButtonProvider : TypeDescriptionProvider
{
    public override ICustomTypeDescriptor? GetTypeDescriptor(Type objectType, object? instance) => new ButtonDescriptor();
}

sealed class Described : CustomTypeDescriptor
{
    public override string? GetClassName() => "Described!";
    public override string? GetComponentName() => "described";
    public override TypeConverter GetConverter() => EntryPoint.WidgetConverter;
    public override AttributeCollection GetAttributes() => new AttributeCollection(new DescriptionAttribute("d"));
    public override PropertyDescriptorCollection GetProperties() => new PropertyDescriptorCollection(new PropertyDescriptor[] { EntryPoint.SizeProp });
    public override PropertyDescriptorCollection GetProperties(Attribute[]? attributes) => GetProperties();
}

sealed class DesignSite : ISite
{
    public DesignSite(IComponent component) => Component = component;
    public IComponent Component { get; }
    public IContainer? Container => null;
    public bool DesignMode => true;
    public string? Name { get; set; } = "button";
    public object? GetService(Type serviceType) => null;
}

sealed class Reader : IResourceReader
{
    private readonly Hashtable _data;
    public Reader(Hashtable data) => _data = data;
    public IDictionaryEnumerator GetEnumerator() => _data.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _data.GetEnumerator();
    public void Close() { }
    public void Dispose() { }
}

sealed class AppResources : ComponentResourceManager
{
    private readonly ResourceSet _set;
    public AppResources(ResourceSet set) => _set = set;
    public override ResourceSet? GetResourceSet(CultureInfo culture, bool createIfNotExists, bool tryParents) => _set;
}

sealed class KeyContext : LicenseContext
{
    public string? Key;
    public override string? GetSavedLicenseKey(Type type, System.Reflection.Assembly? resourceAssembly) => Key;
    public override void SetSavedLicenseKey(Type type, string key) => Key = key;
}

sealed class FileProvider : LicFileLicenseProvider
{
    public string Key(Type type) => GetKey(type);
    public bool Valid(string? key, Type type) => IsKeyValid(key, type);
}

sealed class Options : DesignerOptionService
{
    public int Populated;
    public object? Shown;
    protected override void PopulateOptionCollection(DesignerOptionCollection options)
    {
        Populated++;
        if (options.Parent == null)
        {
            DesignerOptionCollection grid = CreateOptionCollection(options, "Grid", new Described());
            CreateOptionCollection(grid, "Snap", new Described());
        }
    }
    protected override bool ShowDialog(DesignerOptionCollection options, object optionObject)
    {
        Shown = optionObject;
        return true;
    }
    public bool BadName() => EntryPoint.Throws<ArgumentException>(() => CreateOptionCollection(Options, "", new object()));
}

sealed class Relations : MemberRelationshipService
{
    public override bool SupportsRelationship(MemberRelationship source, MemberRelationship relationship) => relationship.Owner is not Plain;
}

public static class EntryPoint
{
    internal static readonly Prop SizeProp = new Prop("Size", typeof(int));
    internal static readonly Prop NameProp = new Prop("Name", typeof(string), new DescriptionAttribute("n"));
    internal static readonly Evt Changed = new Evt("Changed");
    internal static readonly TypeConverter WidgetConverter = new StringConverter();
    internal static readonly EditorBase Editor = new IntEditor();

    internal static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return true; }
        return false;
    }

    public static int Run(int input)
    {
        // Built-in converters come from the intrinsic table.
        if (TypeDescriptor.GetConverter(typeof(int)) is not Int32Converter ints) return 1;
        if ((int)ints.ConvertFromInvariantString("41")! != 41) return 2;
        if (TypeDescriptor.GetConverter(typeof(string)) is not StringConverter) return 3;
        if (TypeDescriptor.GetConverter(typeof(bool)) is not BooleanConverter) return 4;
        if (TypeDescriptor.GetConverter(typeof(Guid)) is not GuidConverter) return 5;
        if (TypeDescriptor.GetConverter(typeof(TimeSpan)) is not TimeSpanConverter) return 6;
        if (TypeDescriptor.GetConverter(typeof(DateTime)) is not DateTimeConverter) return 7;
        if (TypeDescriptor.GetConverter(typeof(Version)) is not VersionConverter) return 8;
        if (TypeDescriptor.GetConverter(typeof(Uri)) is not UriTypeConverter) return 9;
        if (TypeDescriptor.GetConverter(typeof(CultureInfo)) is not CultureInfoConverter) return 10;
        if (TypeDescriptor.GetConverter(typeof(decimal)) is not DecimalConverter) return 11;
        TypeConverter states = TypeDescriptor.GetConverter(typeof(State));
        if (states is not EnumConverter || !ReferenceEquals(states, TypeDescriptor.GetConverter(typeof(State)))) return 12;
        if (!Equals(states.ConvertFromInvariantString("Ready"), State.Ready)) return 13;
        if (TypeDescriptor.GetConverter(typeof(int?)) is not NullableConverter nullable || nullable.UnderlyingType != typeof(int)) return 14;
        if (TypeDescriptor.GetConverter(typeof(int[])) is not ArrayConverter) return 15;
        if (TypeDescriptor.GetConverter(typeof(List<int>)) is not CollectionConverter) return 16;
        if (TypeDescriptor.GetConverter(typeof(IShape)) is not ReferenceConverter) return 17;
        if (TypeDescriptor.GetConverter(typeof(Plain)).GetType() != typeof(TypeConverter)) return 18;
        if (TypeDescriptor.GetConverter(41) is not Int32Converter) return 19;
        if (TypeDescriptor.GetConverter(typeof(Color)) is not ColorConverter || TypeDescriptor.GetConverter(typeof(Point)) is not PointConverter) return 20;
        if (TypeDescriptor.GetConverter(typeof(Size)) is not SizeConverter || TypeDescriptor.GetConverter(typeof(Rectangle)) is not RectangleConverter) return 21;
        if (TypeDescriptor.GetConverter(typeof(SizeF)) is not SizeFConverter || TypeDescriptor.GetConverter(new Component()) is not ComponentConverter) return 22;
        if (TypeDescriptor.GetConverterFromRegisteredType(typeof(int)) is not Int32Converter) return 23;
        if (!Throws<InvalidOperationException>(() => TypeDescriptor.GetConverterFromRegisteredType(typeof(Plain)))) return 24;
        TypeDescriptor.RegisterType<Licensed>();
        if (TypeDescriptor.GetConverterFromRegisteredType(typeof(Licensed)).GetType() != typeof(TypeConverter)) return 25;

        // Explicit providers, precedence, removal and refresh notifications.
        int refreshed = 0;
        RefreshEventHandler onRefresh = e => { if (e.TypeChanged == typeof(Widget)) refreshed++; };
        TypeDescriptor.Refreshed += onRefresh;
        var provider = new WidgetProvider();
        TypeDescriptor.AddProvider(provider, typeof(Widget));
        if (refreshed != 1) return 26;
        var widget = new Widget();
        if (TypeDescriptor.GetConverter(typeof(Widget)) != WidgetConverter || TypeDescriptor.GetConverter(widget) != WidgetConverter) return 27;
        PropertyDescriptorCollection props = TypeDescriptor.GetProperties(typeof(Widget));
        if (props.Count != 2 || props["Size"] != SizeProp || props.Find("name", true) != NameProp) return 28;
        if (TypeDescriptor.GetProperties(widget).Count != 2 || TypeDescriptor.GetProperties(widget, new Attribute[0]).Count != 2) return 29;
        if (TypeDescriptor.GetAttributes(typeof(Widget))[typeof(DescriptionAttribute)] is not DescriptionAttribute { Description: "widget" }) return 30;
        if (TypeDescriptor.GetEvents(widget).Count != 1 || TypeDescriptor.GetEvents(typeof(Widget))["Changed"] != Changed) return 31;
        if (TypeDescriptor.GetDefaultProperty(typeof(Widget)) != SizeProp || TypeDescriptor.GetDefaultEvent(widget) != Changed) return 32;
        if (TypeDescriptor.GetClassName(widget) != "Widget!" || TypeDescriptor.GetClassName(typeof(Widget)) != "Widget!") return 33;
        if (TypeDescriptor.GetEditor(widget, typeof(EditorBase)) != Editor || TypeDescriptor.GetEditor(typeof(Widget), typeof(EditorBase)) != Editor) return 34;
        if (TypeDescriptor.GetReflectionType(typeof(Widget)) != typeof(Widget) || TypeDescriptor.GetReflectionType(widget) != typeof(Widget)) return 35;

        // Attribute overlays merge with the provider's attributes.
        TypeDescriptionProvider overlay = TypeDescriptor.AddAttributes(typeof(Widget), new DisplayNameAttribute("Shape"));
        AttributeCollection merged = TypeDescriptor.GetAttributes(typeof(Widget));
        if (merged[typeof(DisplayNameAttribute)] is not DisplayNameAttribute { DisplayName: "Shape" } || merged[typeof(DescriptionAttribute)] is not DescriptionAttribute) return 36;
        TypeDescriptor.RemoveProvider(overlay, typeof(Widget));
        TypeDescriptor.RemoveProvider(provider, typeof(Widget));
        if (refreshed != 4) return 37;
        TypeDescriptor.Refreshed -= onRefresh;
        if (TypeDescriptor.GetConverter(typeof(Widget)).GetType() != typeof(TypeConverter)) return 38;

        // Instance and transparent providers, and a provider forwarding to the default.
        TypeDescriptor.AddProviderTransparent(provider, widget);
        if (TypeDescriptor.GetConverter(widget) != WidgetConverter || TypeDescriptor.GetConverter(new Widget()).GetType() != typeof(TypeConverter)) return 39;
        TypeDescriptor.RemoveProviderTransparent(provider, widget);
        if (TypeDescriptor.GetConverter(widget).GetType() != typeof(TypeConverter)) return 40;
        var forwarding = new WidgetProvider(TypeDescriptor.GetProvider(typeof(Widget)));
        TypeDescriptor.AddProvider(forwarding, widget);
        if (TypeDescriptor.GetProperties(widget).Count != 2 || TypeDescriptor.GetConverter(widget) != WidgetConverter) return 41;
        TypeDescriptor.RemoveProvider(forwarding, widget);

        // Explicit construction through a service-supplied provider.
        var services = new ServiceContainer();
        services.AddService(typeof(TypeDescriptionProvider), provider);
        if (TypeDescriptor.CreateInstance(services, typeof(Widget), new[] { typeof(int) }, new object[] { 7 }) is not Widget { Size: 7 }) return 43;
        if (!Throws<ArgumentException>(() => TypeDescriptor.CreateInstance(null, typeof(Widget), new[] { typeof(int) }, new object[0]))) return 44;
        if (!Throws<ArgumentNullException>(() => TypeDescriptor.CreateInstance(null, null!, null, null))) return 45;

        // Associations keep the most recent matching secondary.
        var primary = new Widget();
        var secondary = new Plain();
        TypeDescriptor.CreateAssociation(primary, secondary);
        if (TypeDescriptor.GetAssociation(typeof(Plain), primary) != secondary || TypeDescriptor.GetAssociation(typeof(Widget), primary) != primary) return 46;
        if (!Throws<ArgumentException>(() => TypeDescriptor.CreateAssociation(primary, primary))) return 47;
        if (!Throws<ArgumentException>(() => TypeDescriptor.CreateAssociation(primary, secondary))) return 48;
        TypeDescriptor.RemoveAssociation(primary, secondary);
        if (TypeDescriptor.GetAssociation(typeof(Plain), primary) != primary) return 49;
        TypeDescriptor.CreateAssociation(primary, secondary);
        TypeDescriptor.RemoveAssociations(primary);
        if (TypeDescriptor.GetAssociation(typeof(Plain), primary) != primary) return 50;
        if (!Throws<ArgumentNullException>(() => TypeDescriptor.GetAssociation(null!, primary))) return 51;

        // Editor tables hold supplied instances.
        var table = new Hashtable { [typeof(int)] = Editor };
        TypeDescriptor.AddEditorTable(typeof(EditorBase), table);
        if (TypeDescriptor.GetEditor(typeof(int), typeof(EditorBase)) != Editor || TypeDescriptor.GetEditor(typeof(string), typeof(EditorBase)) != null) return 52;

        // Custom descriptors, component names, sorting and marker types.
        var described = new Described();
        if (TypeDescriptor.GetClassName(described) != "Described!" || TypeDescriptor.GetComponentName(described) != "described") return 53;
        if (TypeDescriptor.GetConverter(described) != WidgetConverter || TypeDescriptor.GetProperties(described).Count != 1) return 54;
        if (TypeDescriptor.GetAttributes(described)[typeof(DescriptionAttribute)] is not DescriptionAttribute { Description: "d" }) return 55;
        var container = new Container();
        var part = new Component();
        container.Add(part, "part1");
        if (TypeDescriptor.GetComponentName(part) != "part1" || TypeDescriptor.GetFullComponentName(part) != "part1" || TypeDescriptor.GetComponentName(new Plain()) != null) return 56;
        var sortable = new ArrayList { SizeProp, NameProp };
        TypeDescriptor.SortDescriptorArray(sortable);
        if (sortable[0] != NameProp || sortable[1] != SizeProp) return 57;
        if (TypeDescriptor.InterfaceType == null || TypeDescriptor.ComObjectType == null || TypeDescriptor.InterfaceType == TypeDescriptor.ComObjectType) return 58;

        // NullableConverter wraps the underlying converter.
        if (nullable.NullableType != typeof(int?) || nullable.UnderlyingTypeConverter is not Int32Converter) return 60;
        if (!Equals(nullable.ConvertFrom(null, CultureInfo.InvariantCulture, "41"), 41) || nullable.ConvertFrom(null, CultureInfo.InvariantCulture, "") != null) return 61;
        if ((string?)nullable.ConvertTo(null, CultureInfo.InvariantCulture, null, typeof(string)) != "" || !Equals(nullable.ConvertTo(null, CultureInfo.InvariantCulture, 41, typeof(int)), 41)) return 62;
        if (!nullable.CanConvertFrom(typeof(string)) || !nullable.CanConvertTo(typeof(int)) || !nullable.IsValid("41") || nullable.IsValid("x")) return 63;
        if (!Throws<ArgumentException>(() => new NullableConverter(typeof(int)))) return 64;
        var bools = new NullableConverter(typeof(bool?));
        if (!bools.GetStandardValuesSupported(null) || bools.GetStandardValues(null)!.Count != 3 || !bools.GetStandardValuesExclusive(null)) return 65;

        // Drawing converters.
        var colors = new ColorConverter();
        if ((Color)colors.ConvertFrom(null, CultureInfo.InvariantCulture, "Red")! != Color.Red) return 66;
        if ((Color)colors.ConvertFrom(null, CultureInfo.InvariantCulture, "1, 2, 3")! != Color.FromArgb(1, 2, 3)) return 67;
        if ((string?)colors.ConvertTo(null, CultureInfo.InvariantCulture, Color.Red, typeof(string)) != "Red") return 68;
        if ((string?)colors.ConvertTo(null, CultureInfo.InvariantCulture, Color.FromArgb(1, 2, 3), typeof(string)) != "1, 2, 3") return 69;
        TypeConverter.StandardValuesCollection named = colors.GetStandardValues(null)!;
        if (!colors.GetStandardValuesSupported(null) || named.Count < 100) return 70;
        for (int i = 1; i < named.Count; i++)
        {
            if (string.CompareOrdinal(((Color)named[i - 1]!).Name, ((Color)named[i]!).Name) > 0) return 71;
        }
        var points = new PointConverter();
        if ((Point)points.ConvertFrom(null, CultureInfo.InvariantCulture, "1, 2")! != new Point(1, 2)) return 72;
        if ((string?)points.ConvertTo(null, CultureInfo.InvariantCulture, new Point(1, 2), typeof(string)) != "1, 2") return 73;
        if ((Point)points.CreateInstance(null, new Hashtable { ["X"] = 3, ["Y"] = 4 })! != new Point(3, 4) || !points.GetCreateInstanceSupported(null)) return 74;
        if ((Rectangle)new RectangleConverter().ConvertFrom(null, CultureInfo.InvariantCulture, "1, 2, 3, 4")! != new Rectangle(1, 2, 3, 4)) return 75;
        if ((Size)new SizeConverter().ConvertFrom(null, CultureInfo.InvariantCulture, "5, 6")! != new Size(5, 6)) return 76;
        var sizes = new SizeFConverter();
        if ((SizeF)sizes.ConvertFrom(null, CultureInfo.InvariantCulture, "1.5, 2.5")! != new SizeF(1.5f, 2.5f)) return 77;
        if ((string?)sizes.ConvertTo(null, CultureInfo.InvariantCulture, new SizeF(1.5f, 2.5f), typeof(string)) != "1.5, 2.5") return 78;
        if (!Throws<ArgumentException>(() => points.CreateInstance(null, new Hashtable { ["X"] = "3", ["Y"] = 4 }))) return 79;

        // Licensing: explicit saved keys and managed context state.
        var files = new FileProvider();
        string key = files.Key(typeof(Licensed));
        // The key names the component's type (issue 225: without type names a type's name reads its ID).
        if (key != typeof(Licensed).FullName + " is a licensed component." || !files.Valid(key + " more", typeof(Licensed)) || files.Valid("x", typeof(Licensed)) || files.Valid(null, typeof(Licensed))) return 80;
        var context = new KeyContext { Key = key };
        License? license = files.GetLicense(context, typeof(Licensed), null, true);
        if (license == null || license.LicenseKey != key) return 81;
        license.Dispose();
        LicenseContext saved = LicenseManager.CurrentContext;
        if (LicenseManager.UsageMode != LicenseUsageMode.Runtime) return 82;
        var owner = new object();
        LicenseManager.LockContext(owner);
        if (!Throws<InvalidOperationException>(() => LicenseManager.LockContext(new object()))) return 83;
        if (!Throws<InvalidOperationException>(() => LicenseManager.CurrentContext = context)) return 84;
        if (!Throws<ArgumentException>(() => LicenseManager.UnlockContext(new object()))) return 85;
        LicenseManager.UnlockContext(owner);
        LicenseManager.CurrentContext = context;
        if (LicenseManager.CurrentContext != context) return 86;
        LicenseManager.CurrentContext = saved;

        // Culture string equality, which the option tree's name lookup uses.
        if (!string.Equals("Grid", "grid", StringComparison.InvariantCultureIgnoreCase) || string.Equals("Grid", "grid", StringComparison.InvariantCulture)) return 125;
        if (!"Snap".Equals("SNAP", StringComparison.CurrentCultureIgnoreCase) || !"Snap".Equals("Snap", StringComparison.CurrentCulture) || string.Equals(null, "x", StringComparison.InvariantCulture)) return 126;
        if (!Throws<ArgumentException>(() => string.Equals("a", "a", (StringComparison)99)) || !Throws<ArgumentException>(() => "a".Equals("a", (StringComparison)99))) return 127;

        // DesignerOptionService populates lazily and wraps descriptor targets.
        var options = new Options();
        if (options.Populated != 0 || options.Options.Count != 1 || options.Populated != 1) return 87;
        DesignerOptionService.DesignerOptionCollection? grid = options.Options["grid"];
        if (grid == null || grid.Name != "Grid" || grid.Parent != options.Options || options.Options.IndexOf(grid) != 0 || options.Options[0] != grid) return 88;
        if (grid.Count != 1 || grid["Snap"] == null || options.Options["missing"] != null) return 89;
        if (grid.Properties.Count != 2 || grid.Properties["Size"] == null || options.Options.Properties.Count != 2) return 90;
        if (!grid.ShowDialog() || options.Shown is not Described) return 91;
        if (!options.BadName() || ((IList)grid).IsReadOnly != true || !Throws<NotSupportedException>(() => ((IList)grid).Add(1))) return 92;
        // The options' converter is DesignerOptionService's private DesignerOptionConverter, which exposes the
        // options as properties (a plain TypeConverter does not).
        var optionsConverter = TypeDescriptor.GetConverter(options.Options);
        if (optionsConverter.GetType() == typeof(TypeConverter) || !optionsConverter.GetPropertiesSupported()) return 93;
        var copy = new object[1];
        options.Options.CopyTo(copy, 0);
        if (copy[0] != grid) return 94;
        SizeProp.Value = 5;
        if (!Equals(((IDesignerOptionService)options).GetOptionValue("Grid", "Size"), 5) || ((IDesignerOptionService)options).GetOptionValue("None", "Size") != null) return 95;

        // MemberRelationshipService keeps weak relationships and rejects unsupported ones.
        var relations = new Relations();
        var a = new Widget();
        var b = new Widget();
        var source = new MemberRelationship(a, SizeProp);
        var target = new MemberRelationship(b, NameProp);
        relations[source] = target;
        if (relations[source] != target || relations[a, SizeProp] != target) return 96;
        relations[source] = MemberRelationship.Empty;
        if (relations[source] != MemberRelationship.Empty || !relations[source].IsEmpty) return 97;
        try
        {
            relations[source] = new MemberRelationship(new Plain(), NameProp);
            return 98;
        }
        catch (ArgumentException error) when (error is not ArgumentNullException)
        {
            if (!error.Message.Contains(typeof(Plain).Name)) return 99;
        }
        if (!Throws<ArgumentNullException>(() => _ = new MemberRelationship(null!, SizeProp)) || !Throws<ArgumentNullException>(() => _ = relations[null!, SizeProp])) return 100;
        if (MemberRelationship.Empty.GetHashCode() != default(MemberRelationship).GetHashCode() || source.GetHashCode() != new MemberRelationship(a, SizeProp).GetHashCode()) return 101;
        if (source != new MemberRelationship(a, SizeProp) || source == target || !source.Equals((object)new MemberRelationship(a, SizeProp))) return 102;

        // ComponentResourceManager applies sorted resources through design-mode descriptors.
        TypeDescriptor.AddProvider(new ButtonProvider(), typeof(Button));
        var button = new Button();
        button.Site = new DesignSite(button);
        var set = new ResourceSet(new Reader(new Hashtable { ["button.Text"] = "hello", ["buttonx.Text"] = "no", ["other.Text"] = "no" }));
        var resources = new AppResources(set);
        resources.ApplyResources(button, "button", CultureInfo.InvariantCulture);
        if (button.Text != "hello") return 103;
        if (!Throws<ArgumentNullException>(() => resources.ApplyResources(null!, "button")) || !Throws<ArgumentNullException>(() => resources.ApplyResources(button, null!))) return 104;
        var unmatched = new Button();
        resources.ApplyResources(unmatched, "missing", CultureInfo.InvariantCulture);
        if (unmatched.Text != null) return 105;

#if !DESKTOP_ORACLE
        // NetWasm boundaries: member, attribute and type discovery, activation and files.
        if (!Throws<PlatformNotSupportedException>(() => TypeDescriptor.GetProperties(typeof(Plain)))) return 106;
        if (!Throws<PlatformNotSupportedException>(() => TypeDescriptor.GetAttributes(typeof(Plain)))) return 107;
        if (!Throws<PlatformNotSupportedException>(() => TypeDescriptor.GetEvents(new Plain()))) return 108;
        if (!Throws<PlatformNotSupportedException>(() => TypeDescriptor.GetDefaultProperty(typeof(Plain)))) return 109;
        if (!Throws<PlatformNotSupportedException>(() => TypeDescriptor.GetProperties(described, true))) return 110;
        if (!Throws<PlatformNotSupportedException>(() => TypeDescriptor.CreateInstance(null, typeof(Plain), null, null))) return 111;
        if (!Throws<PlatformNotSupportedException>(() => TypeDescriptor.CreateProperty(typeof(Widget), "Size", typeof(int)))) return 112;
        if (!Throws<PlatformNotSupportedException>(() => TypeDescriptor.CreateEvent(typeof(Widget), "Changed", typeof(EventHandler)))) return 113;
        if (!Throws<PlatformNotSupportedException>(() => TypeDescriptor.CreateDesigner(part, typeof(IDesigner)))) return 114;
        var plain = new Plain();
        TypeDescriptor.AddAttributes(plain, new DescriptionAttribute("p"));
        if (!Throws<PlatformNotSupportedException>(() => TypeDescriptor.GetAttributes(plain))) return 115;
        table[typeof(long)] = typeof(IntEditor);
        if (!Throws<PlatformNotSupportedException>(() => TypeDescriptor.GetEditor(typeof(long), typeof(EditorBase)))) return 116;
        if (!Throws<PlatformNotSupportedException>(() => nullable.ConvertTo(null, CultureInfo.InvariantCulture, 41, typeof(InstanceDescriptor)))) return 117;
        if (!Throws<PlatformNotSupportedException>(() => points.GetProperties(null, new Point(1, 2), null))) return 118;
        context.Key = "bad";
        if (!Throws<PlatformNotSupportedException>(() => files.GetLicense(context, typeof(Licensed), null, true))) return 119;
        if (!Throws<PlatformNotSupportedException>(() => LicenseManager.IsLicensed(typeof(Licensed)))) return 120;
        if (!Throws<PlatformNotSupportedException>(() => LicenseManager.Validate(typeof(Licensed)))) return 121;
        if (!Throws<PlatformNotSupportedException>(() => LicenseManager.CreateWithContext(typeof(Licensed), context))) return 122;
        if (!Throws<PlatformNotSupportedException>(() => resources.ApplyResources(new Button(), "button", CultureInfo.InvariantCulture))) return 124;
        // No COM descriptor provider is discovered; the handler is managed registration state.
#pragma warning disable CS0618
        if (TypeDescriptor.ComNativeDescriptorHandler != null) return 59;
#pragma warning restore CS0618
#endif
        return input + 1;
    }
}
