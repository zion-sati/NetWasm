using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Resolvers;
using System.Xml.Schema;
using System.Xml.Serialization;
using System.Xml.XPath;
using System.Xml.Xsl;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.System.Xml.Tests;

public sealed class XmlTests
{
    [Test]
    public async Task ReaderAndWriterPreserveElementsAttributesAndText()
    {
        using var reader = XmlReader.Create(new StringReader("<root a=\"7\">value</root>"));
        var first = reader.Read();
        var element = reader.NodeType == XmlNodeType.Element && reader.LocalName == "root";
        var attribute = reader.GetAttribute("a");
        var second = reader.Read();
        var text = reader.NodeType == XmlNodeType.Text ? reader.Value : string.Empty;

        using var output = new StringWriter();
        using (var writer = XmlWriter.Create(output, new XmlWriterSettings { OmitXmlDeclaration = true }))
        {
            writer.WriteStartElement("root");
            writer.WriteAttributeString("a", "7");
            writer.WriteString("value");
            writer.WriteEndElement();
            writer.Flush();
        }

        await Assert.That(first && element).IsTrue();
        await Assert.That(attribute).IsEqualTo("7");
        await Assert.That(second && text == "value").IsTrue();
        await Assert.That(output.ToString()).IsEqualTo("<root a=\"7\">value</root>");
    }

    [Test]
    public async Task AsyncReaderUsesConfiguredSuspensionContract()
    {
        var settings = new XmlReaderSettings { Async = true };
        using var reader = XmlReader.Create(new StringReader("<root>async</root>"), settings);
        var first = await reader.ReadAsync();
        var element = reader.NodeType == XmlNodeType.Element && reader.Name == "root";
        var second = await reader.ReadAsync();
        var text = reader.NodeType == XmlNodeType.Text ? reader.Value : string.Empty;

        await Assert.That(first && element).IsTrue();
        await Assert.That(second && text == "async").IsTrue();
    }

    [Test]
    public async Task ResolverAndDtdPolicyDenyAmbientEntitiesAndAllowPreloadedData()
    {
        var settings = new XmlReaderSettings();
        var denied = false;
        try
        {
            using var reader = XmlReader.Create(
                new StringReader("<!DOCTYPE root SYSTEM 'urn:missing'><root/>"), settings);
            while (reader.Read())
            {
            }
        }
        catch (XmlException)
        {
            denied = true;
        }

        var resolver = new FixtureResolver();
        using var entity = (TextReader)resolver.GetEntity(new Uri("urn:fixture"), null, typeof(TextReader))!;

        await Assert.That(settings.DtdProcessing).IsEqualTo(DtdProcessing.Prohibit);
        await Assert.That(denied).IsTrue();
        await Assert.That(entity.ReadToEnd()).IsEqualTo("preloaded");
    }

    [Test]
    public async Task PreloadedXhtmlDtdResolvesNamedEntities()
    {
        // The XHTML 1.0 strict DTD and its entity sets come from XmlPreloadedResolver's known DTDs,
        // with no network access.
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Parse,
            XmlResolver = new XmlPreloadedResolver(XmlKnownDtds.Xhtml10),
        };
        const string page =
            "<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0 Strict//EN\" \"http://www.w3.org/TR/xhtml1/DTD/xhtml1-strict.dtd\">" +
            "<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>t</title></head><body><p>a&nbsp;b&euro;</p></body></html>";
        using var reader = XmlReader.Create(new StringReader(page), settings);
        var document = new XmlDocument();
        document.Load(reader);

