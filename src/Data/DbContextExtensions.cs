using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace SWDataExtractor.Data;

public static class DbContextExtensions
{
    /// <summary>
    /// Registra AppDbContext usando SQLite o SQL Server según el prefijo de la cadena de conexión.
    /// SQL Server se activa cuando la cadena contiene "Server=" o "Initial Catalog=".
    /// </summary>
    public static IServiceCollection AddAppDbContext(
        this IServiceCollection services,
        string cadena,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
    {
        return services.AddDbContext<AppDbContext>(opts =>
        {
            if (EsSqlServer(cadena))
                opts.UseSqlServer(cadena);
            else
                opts.UseSqlite(cadena);
        }, lifetime);
    }

    /// <summary>
    /// Inicializa la BD: migraciones EF para SQLite; EnsureCreated para SQL Server
    /// (las migraciones SQL Server se generarán con --provider cuando sea necesario).
    /// </summary>
    public static async Task InicializarAsync(AppDbContext db, string cadena)
    {
        if (EsSqlServer(cadena))
        {
            await db.Database.EnsureCreatedAsync();
        }
        else
        {
            await db.Database.MigrateAsync();
            // WAL: lectores y escritores concurrentes sin bloquearse — necesario cuando la UI
            // está abierta mientras el Batch programado escanea la misma BD. El modo queda
            // persistido en el archivo .db; ejecutarlo en cada arranque es idempotente.
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        }
    }

    public static bool EsSqlServer(string cadena) =>
        cadena.IndexOf("Server=",         StringComparison.OrdinalIgnoreCase) >= 0 ||
        cadena.IndexOf("Initial Catalog=", StringComparison.OrdinalIgnoreCase) >= 0;
}
