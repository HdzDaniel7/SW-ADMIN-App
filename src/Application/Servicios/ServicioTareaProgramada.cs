using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace SWDataExtractor.Application.Servicios;

public record EstadoTarea(bool Existe, string? Estado, string? ProximaEjecucion, string? UltimaEjecucion);

/// <summary>
/// Gestiona una tarea programada de Windows para escaneo periódico del Batch.
/// Requiere que la aplicación se ejecute como Administrador para crear/eliminar tareas.
/// </summary>
public class ServicioTareaProgramada(ILogger<ServicioTareaProgramada> logger)
{
    private const string NombreTarea = "SWDataExtractor.Escaneo";

    public async Task<EstadoTarea> ObtenerEstadoAsync()
    {
        var salida = await EjecutarSchtasksAsync(
            $"/Query /TN \"{NombreTarea}\" /FO LIST /NH");

        if (salida.CodigoSalida != 0)
            return new EstadoTarea(false, null, null, null);

        // Parsear salida LIST: "Estado:   Listo"  "Próxima ejecución:  ..."  "Última ejecución: ..."
        var lineas = salida.Salida.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        string? estado    = ExtraerValor(lineas, "Estado:");
        string? proxima   = ExtraerValor(lineas, "Hora pr");
        string? ultima    = ExtraerValor(lineas, "Última");

        return new EstadoTarea(true, estado, proxima, ultima);
    }

    /// <param name="rutaExe">Ruta completa al ejecutable SWDataExtractor.Batch.exe</param>
    /// <param name="horaInicio">Hora de inicio diaria, formato HH:MM (ej: "07:00")</param>
    public async Task RegistrarAsync(string rutaExe, string horaInicio = "07:00")
    {
        var args = $"/Create /SC DAILY /TN \"{NombreTarea}\" /TR \"\\\"{rutaExe}\\\"\" " +
                   $"/ST {horaInicio} /RL HIGHEST /F";

        var salida = await EjecutarSchtasksAsync(args);
        if (salida.CodigoSalida != 0)
        {
            logger.LogError("Error al registrar tarea: {Error}", salida.Error);
            throw new InvalidOperationException(
                $"No se pudo registrar la tarea. ¿La aplicación tiene permisos de administrador?\n{salida.Error}");
        }
        logger.LogInformation("Tarea programada registrada: {Nombre} a las {Hora}", NombreTarea, horaInicio);
    }

    public async Task EliminarAsync()
    {
        var salida = await EjecutarSchtasksAsync($"/Delete /TN \"{NombreTarea}\" /F");
        if (salida.CodigoSalida != 0)
        {
            logger.LogError("Error al eliminar tarea: {Error}", salida.Error);
            throw new InvalidOperationException(
                $"No se pudo eliminar la tarea.\n{salida.Error}");
        }
        logger.LogInformation("Tarea programada eliminada: {Nombre}", NombreTarea);
    }

    public async Task EjecutarAhoraAsync()
    {
        var salida = await EjecutarSchtasksAsync($"/Run /TN \"{NombreTarea}\"");
        if (salida.CodigoSalida != 0)
            throw new InvalidOperationException($"No se pudo ejecutar la tarea.\n{salida.Error}");
    }

    private static async Task<(int CodigoSalida, string Salida, string Error)> EjecutarSchtasksAsync(string args)
    {
        var psi = new ProcessStartInfo("schtasks.exe", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        };
        using var proceso = Process.Start(psi)!;
        var salida = await proceso.StandardOutput.ReadToEndAsync();
        var error  = await proceso.StandardError.ReadToEndAsync();
        await proceso.WaitForExitAsync();
        return (proceso.ExitCode, salida, error);
    }

    private static string? ExtraerValor(string[] lineas, string prefijo) =>
        lineas.Select(l => l.Trim())
              .FirstOrDefault(l => l.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase))
              ?.Split(':', 2).ElementAtOrDefault(1)
              ?.Trim();
}
