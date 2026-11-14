using System;
using System.Data;
using System.Data.Common;
using System.Data.SqlTypes;
using System.IO;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.System.Data.Common.Tests;

public sealed class DataTests
{
    private static DataTable CreateOrders()
    {
        var table = new DataTable("Orders");
        var id = table.Columns.Add("Id", typeof(int));
        id.AutoIncrement = true;
        id.AutoIncrementSeed = 1;
        table.Columns.Add("Customer", typeof(string));
        table.Columns.Add("Amount", typeof(decimal));
        table.Columns.Add("Total", typeof(decimal), "Amount * 2");
        table.PrimaryKey = new[] { id };
        table.Rows.Add(null, "ann", 10m);
        table.Rows.Add(null, "bob", 25m);
        table.Rows.Add(null, "ann", 5m);
        table.AcceptChanges();
        return table;
    }

    [Test]
    public async Task TablesFindSelectAndComputeThroughTheExpressionEngine()
    {
        var table = CreateOrders();
        var selected = table.Select("Customer = 'ann'", "Amount DESC");

        await Assert.That(table.Rows.Find(2)!["Customer"]).IsEqualTo("bob");
        await Assert.That(selected.Length).IsEqualTo(2);
        await Assert.That(selected[0]["Amount"]).IsEqualTo(10m);
        await Assert.That(table.Compute("SUM(Amount)", "Customer = 'ann'")).IsEqualTo(15m);
        await Assert.That(table.Rows[1]["Total"]).IsEqualTo(50m);
        await Assert.That(table.Compute("COUNT(Id)", string.Empty)).IsEqualTo(3);
    }

    [Test]
    public async Task RowStatesAndVersionsFollowEditsAcceptAndReject()
    {
        var table = CreateOrders();
        var row = table.Rows[0];
        row["Amount"] = 12m;
        var modified = row.RowState;
        var original = row["Amount", DataRowVersion.Original];
        row.RejectChanges();
        var added = table.Rows.Add(null, "cy", 1m);
        var addedState = added.RowState;
        added.Delete();

        await Assert.That(modified).IsEqualTo(DataRowState.Modified);
        await Assert.That(original).IsEqualTo(10m);
        await Assert.That(row["Amount"]).IsEqualTo(10m);
        await Assert.That(addedState).IsEqualTo(DataRowState.Added);
        await Assert.That(added.RowState).IsEqualTo(DataRowState.Detached);
        await Assert.That(table.Rows.Count).IsEqualTo(3);
    }

    [Test]
    public async Task RelationsCascadeAndViewsTrackLiveChanges()
    {
        var set = new DataSet("Shop");
        var customers = set.Tables.Add("Customers");
        customers.Columns.Add("Name", typeof(string));
        customers.PrimaryKey = new[] { customers.Columns[0] };
        var orders = CreateOrders();
        set.Tables.Add(orders);
        customers.Rows.Add("ann");
        customers.Rows.Add("bob");
        var relation = set.Relations.Add("CustomerOrders", customers.Columns["Name"]!, orders.Columns["Customer"]!);

        var view = new DataView(orders, "Amount > 6", "Amount", DataViewRowState.CurrentRows);
        var before = view.Count;
        var annOrders = customers.Rows[0].GetChildRows(relation).Length;
        customers.Rows[0].Delete();

        await Assert.That(before).IsEqualTo(2);
        await Assert.That(annOrders).IsEqualTo(2);
        await Assert.That(orders.Select(string.Empty, string.Empty, DataViewRowState.CurrentRows).Length).IsEqualTo(1);
        await Assert.That(view.Count).IsEqualTo(1);
    }

