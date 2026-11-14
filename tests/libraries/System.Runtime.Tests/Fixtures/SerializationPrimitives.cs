// From the compiler harness's SerializationPrimitivesCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
#pragma warning disable SYSLIB0011, SYSLIB0050, SYSLIB0051
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
namespace SerializationPrimitivesFixture;

[DataContract(Name = "order", Namespace = "urn:fixture")]
[KnownType(typeof(Line))]
[KnownType("KnownTypes")]
public sealed class Order
{
    [DataMember(Order = 1, IsRequired = true)] public int Id;
    [DataMember(Name = "total", EmitDefaultValue = false)] public decimal Total { get; set; }
    [IgnoreDataMember] public string Cache;
}

[DataContract] public sealed class Line { }

[CollectionDataContract(ItemName = "line")]
public sealed class Lines : System.Collections.Generic.List<Line> { }

[DataContract] public enum Status { [EnumMember(Value = "open")] Open, [EnumMember] Closed }

public sealed class Extensible : IExtensibleDataObject
{
    public ExtensionDataObject ExtensionData { get; set; }
}

public sealed class Provider : ISerializationSurrogateProvider
{
    public Type GetSurrogateType(Type type) => type == typeof(Order) ? typeof(Line) : type;
    public object GetObjectToSerialize(object obj, Type targetType) => obj;
    public object GetDeserializedObject(object obj, Type targetType) => obj;
}

public sealed class Fields : IFieldInfo
{
    public string[] FieldNames { get; set; }
    public Type[] FieldTypes { get; set; }
}

public sealed class Same
{
    public override bool Equals(object other) => true;
    public override int GetHashCode() => 7;
}

public sealed class Hostile
{
    public override bool Equals(object other) => throw new InvalidOperationException();
    public override int GetHashCode() => throw new InvalidOperationException();
}

public struct Point { public int X; }

public sealed class Tracking : Formatter
{
    public readonly StringBuilder Log = new StringBuilder();
    public override ISurrogateSelector SurrogateSelector { get; set; }
    public override SerializationBinder Binder { get; set; }
    public override StreamingContext Context { get; set; }
    public override object Deserialize(Stream serializationStream) => null;
    public override void Serialize(Stream serializationStream, object graph) { }
    public void Member(string name, object data) => WriteMember(name, data);
    public long Queue(object obj) => Schedule(obj);
    public object Next(out long id) => GetNext(out id);
    public void Smuggle(object obj) => m_objectQueue.Enqueue(obj);
    public ObjectIDGenerator Ids => m_idGenerator;
    private void Add(string kind, string name) => Log.Append(kind).Append(':').Append(name).Append(';');
    protected override void WriteArray(object obj, string name, Type memberType) => Add("array/" + memberType.Name, name);
    protected override void WriteBoolean(bool val, string name) => Add("bool", name);
    protected override void WriteByte(byte val, string name) => Add("byte", name);
    protected override void WriteChar(char val, string name) => Add("char", name);
    protected override void WriteDateTime(DateTime val, string name) => Add("date", name);
    protected override void WriteDecimal(decimal val, string name) => Add("decimal", name);
    protected override void WriteDouble(double val, string name) => Add("double", name);
    protected override void WriteInt16(short val, string name) => Add("short", name);
    protected override void WriteInt32(int val, string name) => Add("int", name);
    protected override void WriteInt64(long val, string name) => Add("long", name);
    protected override void WriteObjectRef(object obj, string name, Type memberType) => Add("ref/" + memberType.Name, name);
    protected override void WriteSByte(sbyte val, string name) => Add("sbyte", name);
    protected override void WriteSingle(float val, string name) => Add("float", name);
    protected override void WriteTimeSpan(TimeSpan val, string name) => Add("timespan", name);
    protected override void WriteUInt16(ushort val, string name) => Add("ushort", name);
    protected override void WriteUInt32(uint val, string name) => Add("uint", name);
    protected override void WriteUInt64(ulong val, string name) => Add("ulong", name);
    protected override void WriteValueType(object obj, string name, Type memberType) => Add("value/" + memberType.Name, name);
}

