using SWDataExtractor.Data;

namespace SWDataExtractor.Tests;

public sealed class DbContextExtensionsTests
{
    [Theory]
    [InlineData("Server=localhost;Initial Catalog=swdata;Integrated Security=true", true)]
    [InlineData("Server=myhost\\SQLEXPRESS;Database=sw;User Id=sa;Password=x",      true)]
    [InlineData("Initial Catalog=sw;Server=.;Trusted_Connection=yes",               true)]
    [InlineData("Data Source=swdata.db",                                             false)]
    [InlineData("Data Source=:memory:",                                              false)]
    [InlineData("Filename=./local.db;Cache=shared",                                  false)]
    public void EsSqlServer_DetectaProveedorCorrectamente(string cadena, bool esperado)
    {
        Assert.Equal(esperado, DbContextExtensions.EsSqlServer(cadena));
    }
}
