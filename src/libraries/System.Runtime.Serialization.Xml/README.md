# NetWasm.System.Runtime.Serialization.Xml

`NetWasm.System.Runtime.Serialization.Xml` supplies the `System.Runtime.Serialization.Xml`
assembly for the `netwasm0.1` profile: XmlDictionary and the XML dictionary readers and
writers (text, binary and canonical), the JSON reader and writer from
`JsonReaderWriterFactory`, `XmlObjectSerializer` for hand-written serializers and
`XmlSerializableServices`.

`DataContractSerializer`, `DataContractJsonSerializer` and `XsdDataContractExporter` can be
constructed and configured, but serializing, deserializing and exporting need reflection
and throw `PlatformNotSupportedException` on NetWasm.

The package carries the `NetWasm,Version=v0.1` asset and targets only `netwasm0.1`.