public sealed class Surrogate : ISerializationSurrogate
{
    public int Calls;
    public void GetObjectData(object obj, SerializationInfo info, StreamingContext context) => Calls++;
    public object SetObjectData(object obj, SerializationInfo info, StreamingContext context, ISurrogateSelector selector)
    {
        Calls += 10;
        return obj;
    }
}

public sealed class Seven : IConvertible
{
    public IFormatProvider Seen;
    public TypeCode GetTypeCode() => TypeCode.Object;
    public int ToInt32(IFormatProvider provider) { Seen = provider; return 7; }
    public bool ToBoolean(IFormatProvider provider) => throw new InvalidCastException();
    public byte ToByte(IFormatProvider provider) => throw new InvalidCastException();
    public char ToChar(IFormatProvider provider) => throw new InvalidCastException();
    public DateTime ToDateTime(IFormatProvider provider) => throw new InvalidCastException();
    public decimal ToDecimal(IFormatProvider provider) => throw new InvalidCastException();
    public double ToDouble(IFormatProvider provider) => throw new InvalidCastException();
    public short ToInt16(IFormatProvider provider) => throw new InvalidCastException();
    public long ToInt64(IFormatProvider provider) => throw new InvalidCastException();
    public sbyte ToSByte(IFormatProvider provider) => throw new InvalidCastException();
    public float ToSingle(IFormatProvider provider) => throw new InvalidCastException();
    public string ToString(IFormatProvider provider) => "seven";
    public object ToType(Type conversionType, IFormatProvider provider) => throw new InvalidCastException();
    public ushort ToUInt16(IFormatProvider provider) => throw new InvalidCastException();
    public uint ToUInt32(IFormatProvider provider) => throw new InvalidCastException();
    public ulong ToUInt64(IFormatProvider provider) => throw new InvalidCastException();
}

public sealed class Untouchable : Stream
{
    public override bool CanRead => throw new InvalidOperationException();
    public override bool CanSeek => throw new InvalidOperationException();
    public override bool CanWrite => throw new InvalidOperationException();
    public override long Length => throw new InvalidOperationException();
    public override long Position { get => throw new InvalidOperationException(); set => throw new InvalidOperationException(); }
    public override void Flush() => throw new InvalidOperationException();
    public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException();
    public override long Seek(long offset, SeekOrigin origin) => throw new InvalidOperationException();
    public override void SetLength(long value) => throw new InvalidOperationException();
    public override void Write(byte[] buffer, int offset, int count) => throw new InvalidOperationException();
}

public sealed class Callbacks
{
    public static int Calls;
    [OnSerializing] private void Serializing(StreamingContext context) => Calls++;
    [OnDeserializing] private void Deserializing(StreamingContext context) => Calls++;
}

public sealed class LookupManager : ObjectManager
{
    public LookupManager() : base(null, new StreamingContext(StreamingContextStates.All)) { }
    public override object GetObject(long objectID) => objectID == 3 ? "three" : base.GetObject(objectID);
}

public sealed class BrokenContract : InvalidDataContractException
{
    public BrokenContract(SerializationInfo info, StreamingContext context) : base(info, context) { }
}

public static class EntryPoint
{
    public static int Run(int input)
    {
        int code;
        if ((code = Attributes()) != 0) return code;
        if ((code = DataHelpers()) != 0) return 100 + code;
        if ((code = Ids()) != 0) return 200 + code;
        if ((code = Formatters()) != 0) return 300 + code;
        if ((code = Converter()) != 0) return 400 + code;
        if ((code = Selectors()) != 0) return 500 + code;
        if ((code = Services()) != 0) return 600 + code;
        if ((code = Managers()) != 0) return 700 + code;
        if ((code = Binary()) != 0) return 800 + code;
        return input + 1;
    }

