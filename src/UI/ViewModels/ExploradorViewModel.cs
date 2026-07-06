using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using SWDataExtractor.Application.Servicios;
using SWDataExtractor.Data;

namespace SWDataExtractor.UI.ViewModels;

// Filas aplanadas para las grillas de reportes (una fila por archivo, con número de grupo).
public record FilaDuplicado(int Grupo, int Id, string Nombre, string Ruta, long? TamanoBytes, string? Fecha);
public record FilaVersion(int Grupo, int Id, string NombreBase, string Nombre, string? Fecha, string Ruta, bool MasReciente);
public record FilaBomProyecto(ItemBom Item, string EstadoRapido, string EstadoProfundo);

// Fase 7a: explorador de carpetas + reportes (duplicados, referencias rotas, posibles
// versiones) + dashboard de proyecto. Todo sobre datos ya extraídos — sin SW ni licencia.
public partial class ExploradorViewModel : ObservableObject
{
    private readonly ServicioAnalisisProyecto _analisis;
    private readonly ServicioBom _bom;
    private readonly AppDbContext _db;
    private readonly EscaneadorCarpetas _escaneador;
    private readonly OrquestadorExtraccion _orquestador;
    private readonly Servicios.ServicioNotificaciones _notificaciones;
    private CancellationTokenSource? _ctsLote;

    public ObservableCollection<NodoCarpeta> Carpetas { get; } = [];
    public ObservableCollection<ArchivoResumen> ArchivosCarpeta { get; } = [];
    public ObservableCollection<FilaDuplicado> Duplicados { get; } = [];
    public ObservableCollection<ReferenciaRota> Rotas { get; } = [];
    public ObservableCollection<FilaVersion> Versiones { get; } = [];
    public ObservableCollection<ArchivoResumen> EnsamblesTop { get; } = [];
    public ObservableCollection<FilaBomProyecto> BomProyecto { get; } = [];
    public ObservableCollection<IncumplimientoPropiedad> Incumplimientos { get; } = [];

    [ObservableProperty] private NodoCarpeta? _carpetaSeleccionada;
    [ObservableProperty] private ArchivoResumen? _ensambleSeleccionado;
    [ObservableProperty] private SaludProyecto? _salud;
    [ObservableProperty] private bool _estaCargando;
    [ObservableProperty] private string _mensajeEstado = "Listo";
    [ObservableProperty] private bool _yaCargado;
    [ObservableProperty] private string? _espacioDesperdiciado;
    [ObservableProperty] private bool _loteEnCurso;
    [ObservableProperty] private int  _progresoActual;
    [ObservableProperty] private int  _progresoTotal;
    // Solo las carpetas RAÍZ de escaneo se pueden quitar; las subcarpetas son estructura de
    // disco (idea futura: "excluir subcarpeta" vía PatronesExcluidos, sin implementar aún).
    [ObservableProperty] private bool _carpetaEsRaiz;

    // Disparado por doble clic en cualquier grilla de reportes: MainViewModel lo escucha y
    // navega a la sección Archivos con ese archivo seleccionado.
    public event Action<int>? VerDetalleSolicitado;

    public void SolicitarVerDetalle(int archivoId) => VerDetalleSolicitado?.Invoke(archivoId);

    public ExploradorViewModel(ServicioAnalisisProyecto analisis, ServicioBom bom, AppDbContext db,
        EscaneadorCarpetas escaneador, OrquestadorExtraccion orquestador,
        Servicios.ServicioNotificaciones notificaciones)
    {
        _analisis       = analisis;
        _bom            = bom;
        _db             = db;
        _escaneador     = escaneador;
        _orquestador    = orquestador;
        _notificaciones = notificaciones;
    }

    partial void OnCarpetaSeleccionadaChanged(NodoCarpeta? value)
    {
        if (value is not null)
        {
            _ = CargarArchivosCarpetaAsync(value.Ruta);
            _ = ActualizarEsRaizAsync(value.Ruta);
        }
        else
        {
            CarpetaEsRaiz = false;
        }
    }