        await Assert.That(document.DocumentElement!.InnerText).IsEqualTo("ta\u00A0b\u20AC");
    }

    [Test]
    public async Task DomMutationCloneAndSelectionRemainInMemory()
    {
        var document = new XmlDocument();
        document.LoadXml("<root id=\"r1\"><item>one</item><item>two</item></root>");
        var selected = (XmlElement)document.SelectSingleNode("/root/item")!;
        selected.SetAttribute("kind", "first");
        document.DocumentElement!.AppendChild(document.CreateElement("item"));
        var clone = (XmlDocument)document.CloneNode(true);

        await Assert.That(document.DocumentElement.GetAttribute("id")).IsEqualTo("r1");
        await Assert.That(selected.InnerText).IsEqualTo("one");
        await Assert.That(document.SelectNodes("/root/item")!.Count).IsEqualTo(3);
        await Assert.That(clone.SelectSingleNode("/root/item")!.Attributes!.GetNamedItem("kind")!.Value)
            .IsEqualTo("first");
    }

    [Test]
    public async Task XPathDocumentNavigatorSupportsAxesAndPredicates()
    {
        using var reader = XmlReader.Create(
            new StringReader("<root><item id=\"1\">one</item><item id=\"2\">two</item></root>"));
        var document = new XPathDocument(reader);
        var iterator = document.CreateNavigator().Select("/root/item[@id='2']");
        var count = 0;
        var value = string.Empty;
        while (iterator.MoveNext())
        {
            count++;
            value = iterator.Current!.Value;
        }

        await Assert.That(count).IsEqualTo(1);
        await Assert.That(value).IsEqualTo("two");
    }

    [Test]
    public async Task XNodeToXPathNavigableNavigatesTheLinqTree()
    {
        var document = XDocument.Parse("<root><item id=\"1\">one</item><item id=\"2\">two</item></root>");
        IXPathNavigable navigable = document.ToXPathNavigable();

        var item = navigable.CreateNavigator()!.SelectSingleNode("/root/item[@id='2']");

        await Assert.That(item).IsNotNull();
        await Assert.That(item!.Value).IsEqualTo("two");
    }

    [Test]
    public async Task LinqXmlTreeAxesAndMutationPreserveOrder()
    {
        var document = XDocument.Parse("<root><item>one</item><item>two</item></root>");
        var root = document.Root!;
        var itemName = XName.Get("item");
        var values = string.Empty;
        foreach (var item in root.Descendants(itemName))
        {
            values += item.Value;
        }

        root.SetAttributeValue(XName.Get("id"), "r1");

        await Assert.That(values).IsEqualTo("onetwo");
        await Assert.That(root.Attribute("id")!.Value).IsEqualTo("r1");
        await Assert.That(document.ToString().Contains("<item>two</item>", StringComparison.Ordinal))
            .IsTrue();
    }

    [Test]
    public async Task SchemaSetCompilesAndValidatesDocumentElements()
    {
        var schemas = new XmlSchemaSet();
        const string schemaText = "<xs:schema xmlns:xs='http://www.w3.org/2001/XMLSchema'><xs:element name='root'/></xs:schema>";
        using var schemaReader = XmlReader.Create(
            new StringReader(schemaText));
        schemas.Add(string.Empty, schemaReader);
        schemas.Compile();

        var valid = new XmlDocument();
        valid.LoadXml("<root/>");
        var invalid = new XmlDocument();
        invalid.LoadXml("<other/>");

        await Assert.That(schemas.IsCompiled).IsTrue();
        await Assert.That(schemas.Contains(string.Empty)).IsTrue();
        await Assert.That(Validate(valid, schemas)).IsTrue();
        await Assert.That(Validate(invalid, schemas)).IsFalse();
    }

    [Test]
    public async Task InterpretedXsltTransformsReaderInputAndReportsMessages()
    {
        const string stylesheet = "<xsl:stylesheet version='1.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'><xsl:template match='/'><root/></xsl:template></xsl:stylesheet>";
        var transform = new XslTransform();
        using var stylesheetReader = XmlReader.Create(new StringReader(stylesheet));
        transform.Load(stylesheetReader);
        using var output = new StringWriter();
        using var writer = XmlWriter.Create(output, new XmlWriterSettings { OmitXmlDeclaration = true });
        using var inputReader = XmlReader.Create(new StringReader("<source/>"));
        var input = new XPathDocument(inputReader);
        transform.Transform(input, null, writer);

        await Assert.That(output.ToString().Contains("<root", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task PrimitiveXmlSerializerRoundTripsWithCustomRootName()
    {
        var serializer = new XmlSerializer(typeof(string), new XmlRootAttribute("value"));
        using var output = new StringWriter();
        serializer.Serialize(output, "portable");
        var value = serializer.Deserialize(new StringReader(output.ToString()));

        await Assert.That(output.ToString().Contains("<value>portable</value>", StringComparison.Ordinal))
            .IsTrue();
        await Assert.That(value is string text && text == "portable").IsTrue();
    }

    [Test]
    public async Task PrimitiveXmlSerializerWritesUpstreamPrimitiveForms()
    {
        static string Write(Type type, object? value, string? defaultNamespace = null)
        {
            var serializer = new XmlSerializer(type, defaultNamespace);
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            using (var writer = XmlWriter.Create(output, new XmlWriterSettings { OmitXmlDeclaration = true }))
            {
                serializer.Serialize(writer, value);
            }
            return output.ToString();
        }

        static object? Read(Type type, string xml, string? defaultNamespace = null) =>
            new XmlSerializer(type, defaultNamespace).Deserialize(XmlReader.Create(new StringReader(xml)));

        await Assert.That(Write(typeof(int), 42)).IsEqualTo("<int>42</int>");
        await Assert.That(Write(typeof(bool), true)).IsEqualTo("<boolean>true</boolean>");
        await Assert.That(Write(typeof(double), double.PositiveInfinity)).IsEqualTo("<double>INF</double>");
        await Assert.That(Write(typeof(byte[]), new byte[] { 1, 2, 3 })).IsEqualTo("<base64Binary>AQID</base64Binary>");
        // The writer chooses the instance namespace's prefix (p1 on .NET 11), so check the attribute, not the prefix.
        var nil = Write(typeof(string), null);
        await Assert.That(nil.Contains(":nil=\"true\"", StringComparison.Ordinal) &&
            nil.Contains("\"http://www.w3.org/2001/XMLSchema-instance\"", StringComparison.Ordinal)).IsTrue();
        await Assert.That(Write(typeof(int), 7, "urn:n")).IsEqualTo("<int xmlns=\"urn:n\">7</int>");

        await Assert.That(Read(typeof(int), "<int>42</int>")).IsEqualTo(42);
        await Assert.That(Read(typeof(double), "<double>-INF</double>")).IsEqualTo(double.NegativeInfinity);
        await Assert.That(Read(typeof(int), "<int xmlns=\"urn:n\">7</int>", "urn:n")).IsEqualTo(7);
        await Assert.That(Read(typeof(string), "<string xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:nil=\"true\" />")).IsNull();
        await Assert.That(new XmlSerializer(typeof(int)).CanDeserialize(XmlReader.Create(new StringReader("<int>1</int>")))).IsTrue();
    }

    [Test]
    public async Task PrimitiveXmlSerializerHonoursRootNameAndNamespace()
    {
        var serializer = new XmlSerializer(typeof(int), new XmlRootAttribute("count") { Namespace = "urn:c" });
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using (var writer = XmlWriter.Create(output, new XmlWriterSettings { OmitXmlDeclaration = true }))
        {
            serializer.Serialize(writer, 5);
        }
        var value = serializer.Deserialize(XmlReader.Create(new StringReader(output.ToString())));
        var rejectsOtherRoot = false;
        try
        {
            serializer.Deserialize(XmlReader.Create(new StringReader("<int>5</int>")));
        }
        catch (InvalidOperationException)
        {
            rejectsOtherRoot = true;
        }

        await Assert.That(output.ToString()).IsEqualTo("<count xmlns=\"urn:c\">5</count>");
        await Assert.That(value).IsEqualTo(5);
        await Assert.That(rejectsOtherRoot).IsTrue();
    }

    [Test]
    public async Task XmlSerializerSubclassDispatchesThroughItsReaderAndWriter()
    {
        var serializer = new PointSerializer();
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using (var writer = XmlWriter.Create(output, new XmlWriterSettings { OmitXmlDeclaration = true }))
        {
            serializer.Serialize(writer, new Point(3, 4));
        }
        var point = (Point)serializer.Deserialize(XmlReader.Create(new StringReader(output.ToString())))!;

        await Assert.That(output.ToString().StartsWith("<point ", StringComparison.Ordinal)).IsTrue();
        await Assert.That(output.ToString().Contains("x=\"3\" y=\"4\"", StringComparison.Ordinal)).IsTrue();
        await Assert.That(point).IsEqualTo(new Point(3, 4));
    }

    [Test]
    public async Task SchemaImporterAndExporterRoundTripSchemaTypes()
    {
        const string xsd =
            "<xs:schema xmlns:xs='http://www.w3.org/2001/XMLSchema' targetNamespace='urn:t' xmlns='urn:t' elementFormDefault='qualified'>" +
            "<xs:element name='order'><xs:complexType><xs:sequence>" +
            "<xs:element name='id' type='xs:int'/><xs:element name='note' type='xs:string' minOccurs='0'/>" +
            "</xs:sequence></xs:complexType></xs:element></xs:schema>";
        var schemas = new XmlSchemas();
        using (var reader = XmlReader.Create(new StringReader(xsd)))
        {
            schemas.Add(XmlSchema.Read(reader, null)!);
        }
        schemas.Compile(null, true);
        var mapping = new XmlSchemaImporter(schemas).ImportTypeMapping(new XmlQualifiedName("order", "urn:t"));

        var exported = new XmlSchemas();
        new XmlSchemaExporter(exported).ExportTypeMapping(mapping);
        var exportedElement = false;
        foreach (XmlSchemaObject item in exported["urn:t"]!.Items)
        {
            exportedElement |= item is XmlSchemaElement { Name: "order" };
        }

        await Assert.That(mapping.ElementName).IsEqualTo("order");
        await Assert.That(mapping.Namespace).IsEqualTo("urn:t");
        await Assert.That(exportedElement).IsTrue();
    }

    [Test]
    public async Task XmlConvertPreservesNames()
    {
        var encoded = XmlConvert.EncodeName("name with space");
        var decoded = XmlConvert.DecodeName(encoded);

        await Assert.That(encoded == "name with space").IsFalse();
        await Assert.That(decoded).IsEqualTo("name with space");
    }

    [Test]
    public async Task XsltScriptIsRejectedBeforeObservableOutput()
    {
        var rejected = false;
        try
        {
            const string stylesheet = "<xsl:stylesheet version='1.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform' xmlns:msxsl='urn:schemas-microsoft-com:xslt'><msxsl:script language='C#' implements-prefix='user'>public string f() => \"x\";</msxsl:script></xsl:stylesheet>";
            var transform = new XslTransform();
            using var stylesheetReader = XmlReader.Create(new StringReader(stylesheet));
            transform.Load(stylesheetReader);
        }
        catch (Exception)
        {
            rejected = true;
        }

        await Assert.That(rejected).IsTrue();
    }

#if NETWASM
    [Test]
    public async Task ReflectionCodeGenerationAndNetworkPathsAreUnsupported()
    {
        // NetWasm has no IL generation, reflection-based XML serialization or network acquisition for System.Xml, so
        // these bind and throw PlatformNotSupportedException; desktop .NET runs them.
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

        var compiled = Unsupported(() => new XslCompiledTransform().Load(XmlReader.Create(new StringReader("<x/>"))));
        var schemaWrite = Unsupported(() => new XmlSchema().Write(new StringWriter()));
        var network = Unsupported(() => new XmlUrlResolver().GetEntity(new Uri("http://example.invalid/a.dtd"), null, null));
        var arbitraryType = Unsupported(() => new XmlSerializer(typeof(Point)));
        var fromTypes = Unsupported(() => XmlSerializer.FromTypes(new[] { typeof(int) }));
        var reflectionImport = Unsupported(() => new XmlReflectionImporter().ImportTypeMapping(typeof(int)));

        await Assert.That(compiled).IsTrue();
        await Assert.That(schemaWrite).IsTrue();
        await Assert.That(network).IsTrue();
        await Assert.That(arbitraryType && fromTypes && reflectionImport).IsTrue();
    }

#endif
    // System.Xml reads and writes files by path, as on .NET (WASI 0.3 file system; test hosts grant a private /tmp).
    [Test]
    public async Task DocumentsRoundTripThroughFilesByPath()
    {
        var directory = Path.Combine(Path.GetTempPath(), "netwasm-xml-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "document.xml");
            var document = new XmlDocument();
            document.LoadXml("<root><item id=\"1\">one</item></root>");
            document.Save(path);

            using (var writer = XmlWriter.Create(Path.Combine(directory, "written.xml")))
            {
                writer.WriteElementString("value", "two");
            }

            var loaded = new XmlDocument();
            loaded.Load(path);
            using var reader = XmlReader.Create(Path.Combine(directory, "written.xml"));
            reader.MoveToContent();

            var missing = false;
            try
            {
                XmlReader.Create(Path.Combine(directory, "missing.xml")).Read();
            }
            catch (FileNotFoundException)
            {
                missing = true;
            }

            await Assert.That(loaded.SelectSingleNode("/root/item[@id='1']")?.InnerText).IsEqualTo("one");
            await Assert.That(reader.ReadElementContentAsString()).IsEqualTo("two");
            await Assert.That(missing).IsTrue();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static bool Validate(XmlDocument document, XmlSchemaSet schemas)
    {
        try
        {
            document.Schemas = schemas;
            document.Validate(null);
            return true;
        }
        catch (XmlSchemaException)
        {
            return false;
        }
    }

    private sealed class FixtureResolver : XmlResolver
    {
        public override object GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn) =>
            new StringReader("preloaded");
    }

    public readonly record struct Point(int X, int Y);

    private sealed class PointSerializer : XmlSerializer
    {
        protected override XmlSerializationWriter CreateWriter() => new PointWriter();

        protected override void Serialize(object? o, XmlSerializationWriter writer) => ((PointWriter)writer).Write((Point)o!);

        protected override XmlSerializationReader CreateReader() => new PointReader();

        protected override object Deserialize(XmlSerializationReader reader) => ((PointReader)reader).Read();
    }

    private sealed class PointWriter : XmlSerializationWriter
    {
        protected override void InitCallbacks()
        {
        }

        public void Write(Point point)
        {
            WriteStartDocument();
            WriteStartElement("point", string.Empty, null, false);
            WriteAttribute("x", point.X.ToString(CultureInfo.InvariantCulture));
            WriteAttribute("y", point.Y.ToString(CultureInfo.InvariantCulture));
            WriteEndElement();
        }
    }

    private sealed class PointReader : XmlSerializationReader
    {
        protected override void InitCallbacks()
        {
        }

        protected override void InitIDs()
        {
        }

        public Point Read()
        {
            Reader.MoveToContent();
            var point = new Point(
                int.Parse(Reader.GetAttribute("x")!, CultureInfo.InvariantCulture),
                int.Parse(Reader.GetAttribute("y")!, CultureInfo.InvariantCulture));
            Reader.Skip();
            return point;
        }
    }

    // XmlUrlResolver reads file URIs, as upstream's does.
    [Test]
    public async Task UrlResolverReadsFileUris()
    {
        var path = Path.Combine(Path.GetTempPath(), "netwasm-xml-" + Guid.NewGuid().ToString("N") + ".xml");
        File.WriteAllText(path, "<root>resolved</root>");
        try
        {
            var resolver = new XmlUrlResolver();
            var uri = resolver.ResolveUri(null, path);
            string text;
            using (var stream = (Stream)resolver.GetEntity(uri, null, typeof(Stream))!)
            using (var reader = new StreamReader(stream))
            {
                text = reader.ReadToEnd();
            }
            int first;
            using (var stream = (Stream)await resolver.GetEntityAsync(uri, null, null))
            {
                first = stream.ReadByte();
            }

            await Assert.That(uri.IsFile).IsTrue();
            await Assert.That(text).IsEqualTo("<root>resolved</root>");
            await Assert.That(first).IsEqualTo((int)'<');
        }
        finally
        {
            File.Delete(path);
        }
    }
}
