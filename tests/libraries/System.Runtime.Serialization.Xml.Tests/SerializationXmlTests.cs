using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Schema;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.System.Runtime.Serialization.Xml.Tests;

public sealed class SerializationXmlTests
{
    [Test]
    public async Task BinaryDictionaryWriterAndReaderRoundTripTypedValues()
    {
        var dictionary = new XmlDictionary();
        var root = dictionary.Add("root");
        var ns = XmlDictionaryString.Empty;
        var stream = new MemoryStream();
        using (var writer = XmlDictionaryWriter.CreateBinaryWriter(stream, dictionary, null, ownsStream: false))
        {
            writer.WriteStartElement(root, ns);
            writer.WriteStartElement("count");
            writer.WriteValue(42);
            writer.WriteEndElement();
            writer.WriteStartElement("ids");
            writer.WriteArray(null, "v", null, new[] { 1L, 2L, 3L }, 0, 3);
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        stream.Position = 0;
        using var reader = XmlDictionaryReader.CreateBinaryReader(stream, dictionary, XmlDictionaryReaderQuotas.Max);
        reader.ReadStartElement(root, ns);
        var count = reader.ReadElementContentAsInt();
        reader.ReadStartElement("ids");
        var ids = reader.ReadInt64Array("v", string.Empty);

        await Assert.That(count).IsEqualTo(42);
        await Assert.That(ids.Length).IsEqualTo(3);
        await Assert.That(ids[2]).IsEqualTo(3L);
    }

    [Test]
    public async Task TextDictionaryWriterWritesUtf8Xml()
    {
        var stream = new MemoryStream();
        using (var writer = XmlDictionaryWriter.CreateTextWriter(stream, Encoding.UTF8, ownsStream: false))
        {
            writer.WriteStartElement("item");
            writer.WriteAttributeString("id", "7");
            writer.WriteString("a<b");
            writer.WriteEndElement();
        }

        await Assert.That(Encoding.UTF8.GetString(stream.ToArray())).IsEqualTo("<item id=\"7\">a&lt;b</item>");
    }

    [Test]
    public async Task JsonWriterAndReaderMapJsonToXml()
    {
        var stream = new MemoryStream();
        using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, ownsStream: false))
        {
            writer.WriteStartElement("root");
            writer.WriteAttributeString("type", "object");
            writer.WriteStartElement("name");
            writer.WriteString("ann");
            writer.WriteEndElement();
            writer.WriteStartElement("count");
            writer.WriteAttributeString("type", "number");
            writer.WriteString("3");
            writer.WriteEndElement();
            writer.WriteEndElement();
        }
        var json = Encoding.UTF8.GetString(stream.ToArray());

        using var reader = JsonReaderWriterFactory.CreateJsonReader(Encoding.UTF8.GetBytes("{\"a\":[1,true]}"), XmlDictionaryReaderQuotas.Max);
        var document = new XmlDocument();
        document.Load(reader);