    private async Task ActualizarEsRaizAsync(string ruta)
    {
        var raices = await _escaneador.ObtenerCarpetasGuardadasAsync(CancellationToken.None);
        var normalizada = ruta.TrimEnd(System.IO.Path.DirectorySeparatorChar);
        CarpetaEsRaiz = raices.Any(r =>
            string.Equals(r.TrimEnd(System.IO.Path.DirectorySeparatorChar), normalizada,
                StringComparison.OrdinalIgnoreCase));
    }

    [RelayCommand]
    public async Task ActualizarAsync()
    {
        EstaCargando  = true;
        MensajeEstado = "Analizando proyectos…";
        try
        {
            var arbol     = await _analisis.ObtenerArbolCarpetasAsync();
            var duplicados = await _analisis.ObtenerDuplicadosAsync();
            var rotas     = await _analisis.ObtenerReferenciasRotasAsync();
            var versiones = await _analisis.ObtenerPosiblesVersionesAsync();
            var ensambles = await _analisis.ObtenerEnsamblesTopLevelAsync();
            var incumplimientos = await _analisis.ObtenerIncumplimientosAsync();

            Carpetas.Clear();
            foreach (var n in arbol) Carpetas.Add(n);

            Duplicados.Clear();
            long desperdiciado = 0;
            foreach (var g in duplicados)
            {
                // Espacio recuperable: todas las copias menos una (la que se conservaría).
                desperdiciado += (g.Archivos.Count - 1) * (g.Archivos[0].TamanoBytes ?? 0);
                foreach (var a in g.Archivos)
                    Duplicados.Add(new FilaDuplicado(g.NumeroGrupo, a.Id, a.Nombre, a.Ruta, a.TamanoBytes, a.FechaModDisco));
            }
            EspacioDesperdiciado = duplicados.Count == 0 ? null
                : $"Espacio recuperable si se conserva una sola copia por grupo: {FormatearTamano(desperdiciado)}";

            Rotas.Clear();
            foreach (var r in rotas) Rotas.Add(r);

            Versiones.Clear();
            foreach (var g in versiones)
                for (int i = 0; i < g.Versiones.Count; i++)
                {
                    var v = g.Versiones[i];
                    Versiones.Add(new FilaVersion(g.NumeroGrupo, v.Id, g.NombreBase, v.Nombre, v.FechaModDisco, v.Ruta, i == 0));
                }

            EnsamblesTop.Clear();
            foreach (var e in ensambles) EnsamblesTop.Add(e);

            Incumplimientos.Clear();
            foreach (var i in incumplimientos) Incumplimientos.Add(i);

            YaCargado = true;
            MensajeEstado =
                $"{duplicados.Count} grupo(s) de duplicados · {rotas.Count} referencia(s) rota(s) · " +
                $"{versiones.Count} grupo(s) de posibles versiones · {incumplimientos.Count} incumplimiento(s) · " +
                $"{ensambles.Count} ensamble(s) top-level";
        }
        finally { EstaCargando = false; }
    }

    private async Task CargarArchivosCarpetaAsync(string carpeta)
    {
        var archivos = await _analisis.ObtenerArchivosDeCarpetaAsync(carpeta);
        ArchivosCarpeta.Clear();
        foreach (var a in archivos) ArchivosCarpeta.Add(a);
    }

    // Dashboard: BOM del ensamble elegido + estado de extracción de cada componente + salud.
    [RelayCommand]
    private async Task AnalizarProyectoAsync()
    {
        if (EnsambleSeleccionado is null) return;
        EstaCargando  = true;
        MensajeEstado = $"Analizando {EnsambleSeleccionado.Nombre}…";
        try
        {
            var bom = await _bom.ObtenerBomIndentadoAsync(EnsambleSeleccionado.Id);
            Salud   = await _analisis.CalcularSaludAsync(bom);

            var ids = bom.Where(i => i.ArchivoId is not null).Select(i => i.ArchivoId!.Value).Distinct().ToList();
            var estados = await _db.Archivos
                .Where(a => ids.Contains(a.Id))
                .Select(a => new { a.Id, a.EstadoRapido, a.EstadoProfundo })
                .ToDictionaryAsync(a => a.Id, a => (a.EstadoRapido, a.EstadoProfundo));

            BomProyecto.Clear();
            foreach (var item in bom)
            {
                var (rapido, profundo) = item.ArchivoId is not null && estados.TryGetValue(item.ArchivoId.Value, out var e)
                    ? e
                    : (item.EsToolbox ? ("toolbox", "toolbox") : ("no_indexado", "no_indexado"));
                BomProyecto.Add(new FilaBomProyecto(item, rapido, profundo));
            }

            MensajeEstado = $"BOM de {EnsambleSeleccionado.Nombre}: {bom.Count - 1} componente(s)";
        }
        finally { EstaCargando = false; }
    }