    static int Attributes()
    {
        var contract = new DataContractAttribute();
        if (contract.Name != null || contract.IsNameSetExplicitly || contract.IsNamespaceSetExplicitly || contract.IsReference || contract.IsReferenceSetExplicitly) return 1;
        contract.Name = null;
        contract.Namespace = null;
        contract.IsReference = false;
        if (!contract.IsNameSetExplicitly || !contract.IsNamespaceSetExplicitly || !contract.IsReferenceSetExplicitly || contract.Name != null) return 2;
        var collection = new CollectionDataContractAttribute { KeyName = "k" };
        if (!collection.IsKeyNameSetExplicitly || collection.IsItemNameSetExplicitly || collection.IsValueNameSetExplicitly || collection.KeyName != "k") return 3;
        collection.ValueName = null;
        collection.ItemName = "i";
        if (!collection.IsValueNameSetExplicitly || !collection.IsItemNameSetExplicitly || collection.IsNameSetExplicitly || collection.IsReferenceSetExplicitly) return 4;
        var member = new DataMemberAttribute();
        if (member.Order != -1 || member.IsRequired || !member.EmitDefaultValue || member.IsNameSetExplicitly) return 5;
        member.Order = 0;
        member.Name = null;
        if (member.Order != 0 || !member.IsNameSetExplicitly) return 6;
        try { member.Order = -1; return 7; }
        catch (InvalidDataContractException ex)
        {
            if (ex.Message != "Property 'Order' in DataMemberAttribute attribute cannot be a negative number." || member.Order != 0) return 8;
        }
        var enumMember = new EnumMemberAttribute();
        if (enumMember.IsValueSetExplicitly) return 9;
        enumMember.Value = null;
        if (!enumMember.IsValueSetExplicitly || enumMember.Value != null) return 10;
        var byType = new KnownTypeAttribute((Type)null);
        var byName = new KnownTypeAttribute((string)null);
        if (byType.Type != null || byType.MethodName != null || byName.Type != null || byName.MethodName != null) return 11;
        if (new KnownTypeAttribute(typeof(Line)).Type != typeof(Line) || new KnownTypeAttribute("M").MethodName != "M") return 12;
        var ns = new ContractNamespaceAttribute("not a uri");
        if (ns.ContractNamespace != "not a uri" || ns.ClrNamespace != null) return 13;
        ns.ClrNamespace = null;
        ns.ClrNamespace = "Fixture";
        if (ns.ClrNamespace != "Fixture") return 14;
        Attribute ignore = new IgnoreDataMemberAttribute();
        if (ignore == null) return 15;
        var inner = new FormatException();
        var error = new InvalidDataContractException("bad", inner);
        if (error.Message != "bad" || !ReferenceEquals(error.InnerException, inner)) return 16;
        if (new InvalidDataContractException("only").InnerException != null || new InvalidDataContractException().InnerException != null) return 17;
        #if !DESKTOP_ORACLE
        try { _ = new BrokenContract(null, default); return 18; }
        catch (PlatformNotSupportedException) { }
        #endif
        var order = new Order { Id = 1, Total = 2m, Cache = "c" };
        return order.Id == 1 && Status.Open != Status.Closed && new Lines().Count == 0 ? 0 : 19;
    }

