using NetWasm.Toolchain.Prerequisites;

namespace NetWasm.Toolchain.Tests;

public sealed class SystemHostEnvironmentVariableReaderTests
{
    [Fact]
    public void ReadReturnsTheExactSelectedValueAndMissingState()
    {
        const string name = "NETWASM_SYSTEM_ENVIRONMENT_READER_TEST_VALUE";
        var original = Environment.GetEnvironmentVariable(name);
        var reader = Assert.IsAssignableFrom<IHostEnvironmentVariableReader>(
            new SystemHostEnvironmentVariableReader());

        try
        {
            Environment.SetEnvironmentVariable(name, "selected");
            Assert.Equal("selected", reader.Read(name));

            Environment.SetEnvironmentVariable(name, null);
            Assert.Null(reader.Read(name));
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, original);
        }
    }

    [Fact]
    public void ReadRejectsMissingNames()
    {
        var reader = Assert.IsAssignableFrom<IHostEnvironmentVariableReader>(
            new SystemHostEnvironmentVariableReader());

        Assert.Throws<ArgumentNullException>(() => reader.Read(null!));
        Assert.Throws<ArgumentException>(() => reader.Read(" "));
    }
}