        await Assert.That(json).IsEqualTo("{\"name\":\"ann\",\"count\":3}");
        await Assert.That(document.DocumentElement!.Name).IsEqualTo("root");
        await Assert.That(document.DocumentElement["a"]!.GetAttribute("type")).IsEqualTo("array");
        await Assert.That(document.DocumentElement["a"]!.ChildNodes.Count).IsEqualTo(2);
    }

    [Test]
    public async Task UniqueIdFormatsGuidsAsUrns()
    {
        var guid = new Guid("00112233-4455-6677-8899-aabbccddeeff");
        var id = new UniqueId(guid);
        var parsed = new UniqueId("urn:uuid:00112233-4455-6677-8899-aabbccddeeff");
        var text = new UniqueId("not-a-guid");

        await Assert.That(id.ToString()).IsEqualTo("urn:uuid:00112233-4455-6677-8899-aabbccddeeff");
        await Assert.That(parsed.IsGuid && parsed == id).IsTrue();
        await Assert.That(text.IsGuid).IsFalse();
    }

    [Test]
    public async Task XmlObjectSerializerSubclassRoundTripsThroughStreams()
    {
        var serializer = new PointSerializer();
        var stream = new MemoryStream();
        serializer.WriteObject(stream, new Point(3, 4));
        var written = Encoding.UTF8.GetString(stream.ToArray());
        stream.Position = 0;
        var point = (Point)serializer.ReadObject(stream)!;

        await Assert.That(written).IsEqualTo("<point x=\"3\" y=\"4\"/>");
        await Assert.That(point).IsEqualTo(new Point(3, 4));
        await Assert.That(stream.CanRead).IsTrue();
    }

    [Test]
    public async Task SerializableServicesTransferNodesAndBuildTheDefaultSchema()
    {
        var reader = XmlReader.Create(new StringReader("<w a=\"1\"><x>t</x><y/></w>"));
        reader.MoveToContent();
        var nodes = XmlSerializableServices.ReadNodes(reader);
        var output = new StringWriter();
        using (var writer = XmlWriter.Create(output, new XmlWriterSettings { OmitXmlDeclaration = true, ConformanceLevel = ConformanceLevel.Fragment }))
        {
            XmlSerializableServices.WriteNodes(writer, nodes[1..]);
        }

        var schemas = new XmlSchemaSet();
        XmlSerializableServices.AddDefaultSchema(schemas, new XmlQualifiedName("Bag", "urn:bag"));
        schemas.Compile();
        XmlSchemaComplexType? bag = null;
        foreach (XmlSchema schema in schemas.Schemas("urn:bag"))
        {
            bag = schema.SchemaTypes[new XmlQualifiedName("Bag", "urn:bag")] as XmlSchemaComplexType;
        }

        await Assert.That(nodes.Length).IsEqualTo(3);
        await Assert.That(nodes[0] is XmlAttribute { Name: "a", Value: "1" }).IsTrue();
        await Assert.That(output.ToString()).IsEqualTo("<x>t</x><y />");
        await Assert.That(bag is { IsMixed: true }).IsTrue();
    }

    [Test]
    public async Task SerializersKeepTheirConfiguration()
    {
        var knownTypes = new[] { typeof(int) };
        var serializer = new DataContractSerializer(typeof(Point), new DataContractSerializerSettings { MaxItemsInObjectGraph = 10, KnownTypes = knownTypes, PreserveObjectReferences = true });
        knownTypes[0] = typeof(string);
        var json = new DataContractJsonSerializer(typeof(Point), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true, MaxItemsInObjectGraph = 5 });
        var exporter = new XsdDataContractExporter();

        await Assert.That(serializer.MaxItemsInObjectGraph).IsEqualTo(10);
        await Assert.That(serializer.PreserveObjectReferences).IsTrue();
        await Assert.That(serializer.KnownTypes[0]).IsEqualTo(typeof(int));
        await Assert.That(json.UseSimpleDictionaryFormat && json.MaxItemsInObjectGraph == 5).IsTrue();
        await Assert.That(exporter.Schemas.Contains(XmlSchema.Namespace)).IsTrue();
    }

#if NETWASM
    [Test]
    public async Task ReflectionBasedSerializationAndExportAreUnsupported()
    {
        // NetWasm has no reflection-based DataContract engine, so these bind and throw PlatformNotSupportedException;
        // desktop .NET runs them.
        static bool Unsupported(Action action)
        {
            try
            {
                action();
                return false;
            }
            catch (PlatformNotSupportedException)
            {
                return true;
            }
        }

        var untouched = new MemoryStream();
        var write = Unsupported(() => new DataContractSerializer(typeof(Point)).WriteObject(untouched, new Point(1, 2)));
        var read = Unsupported(() => new DataContractJsonSerializer(typeof(Point)).ReadObject(untouched));
        var knownType = Unsupported(() => new DataContractJsonSerializer(typeof(Point), new[] { typeof(int) }));
        var export = Unsupported(() => new XsdDataContractExporter().Export(typeof(Point)));

        await Assert.That(write && read && knownType && export).IsTrue();
        await Assert.That(untouched.Length).IsEqualTo(0L);
    }
#endif

    public readonly record struct Point(int X, int Y);

    private sealed class PointSerializer : XmlObjectSerializer
    {
        public override void WriteStartObject(XmlDictionaryWriter writer, object? graph) => writer.WriteStartElement("point");

        public override void WriteObjectContent(XmlDictionaryWriter writer, object? graph)
        {
            var point = (Point)graph!;
            writer.WriteAttributeString("x", point.X.ToString(global::System.Globalization.CultureInfo.InvariantCulture));
            writer.WriteAttributeString("y", point.Y.ToString(global::System.Globalization.CultureInfo.InvariantCulture));
        }

        public override void WriteEndObject(XmlDictionaryWriter writer) => writer.WriteEndElement();

        public override bool IsStartObject(XmlDictionaryReader reader) => reader.IsStartElement("point");

        public override object? ReadObject(XmlDictionaryReader reader, bool verifyObjectName)
        {
            reader.MoveToContent();
            var point = new Point(int.Parse(reader.GetAttribute("x")!), int.Parse(reader.GetAttribute("y")!));
            reader.Skip();
            return point;
        }
    }
}