    static int DataHelpers()
    {
        var extensible = new Extensible();
        if (extensible.ExtensionData != null) return 1;
        IExtensibleDataObject contract = extensible;
        contract.ExtensionData = null;
        var options = new ExportOptions();
        var known = options.KnownTypes;
        if (known == null || known.Count != 0 || !ReferenceEquals(known, options.KnownTypes)) return 2;
        known.Add(typeof(Order));
        if (options.KnownTypes.Count != 1 || options.KnownTypes[0] != typeof(Order)) return 3;
        var provider = new Provider();
        options.DataContractSurrogate = provider;
        if (!ReferenceEquals(options.DataContractSurrogate, provider) || options.DataContractSurrogate.GetSurrogateType(typeof(Order)) != typeof(Line)) return 4;
        try { _ = new DateTimeFormat(null); return 5; } catch (ArgumentNullException) { }
        try { _ = new DateTimeFormat("o", null); return 6; } catch (ArgumentNullException) { }
        var current = new DateTimeFormat("not a format");
        if (current.FormatString != "not a format" || !ReferenceEquals(current.FormatProvider, DateTimeFormatInfo.CurrentInfo)) return 7;
        if (current.DateTimeStyles != DateTimeStyles.RoundtripKind) return 8;
        var culture = CultureInfo.InvariantCulture;
        var explicitFormat = new DateTimeFormat("", culture);
        if (explicitFormat.FormatString != "" || !ReferenceEquals(explicitFormat.FormatProvider, culture)) return 9;
        explicitFormat.DateTimeStyles = (DateTimeStyles)12345;
        if ((int)explicitFormat.DateTimeStyles != 12345) return 10;
        if ((int)EmitTypeInformation.AsNeeded != 0 || (int)EmitTypeInformation.Always != 1 || (int)EmitTypeInformation.Never != 2) return 11;
        var fields = new Fields();
        IFieldInfo info = fields;
        var names = new[] { "a" };
        info.FieldNames = names;
        info.FieldTypes = null;
        if (!ReferenceEquals(fields.FieldNames, names) || info.FieldTypes != null) return 12;
        if ((int)FormatterTypeStyle.TypesWhenNeeded != 0 || (int)FormatterTypeStyle.TypesAlways != 1 || (int)FormatterTypeStyle.XsdString != 2) return 13;
        if ((int)FormatterAssemblyStyle.Simple != 0 || (int)FormatterAssemblyStyle.Full != 1) return 14;
        if ((int)TypeFilterLevel.Low != 2 || (int)TypeFilterLevel.Full != 3) return 15;
        return 0;
    }

    static int Ids()
    {
        var ids = new ObjectIDGenerator();
        var first = new Same();
        var second = new Same();
        bool firstTime;
        if (ids.HasId(first, out firstTime) != 0 || !firstTime) return 1;
        if (ids.GetId(first, out firstTime) != 1 || !firstTime) return 2;
        if (ids.GetId(first, out firstTime) != 1 || firstTime) return 3;
        if (ids.HasId(first, out firstTime) != 1 || firstTime) return 4;
        if (ids.GetId(second, out firstTime) != 2 || !firstTime) return 5;
        if (ids.GetId(new Hostile(), out firstTime) != 3 || !firstTime) return 6;
        var objects = new object[200];
        for (int i = 0; i < objects.Length; i++)
        {
            objects[i] = i % 2 == 0 ? new Same() : (object)new Line();
            if (ids.GetId(objects[i], out firstTime) != i + 4 || !firstTime) return 7;
        }
        for (int i = 0; i < objects.Length; i++)
        {
            if (ids.GetId(objects[i], out firstTime) != i + 4 || firstTime) return 8;
        }
        if (ids.GetId(first, out _) != 1 || ids.HasId(second, out _) != 2) return 9;
        try { ids.GetId(null, out _); return 10; } catch (ArgumentNullException) { }
        try { ids.HasId(null, out _); return 11; } catch (ArgumentNullException) { }
        return 0;
    }

