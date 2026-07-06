using Microsoft.EntityFrameworkCore;
using SWDataExtractor.Application.Config;
using SWDataExtractor.Data;
using SWDataExtractor.Data.Entities;

namespace SWDataExtractor.Application.Servicios;

public enum Rol { Visualizador, Operador, Administrador }

public class ServicioRoles(AppDbContext db)
{
    private const string ClaveRol = "RolActual";

    public async Task<Rol> ObtenerRolAsync(CancellationToken ct = default)
    {
        var ajuste = await db.AjustesApp
            .FirstOrDefaultAsync(a => a.Clave == ClaveRol, ct);
        return ajuste?.Valor is not null && Enum.TryParse<Rol>(ajuste.Valor, true, out var rol)
            ? rol
            : Rol.Administrador;
    }

    public async Task CambiarRolAsync(Rol rol, CancellationToken ct = default)
    {
        var ajuste = await db.AjustesApp
            .FirstOrDefaultAsync(a => a.Clave == ClaveRol, ct);
        if (ajuste is null)
            db.AjustesApp.Add(new AjusteApp
            {
                Clave       = ClaveRol,
                Valor       = rol.ToString(),
                Descripcion = "Rol activo en esta máquina"
            });
        else
            ajuste.Valor = rol.ToString();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Permisos efectivos para cada rol.</summary>
    public static ConfiguracionFuncionalidades FuncionalidadesParaRol(Rol rol) => rol switch
    {
        Rol.Visualizador => new ConfiguracionFuncionalidades
        {
            ExtraccionProfunda   = false,
            EscrituraPropiedades = false,
            Etiquetas            = false,
            ComparacionBom       = true,
            ExportacionExcel     = true
        },
        Rol.Operador => new ConfiguracionFuncionalidades
        {
            ExtraccionProfunda   = true,
            EscrituraPropiedades = true,
            Etiquetas            = true,
            ComparacionBom       = true,
            ExportacionExcel     = true
        },
        _ => new ConfiguracionFuncionalidades() // Administrador: todo habilitado
    };
}
