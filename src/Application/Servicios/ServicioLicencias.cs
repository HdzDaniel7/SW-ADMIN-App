using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SWDataExtractor.Data;
using SWDataExtractor.Data.Entities;

namespace SWDataExtractor.Application.Servicios;

// Guarda/lee claves de licencia (hoy solo DocManager) en ajustes_app, con el mismo patrón
// "BD primero, config de fallback" que EscaneadorCarpetas usa para las carpetas de escaneo.
// Reemplaza la necesidad de `dotnet user-secrets set` por terminal para configurar la clave.
// El valor se cifra con DPAPI (ligado al usuario de Windows de esta máquina) antes de guardarse.
// DPAPI es exclusivo de Windows; único SO que soporta esta app (WPF + COM SolidWorks).
[SupportedOSPlatform("windows")]
public class ServicioLicencias(AppDbContext db, ILogger<ServicioLicencias> logger)
{
    public const string ClaveDocManager = "DocManagerLicenciaKey";

    public async Task<string?> ObtenerClaveDocManagerAsync(CancellationToken ct = default)
    {
        var ajuste = await db.AjustesApp.FirstOrDefaultAsync(a => a.Clave == ClaveDocManager, ct);
        if (string.IsNullOrWhiteSpace(ajuste?.Valor)) return null;
        return Desencriptar(ajuste.Valor);
    }

    public async Task GuardarClaveDocManagerAsync(string? clave, CancellationToken ct = default)
    {
        var valorGuardado = string.IsNullOrEmpty(clave) ? "" : Encriptar(clave);

        var ajuste = await db.AjustesApp.FirstOrDefaultAsync(a => a.Clave == ClaveDocManager, ct);
        if (ajuste is null)
        {
            db.AjustesApp.Add(new AjusteApp
            {
                Clave       = ClaveDocManager,
                Valor       = valorGuardado,
                Descripcion = "Clave SwDmLicenseKey de SolidWorks Document Manager (cifrada con DPAPI), configurada desde la UI"
            });
        }
        else
        {
            ajuste.Valor = valorGuardado;
        }
        await db.SaveChangesAsync(ct);
    }

    private string Encriptar(string texto)
    {
        var bytes = Encoding.UTF8.GetBytes(texto);
        var cifrado = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(cifrado);
    }

    // Tolera valores guardados antes de introducir el cifrado (texto plano) o generados en
    // otra máquina/usuario (DPAPI falla al descifrar): en ambos casos, en vez de perder la
    // clave, se trata el valor guardado como texto plano — mejor un fallback silencioso que
    // bloquear la extracción Rápida por un cambio de formato interno.
    private string Desencriptar(string valorGuardado)
    {
        try
        {
            var cifrado = Convert.FromBase64String(valorGuardado);
            var bytes   = DesencriptarDpapi(cifrado);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "No se pudo descifrar la clave DocManager guardada; se usa como texto plano");
            return valorGuardado;
        }
    }

    private static byte[] DesencriptarDpapi(byte[] cifrado) =>
        ProtectedData.Unprotect(cifrado, null, DataProtectionScope.CurrentUser);
}