    static int Formatters()
    {
        var formatter = new Tracking();
        formatter.Member("n", null);
        formatter.Member("b", true);
        formatter.Member("c", 'x');
        formatter.Member("sb", (sbyte)-1);
        formatter.Member("by", (byte)1);
        formatter.Member("s", (short)1);
        formatter.Member("i", 1);
        formatter.Member("l", 1L);
        formatter.Member("f", 1f);
        formatter.Member("d", 1d);
        formatter.Member("dt", DateTime.MinValue);
        formatter.Member("m", 1m);
        formatter.Member("us", (ushort)1);
        formatter.Member("ui", 1u);
        formatter.Member("ul", 1ul);
        formatter.Member("arr", new int[2]);
        formatter.Member("e", Status.Closed);
        formatter.Member("ts", TimeSpan.Zero);
        formatter.Member("pt", new Point());
        formatter.Member("str", "text");
        formatter.Member("obj", new Line());
        // The formatter names each member's type (issue 225: without type names a type's name reads its ID).
        var expected = $"ref/{typeof(object).Name}:n;bool:b;char:c;sbyte:sb;byte:by;short:s;int:i;long:l;float:f;double:d;date:dt;decimal:m;ushort:us;uint:ui;ulong:ul;array/{typeof(int[]).Name}:arr;value/{typeof(Status).Name}:e;value/{typeof(TimeSpan).Name}:ts;value/{typeof(Point).Name}:pt;ref/{typeof(string).Name}:str;ref/{typeof(Line).Name}:obj;";
        if (formatter.Log.ToString() != expected) return 1;
        if (formatter.Queue(null) != 0) return 2;
        var a = new Line();
        var b = new Line();
        if (formatter.Queue(a) != 1 || formatter.Queue(b) != 2 || formatter.Queue(a) != 1) return 3;
        long id;
        if (!ReferenceEquals(formatter.Next(out id), a) || id != 1) return 4;
        if (!ReferenceEquals(formatter.Next(out id), b) || id != 2) return 5;
        if (formatter.Next(out id) != null || id != 0) return 6;
        formatter.Smuggle(new Line());
        try { formatter.Next(out id); return 7; }
        catch (SerializationException ex) { if (ex.Message != "Object has never been assigned an objectID.") return 8; }
        if (formatter.Ids.HasId(a, out _) != 1) return 9;
        IFormatter contract = formatter;
        var context = new StreamingContext(StreamingContextStates.Remoting);
        contract.Context = context;
        return contract.Context.State == StreamingContextStates.Remoting && contract.Deserialize(null) == null ? 0 : 10;
    }

    static int Converter()
    {
        var converter = new FormatterConverter();
        if (converter.ToInt32("42") != 42 || converter.ToDouble("1.5") != 1.5 || converter.ToBoolean("true") != true) return 1;
        if (converter.ToString(12) != "12" || converter.ToChar(65) != 'A' || converter.ToDecimal("2.5") != 2.5m) return 2;
        if (!(converter.Convert("5", typeof(long)) is long five) || five != 5) return 3;
        if (!(converter.Convert(3, TypeCode.Double) is double three) || three != 3d) return 4;
        try { converter.ToInt32(null); return 5; } catch (ArgumentNullException) { }
        try { converter.Convert(null, typeof(int)); return 6; } catch (ArgumentNullException) { }
        try { converter.Convert(null, TypeCode.Int32); return 7; } catch (ArgumentNullException) { }
        try { converter.ToByte(300); return 8; } catch (OverflowException) { }
        try { converter.ToInt32("nope"); return 9; } catch (FormatException) { }
        var seven = new Seven();
        if (converter.ToInt32(seven) != 7 || !ReferenceEquals(seven.Seen, CultureInfo.InvariantCulture)) return 10;
        IFormatterConverter contract = converter;
        return contract.ToUInt64("18446744073709551615") == ulong.MaxValue ? 0 : 11;
    }