    [Test]
    public async Task DataSetXmlRoundTripsSchemaAndData()
    {
        var set = new DataSet("Shop");
        var orders = CreateOrders();
        orders.Columns.Add("Key", typeof(Guid));
        orders.Rows[0]["Key"] = new Guid("00000000-0000-0000-0000-000000000042");
        orders.MinimumCapacity = 10;
        set.Tables.Add(orders);
        set.EnforceConstraints = false;
        set.AcceptChanges();

        using var writer = new StringWriter();
        set.WriteXml(writer, XmlWriteMode.WriteSchema);
        var xml = writer.ToString();
        var schema = set.GetXmlSchema();

        var copy = new DataSet();
        copy.ReadXml(new StringReader(xml), XmlReadMode.ReadSchema);
        var table = copy.Tables["Orders"]!;

        await Assert.That(schema.Contains("msdata:AutoIncrement=\"true\"", StringComparison.Ordinal)).IsTrue();
        await Assert.That(schema.Contains("msdata:DataType=\"System.Guid, System.Private.CoreLib", StringComparison.Ordinal)).IsTrue();
        // Table and dataset properties are written and read through their property descriptors.
        await Assert.That(schema.Contains("msdata:MinimumCapacity=\"10\"", StringComparison.Ordinal)).IsTrue();
        await Assert.That(schema.Contains("msdata:EnforceConstraints=\"False\"", StringComparison.Ordinal)).IsTrue();
        await Assert.That(table.MinimumCapacity).IsEqualTo(10);
        await Assert.That(copy.EnforceConstraints).IsFalse();
        await Assert.That(table.Rows.Count).IsEqualTo(3);
        await Assert.That(table.Columns["Key"]!.DataType).IsEqualTo(typeof(Guid));
        await Assert.That(table.Columns["Id"]!.AutoIncrement).IsTrue();
        await Assert.That(table.Columns["Total"]!.Expression).IsEqualTo("Amount * 2");
        await Assert.That(table.Rows[0]["Key"]).IsEqualTo(new Guid("00000000-0000-0000-0000-000000000042"));
        await Assert.That(table.Rows[1]["Total"]).IsEqualTo(50m);
    }

    [Test]
    public async Task TableReaderFieldAccessAndSqlTypes()
    {
        var table = CreateOrders();
        table.Columns.Add("Discount", typeof(int));
        using var reader = table.CreateDataReader();
        var rows = 0;
        while (reader.Read())
        {
            rows++;
        }

        var discount = table.Rows[0].Field<int?>("Discount");
        var sum = new SqlInt32(40) + new SqlInt32(2);
        var nullSum = SqlInt32.Null + new SqlInt32(1);

        await Assert.That(rows).IsEqualTo(3);
        await Assert.That(discount).IsNull();
        await Assert.That(table.Rows[0].Field<string>("Customer")).IsEqualTo("ann");
        await Assert.That(sum.Value).IsEqualTo(42);
        await Assert.That(nullSum.IsNull).IsTrue();
        await Assert.That(new SqlString("abc").CompareTo(new SqlString("abd")) < 0).IsTrue();
    }

    [Test]
    public async Task ConnectionStringsAndInstanceProviderRegistration()
    {
        var builder = new DbConnectionStringBuilder { ConnectionString = "Data Source=server; Initial Catalog='db;1'" };
        DbProviderFactories.RegisterFactory("NetWasm.Test", TestFactory.Instance);
        var found = DbProviderFactories.TryGetFactory("NetWasm.Test", out var factory);
        var missing = DbProviderFactories.TryGetFactory("NetWasm.Missing", out _);
        DbProviderFactories.UnregisterFactory("NetWasm.Test");

        await Assert.That(builder["data source"]).IsEqualTo("server");
        await Assert.That(builder["initial catalog"]).IsEqualTo("db;1");
        await Assert.That(found && ReferenceEquals(factory, TestFactory.Instance)).IsTrue();
        await Assert.That(missing).IsFalse();
    }

    [Test]
    public async Task BinaryRemotingFormatIsRejected()
    {
        var rejected = false;
        try
        {
#pragma warning disable SYSLIB0038 // The test checks the obsolete binary format is rejected.
            new DataSet().RemotingFormat = SerializationFormat.Binary;
#pragma warning restore SYSLIB0038
        }
        catch (global::System.ComponentModel.InvalidEnumArgumentException)
        {
            rejected = true;
        }

        await Assert.That(rejected).IsTrue();
    }

#if NETWASM
    [Test]
    public async Task ReflectionFormatterAndFilePathsAreUnsupported()
    {
        // NetWasm has no reflection-based activation or type lookup, binary formatter or file-name XML overloads;
        // these bind and throw PlatformNotSupportedException.
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

        DbProviderFactories.RegisterFactory("NetWasm.Deferred", "Some.Factory, Some.Assembly");
        var deferred = Unsupported(() => DbProviderFactories.GetFactory("NetWasm.Deferred"));
        DbProviderFactories.UnregisterFactory("NetWasm.Deferred");

        await Assert.That(deferred).IsTrue();
        await Assert.That(Unsupported(() => DbProviderFactories.RegisterFactory("NetWasm.Type", typeof(TestFactory)))).IsTrue();
        await Assert.That(Unsupported(() => new DerivedTable().Clone())).IsTrue();
        await Assert.That(Unsupported(() => new DataSet().WriteXml("out.xml"))).IsTrue();
    }

    private sealed class DerivedTable : DataTable
    {
    }
#endif

    private sealed class TestFactory : DbProviderFactory
    {
        public static readonly TestFactory Instance = new();
    }
}
