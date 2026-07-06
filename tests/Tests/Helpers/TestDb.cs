using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SWDataExtractor.Data;

namespace SWDataExtractor.Tests.Helpers;

/// <summary>
/// Wrapper de SQLite en memoria para tests. Mantiene la conexión abierta para que
/// la BD no sea liberada entre operaciones del mismo test.
/// </summary>
internal sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _conn;
    public AppDbContext Db { get; }

    public TestDb()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        Db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(_conn)
                .Options);
        Db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Db.Dispose();
        _conn.Close();
        _conn.Dispose();
    }
}