    static int Selectors()
    {
        var marker = new object();
        var all = new StreamingContext(StreamingContextStates.All, marker);
        var file = new StreamingContext(StreamingContextStates.File, marker);
        var first = new SurrogateSelector();
        var surrogate = new Surrogate();
        first.AddSurrogate(typeof(Order), all, surrogate);
        ISurrogateSelector found;
        if (!ReferenceEquals(first.GetSurrogate(typeof(Order), file, out found), surrogate) || !ReferenceEquals(found, first)) return 1;
        if (first.GetSurrogate(typeof(Order), new StreamingContext(StreamingContextStates.File, new object()), out found) != null || !ReferenceEquals(found, first)) return 2;
        if (first.GetSurrogate(typeof(Line), all, out found) != null) return 3;
        try { first.AddSurrogate(typeof(Order), all, new Surrogate()); return 4; } catch (ArgumentException) { }
        var narrow = new SurrogateSelector();
        narrow.AddSurrogate(typeof(Line), file, surrogate);
        if (narrow.GetSurrogate(typeof(Line), all, out _) != null || narrow.GetSurrogate(typeof(Line), file, out _) == null) return 5;
        var second = new SurrogateSelector();
        var lineSurrogate = new Surrogate();
        second.AddSurrogate(typeof(Line), all, lineSurrogate);
        first.ChainSelector(second);
        if (!ReferenceEquals(first.GetNextSelector(), second)) return 6;
        if (!ReferenceEquals(first.GetSurrogate(typeof(Line), all, out found), lineSurrogate) || !ReferenceEquals(found, second)) return 7;
        if (first.GetSurrogate(typeof(Point), all, out found) != null || !ReferenceEquals(found, second)) return 8;
        var third = new SurrogateSelector();
        first.ChainSelector(third);
        if (!ReferenceEquals(first.GetNextSelector(), third) || !ReferenceEquals(third.GetNextSelector(), second)) return 9;
        try { first.ChainSelector(first); return 10; } catch (SerializationException) { }
        try { second.ChainSelector(first); return 11; } catch (ArgumentException) { }
        try { first.ChainSelector(null); return 12; } catch (ArgumentNullException) { }
        try { first.AddSurrogate(null, all, surrogate); return 13; } catch (ArgumentNullException) { }
        first.RemoveSurrogate(typeof(Order), file);
        if (first.GetSurrogate(typeof(Order), all, out _) != null) return 14;
        second.RemoveSurrogate(typeof(Point), all);
        return 0;
    }

    static int Services()
    {
        FormatterServices.CheckTypeSecurity(null, (TypeFilterLevel)99);
        try { FormatterServices.GetSurrogateForCyclicalReference(null); return 1; } catch (ArgumentNullException) { }
        var inner = new Surrogate();
        var wrapper = FormatterServices.GetSurrogateForCyclicalReference(inner);
        if (ReferenceEquals(wrapper, inner)) return 2;
        var payload = new Line();
        wrapper.GetObjectData(payload, null, default);
        if (!ReferenceEquals(wrapper.SetObjectData(payload, null, default, null), payload) || inner.Calls != 11) return 3;
        #if !DESKTOP_ORACLE
        var caught = 0;
        try { FormatterServices.GetSerializableMembers(typeof(Order)); } catch (PlatformNotSupportedException) { caught++; }
        try { FormatterServices.GetSerializableMembers(typeof(Order), default); } catch (PlatformNotSupportedException) { caught++; }
        try { FormatterServices.GetObjectData(payload, new System.Reflection.MemberInfo[0]); } catch (PlatformNotSupportedException) { caught++; }
        try { FormatterServices.PopulateObjectMembers(payload, new System.Reflection.MemberInfo[0], new object[0]); } catch (PlatformNotSupportedException) { caught++; }
        try { FormatterServices.GetTypeFromAssembly(null, "SerializationPrimitivesFixture.Order"); } catch (PlatformNotSupportedException) { caught++; }
        if (caught != 5) return 4;
        #endif
        return 0;
    }