    // ── Gestión de carpetas raíz (movida aquí desde Configuración por pedido del usuario;
    //    la lógica sigue viviendo en EscaneadorCarpetas, esta capa solo recolecta input) ──

    [RelayCommand]
    private async Task AgregarCarpetasAsync()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Seleccionar carpeta(s) raíz de escaneo",
            Multiselect = true
        };
        if (dlg.ShowDialog() != true) return;

        var actuales = await _escaneador.ObtenerCarpetasGuardadasAsync(CancellationToken.None);
        var nuevas = dlg.FolderNames
            .Where(f => !actuales.Any(a => string.Equals(a, f, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (nuevas.Count == 0) return;

        await _escaneador.GuardarCarpetasAsync([.. actuales, .. nuevas], CancellationToken.None);

        EstaCargando  = true;
        MensajeEstado = "Escaneando carpetas nuevas…";
        try
        {
            var r = await _escaneador.EscanearAsync(CancellationToken.None);
            _notificaciones.Exito($"{nuevas.Count} carpeta(s) agregada(s): +{r.Nuevos} archivo(s) indexado(s).");
        }
        finally { EstaCargando = false; }

        await ActualizarAsync();
    }

    [RelayCommand]
    private async Task QuitarCarpetaAsync()
    {
        if (CarpetaSeleccionada is null || !CarpetaEsRaiz) return;
        var carpeta = CarpetaSeleccionada.Ruta;

        var confirmar = System.Windows.MessageBox.Show(
            $"Quitar la carpeta de escaneo:\n{carpeta}\n\n" +
            "¿Eliminar también de la base de datos los archivos indexados dentro de ella " +
            "(propiedades, features, BOM, historial)?\n\n" +
            "Sí = quitar carpeta y borrar sus datos.\nNo = quitar carpeta, conservar datos (quedan como omitidos).\nCancelar = no hacer nada.",
            "Quitar carpeta", System.Windows.MessageBoxButton.YesNoCancel,
            System.Windows.MessageBoxImage.Question);
        if (confirmar == System.Windows.MessageBoxResult.Cancel) return;

        var actuales = await _escaneador.ObtenerCarpetasGuardadasAsync(CancellationToken.None);
        actuales.RemoveAll(a => string.Equals(
            a.TrimEnd(System.IO.Path.DirectorySeparatorChar),
            carpeta.TrimEnd(System.IO.Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase));
        await _escaneador.GuardarCarpetasAsync(actuales, CancellationToken.None);

        int borrados = 0;
        if (confirmar == System.Windows.MessageBoxResult.Yes)
            borrados = await _escaneador.BorrarArchivosBajoCarpetasAsync([carpeta], CancellationToken.None);

        EstaCargando  = true;
        MensajeEstado = "Re-escaneando…";
        try
        {
            await _escaneador.EscanearAsync(CancellationToken.None);
            _notificaciones.Exito(borrados > 0
                ? $"Carpeta quitada; {borrados} archivo(s) eliminados de la BD."
                : "Carpeta quitada (sus datos se conservan como omitidos).");
        }
        finally { EstaCargando = false; }

        ArchivosCarpeta.Clear();
        CarpetaSeleccionada = null;
        await ActualizarAsync();
    }

    [RelayCommand]
    private async Task PurgarOmitidosAsync()
    {
        var confirmar = System.Windows.MessageBox.Show(
            "Se eliminarán de la base de datos todos los archivos marcados como \"omitido\" " +
            "(ya no encontrados en disco) y sus datos asociados. Esta acción no se puede deshacer.",
            "Purgar omitidos", System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        if (confirmar != System.Windows.MessageBoxResult.Yes) return;

        var borrados = await _escaneador.PurgarOmitidosAsync(CancellationToken.None);
        _notificaciones.Exito(borrados == 0
            ? "No había archivos omitidos que purgar."
            : $"{borrados} archivo(s) omitido(s) eliminados de la BD.");
        await ActualizarAsync();
    }

    // ── Extracción por lote (secuencial abrir→extraer→cerrar: la API COM de SW es STA,
    //    paralelizar contra una instancia no acelera y abrir todo junto la desestabiliza) ──

    [RelayCommand]
    private Task ExtraerCarpetaAsync() =>
        CarpetaSeleccionada is null ? Task.CompletedTask : EjecutarLoteAsync(CarpetaSeleccionada.Ruta);

    [RelayCommand]
    private Task ExtraerTodosAsync() => EjecutarLoteAsync(null);

    [RelayCommand]
    private void CancelarLote() => _ctsLote?.Cancel();

    private async Task EjecutarLoteAsync(string? carpeta)
    {
        if (LoteEnCurso) return;
        LoteEnCurso  = true;
        EstaCargando = true;
        _ctsLote     = new CancellationTokenSource();
        try
        {
            var progreso = new Progress<ProgresoLote>(p =>
            {
                ProgresoActual = p.Actual;
                ProgresoTotal  = p.Total;
                MensajeEstado  = $"Extrayendo {p.Actual} de {p.Total}: {p.Archivo}";
            });

            var resumen = await _orquestador.ProcesarPendientesAsync(
                ModoExtraccion.Auto, _ctsLote.Token, progreso, carpeta);

            var alcance = carpeta is null ? "todos los pendientes" : System.IO.Path.GetFileName(carpeta.TrimEnd(System.IO.Path.DirectorySeparatorChar));
            MensajeEstado = resumen.Cancelado
                ? $"Lote cancelado ({alcance}): {resumen.Ok} ok, {resumen.Errores} con error de {resumen.Total}"
                : $"Lote completado ({alcance}): {resumen.Ok} ok, {resumen.Errores} con error de {resumen.Total}";

            if (resumen.Cancelado)      _notificaciones.Error(MensajeEstado, "Extracción cancelada");
            else if (resumen.Errores > 0) _notificaciones.Error(MensajeEstado, "Lote con errores");
            else                          _notificaciones.Exito(MensajeEstado, "Extracción completada");

            // Refrescar reportes y la carpeta visible con los estados nuevos
            await ActualizarAsync();
            if (CarpetaSeleccionada is not null)
                await CargarArchivosCarpetaAsync(CarpetaSeleccionada.Ruta);
        }
        finally
        {
            LoteEnCurso    = false;
            EstaCargando   = false;
            ProgresoActual = 0;
            ProgresoTotal  = 0;
            _ctsLote?.Dispose();
            _ctsLote = null;
        }
    }

    // Exporta los tres reportes (duplicados, rotas, versiones) a un Excel de varias hojas.
    [RelayCommand]
    private void ExportarReportes()
    {
        if (!YaCargado) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter   = "Excel|*.xlsx",
            FileName = $"Reportes_Proyectos_{DateTime.Now:yyyyMMdd}.xlsx"
        };
        if (dlg.ShowDialog() != true) return;
        Servicios.ExportadorExcel.ExportarReportesProyecto(
            Duplicados.ToList(), Rotas.ToList(), Versiones.ToList(), Incumplimientos.ToList(), dlg.FileName);
        MensajeEstado = $"Reportes exportados a {dlg.FileName}";
    }

    private static string FormatearTamano(long bytes) => bytes switch
    {
        >= 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):N1} GB",
        >= 1024 * 1024        => $"{bytes / (1024.0 * 1024):N1} MB",
        >= 1024               => $"{bytes / 1024.0:N0} KB",
        _                     => $"{bytes} B"
    };

    [RelayCommand]
    private void AbrirArchivo(string? ruta)
    {
        if (string.IsNullOrEmpty(ruta)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ruta) { UseShellExecute = true });
        }
        catch (Exception ex) { MensajeEstado = $"No se pudo abrir: {ex.Message}"; }
    }

    [RelayCommand]
    private void AbrirCarpetaContenedora(string? ruta)
    {
        if (string.IsNullOrEmpty(ruta)) return;
        try
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{ruta}\"");
        }
        catch (Exception ex) { MensajeEstado = $"No se pudo abrir el explorador: {ex.Message}"; }
    }
}