    static int Managers()
    {
        var manager = new ObjectManager(new SurrogateSelector(), new StreamingContext(StreamingContextStates.All));
        try { manager.GetObject(0); return 1; } catch (ArgumentOutOfRangeException) { }
        if (manager.GetObject(5) != null) return 2;
        manager.RaiseDeserializationEvent();
        manager.RaiseDeserializationEvent();
        ObjectManager lookup = new LookupManager();
        if ((string)lookup.GetObject(3) != "three" || lookup.GetObject(4) != null) return 3;
        var serialization = new SerializationObjectManager(new StreamingContext(StreamingContextStates.All));
        serialization.RaiseOnSerializedEvent();
        serialization.RaiseOnSerializedEvent();
        #if !DESKTOP_ORACLE
        var caught = 0;
        var callbacks = new Callbacks();
        try { serialization.RegisterObject(callbacks); } catch (PlatformNotSupportedException) { caught++; }
        serialization.RaiseOnSerializedEvent();
        try { manager.RegisterObject(callbacks, 1); } catch (PlatformNotSupportedException) { caught++; }
        try { manager.RegisterObject(callbacks, 1, null); } catch (PlatformNotSupportedException) { caught++; }
        try { manager.RegisterObject(callbacks, 1, null, 0, null); } catch (PlatformNotSupportedException) { caught++; }
        try { manager.RegisterObject(callbacks, 1, null, 0, null, null); } catch (PlatformNotSupportedException) { caught++; }
        try { manager.RecordFixup(1, null, 2); } catch (PlatformNotSupportedException) { caught++; }
        try { manager.RecordDelayedFixup(1, "x", 2); } catch (PlatformNotSupportedException) { caught++; }
        try { manager.RecordArrayElementFixup(1, 0, 2); } catch (PlatformNotSupportedException) { caught++; }
        try { manager.RecordArrayElementFixup(1, new[] { 0 }, 2); } catch (PlatformNotSupportedException) { caught++; }
        try { manager.DoFixups(); } catch (PlatformNotSupportedException) { caught++; }
        try { manager.RaiseOnDeserializingEvent(callbacks); } catch (PlatformNotSupportedException) { caught++; }
        if (caught != 11 || Callbacks.Calls != 0 || manager.GetObject(1) != null) return 4;
        manager.RaiseDeserializationEvent();
        #endif
        return 0;
    }

    static int Binary()
    {
        var formatter = new BinaryFormatter();
        if (formatter.TypeFormat != FormatterTypeStyle.TypesAlways || formatter.AssemblyFormat != FormatterAssemblyStyle.Simple) return 1;
        if (formatter.FilterLevel != TypeFilterLevel.Full || formatter.Binder != null || formatter.SurrogateSelector != null) return 2;
        if (formatter.Context.State != StreamingContextStates.All || formatter.Context.Context != null) return 3;
        var selector = new SurrogateSelector();
        var marker = new object();
        var configured = new BinaryFormatter(selector, new StreamingContext(StreamingContextStates.File, marker));
        if (!ReferenceEquals(configured.SurrogateSelector, selector) || !ReferenceEquals(configured.Context.Context, marker) || configured.Context.State != StreamingContextStates.File) return 4;
        configured.TypeFormat = (FormatterTypeStyle)99;
        configured.AssemblyFormat = (FormatterAssemblyStyle)98;
        configured.FilterLevel = (TypeFilterLevel)97;
        configured.SurrogateSelector = null;
        if ((int)configured.TypeFormat != 99 || (int)configured.AssemblyFormat != 98 || (int)configured.FilterLevel != 97 || configured.SurrogateSelector != null) return 5;
        IFormatter contract = configured;
        var caught = 0;
        try { contract.Serialize(new Untouchable(), new Line()); } catch (PlatformNotSupportedException) { caught++; }
        try { contract.Deserialize(new Untouchable()); } catch (PlatformNotSupportedException) { caught++; }
        try { formatter.Serialize(null, null); } catch (PlatformNotSupportedException) { caught++; }
        try { formatter.Deserialize(null); } catch (PlatformNotSupportedException) { caught++; }
        return caught == 4 ? 0 : 6;
    }
}
