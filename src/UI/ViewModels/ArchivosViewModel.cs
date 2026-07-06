using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using SWDataExtractor.Application.Servicios;
using SWDataExtractor.Data;
using SWDataExtractor.Data.Entities;

namespace SWDataExtractor.UI.ViewModels;

public partial class ArchivosViewModel : ObservableObject
{
    private readonly AppDbContext _db;
    private readonly OrquestadorExtraccion _orquestador;
    private readonly EscaneadorCarpetas _escaneador;
    private readonly Servicios.ServicioNotificaciones _notificaciones;

    public FuncionalidadesViewModel Funcionalidades { get; }

    public ObservableCollection<Archivo> ArchivosSeleccionados { get; } = [];

    // Se reemplaza COMPLETA en cada carga (nueva colección + nueva vista) en vez de hacer
    // Clear+Add sobre la misma instancia: el reset sobre la colección en uso dejaba al
    // DataGrid con estado interno de selección/anclas obsoleto, y producía filas "fantasma"
    // seleccionadas (la fila presionada + la de abajo) tras extraer o escanear.
    [ObservableProperty] private ICollectionView? _archivosVista;
    private ObservableCollection<Archivo> _archivos = [];

    [ObservableProperty] private Archivo? _archivoSeleccionado;
    [ObservableProperty] private bool     _haySeleccion;
    [ObservableProperty] private string _filtroTexto = "";
    [ObservableProperty] private string _filtroTipo  = "Todos";
    [ObservableProperty] private string _filtroEstado = "Todos";
    [ObservableProperty] private bool   _agruparPorCarpeta = true;
    [ObservableProperty] private bool   _estaCargando;
    [ObservableProperty] private string _mensajeEstado = "Listo";

    public List<string> OpcionesTipo  { get; } = ["Todos", "pieza", "ensamble", "plano", "step", "otro"];
    public List<string> OpcionesEstado { get; } =
        ["Todos", "pendiente", "ok", "error", "timeout", "version_no_soportada", "bloqueado", "omitido"];

    public ArchivosViewModel(AppDbContext db, OrquestadorExtraccion orquestador,
        EscaneadorCarpetas escaneador, FuncionalidadesViewModel funcionalidades,
        Servicios.ServicioNotificaciones notificaciones)
    {
        _db             = db;
        _orquestador    = orquestador;
        _escaneador     = escaneador;
        _notificaciones = notificaciones;
        Funcionalidades = funcionalidades;

        ArchivosSeleccionados.CollectionChanged += (_, _) =>
            HaySeleccion = ArchivosSeleccionados.Count > 0;
    }

    partial void OnFiltroTextoChanged(string value)  => ArchivosVista?.Refresh();
    partial void OnFiltroTipoChanged(string value)   => ArchivosVista?.Refresh();
    partial void OnFiltroEstadoChanged(string value) => ArchivosVista?.Refresh();
    partial void OnAgruparPorCarpetaChanged(bool value) => AplicarAgrupacion();

    // Agrupación por carpeta contenedora sobre la MISMA vista (misma grilla, filtros y
    // columnas): solo se insertan encabezados de grupo colapsables. El orden por Ruta
    // hace que los grupos salgan alfabéticos en vez de "por orden de aparición".
    private void AplicarAgrupacion()
    {
        if (ArchivosVista is null) return;
        using (ArchivosVista.DeferRefresh())
        {
            ArchivosVista.GroupDescriptions.Clear();
            ArchivosVista.SortDescriptions.Clear();
            if (AgruparPorCarpeta)
            {
                ArchivosVista.GroupDescriptions.Add(new PropertyGroupDescription(
                    nameof(Archivo.Ruta), new Converters.CarpetaDeRutaConverter()));
                ArchivosVista.SortDescriptions.Add(new SortDescription(
                    nameof(Archivo.Ruta), ListSortDirection.Ascending));
            }
        }
    }

    private bool AplicarFiltro(object obj)
    {
        if (obj is not Archivo a) return false;

        if (!string.IsNullOrWhiteSpace(FiltroTexto) &&
            !a.Nombre.Contains(FiltroTexto, StringComparison.OrdinalIgnoreCase) &&
            !a.Ruta.Contains(FiltroTexto, StringComparison.OrdinalIgnoreCase))
            return false;

        if (FiltroTipo  != "Todos" && a.Tipo         != FiltroTipo)  return false;
        if (FiltroEstado != "Todos" && a.EstadoRapido != FiltroEstado) return false;

        return true;
    }

    [RelayCommand]
    private async Task CargarAsync()
    {
        EstaCargando  = true;
        MensajeEstado = "Cargando archivos…";
        try
        {
            int? idSeleccionado = ArchivoSeleccionado?.Id;

            var lista = await _db.Archivos.OrderBy(a => a.Nombre).ToListAsync();

            // Colección y vista NUEVAS por carga (ver comentario en el campo _archivos):
            // reemplazar el ItemsSource resetea por completo el estado interno de selección
            // del DataGrid — cero filas fantasma.
            _archivos = new ObservableCollection<Archivo>(lista);
            var vista = CollectionViewSource.GetDefaultView(_archivos);
            vista.Filter = AplicarFiltro;
            ArchivosVista = vista;
            AplicarAgrupacion();

            ArchivosSeleccionados.Clear();
            ArchivoSeleccionado = idSeleccionado is null
                ? null
                : _archivos.FirstOrDefault(a => a.Id == idSeleccionado);

            // Sincronizar la multi-selección explícitamente: el SelectionChanged del grid
            // puede dispararse antes de que el ItemsSource nuevo esté aplicado (timing de
            // bindings), dejando HaySeleccion=false y el panel de detalle colapsado aunque
            // la fila quede resaltada. El grid re-sincroniza después con el mismo contenido.
            if (ArchivoSeleccionado is not null)
            {
                ArchivosSeleccionados.Add(ArchivoSeleccionado);
                // El DbContext compartido (tracking) devuelve la MISMA instancia al recargar,
                // así que la asignación de arriba no dispara PropertyChanged y el panel de
                // detalle quedaba obsoleto (p. ej. sin las propiedades recién extraídas).
                // Re-notificar fuerza a MainViewModel a recargar el detalle.
                OnPropertyChanged(nameof(ArchivoSeleccionado));
            }

            MensajeEstado = $"{lista.Count} archivos cargados";
        }
        finally { EstaCargando = false; }
    }

    [RelayCommand]
    private async Task EscanearAsync()
    {
        EstaCargando  = true;
        MensajeEstado = "Escaneando carpetas…";
        try
        {
            var r = await _escaneador.EscanearAsync(CancellationToken.None);
            MensajeEstado = $"Escaneo: +{r.Nuevos} nuevos, {r.Actualizados} actualizados";
            await CargarAsync();
        }
        finally { EstaCargando = false; }
    }

    [RelayCommand]
    private async Task ExtraerSeleccionadoAsync()
    {
        if (ArchivoSeleccionado is null) return;
        var archivo = ArchivoSeleccionado;
        EstaCargando  = true;
        MensajeEstado = $"Extrayendo {archivo.Nombre}…";
        try
        {
            await _orquestador.ProcesarUnoAsync(archivo.Id, ModoExtraccion.Auto, CancellationToken.None);
            MensajeEstado = "Extracción completada";
            await CargarAsync();
            OfrecerAbrirSiFaltaSw(archivo);
        }
        finally { EstaCargando = false; }
    }

    // Usa SwApi (COM). Requiere SolidWorks abierto; no necesita clave DocManager.
    [RelayCommand]
    private async Task ExtraerProfundoAsync()
    {
        if (ArchivoSeleccionado is null) return;
        var archivo = ArchivoSeleccionado;
        EstaCargando  = true;
        MensajeEstado = $"Extracción profunda (SW): {archivo.Nombre}…";
        try
        {
            await _orquestador.ProcesarUnoAsync(archivo.Id, ModoExtraccion.Profundo, CancellationToken.None);
            MensajeEstado = "Extracción profunda completada";
            await CargarAsync();
            OfrecerAbrirSiFaltaSw(archivo);
        }
        finally { EstaCargando = false; }
    }

    // Si la extracción falló específicamente porque SolidWorks no está en ejecución (mensaje
    // exacto de ExtractorSwApi.ExtraerAsync), ofrece abrir el archivo con la app asociada
    // (normalmente SolidWorks) para que el usuario pueda reintentar una vez cargue. `archivo`
    // es la misma instancia rastreada por EF que usó OrquestadorExtraccion, así que sus
    // campos ya reflejan el resultado sin necesidad de volver a consultar la BD.
    private void OfrecerAbrirSiFaltaSw(Archivo archivo)
    {
        var mensaje = archivo.MensajeError ?? "";
        bool faltaSw = archivo.EstadoRapido == "error" &&
            mensaje.Contains("SolidWorks no está en ejecución", StringComparison.OrdinalIgnoreCase);
        if (!faltaSw) return;

        var respuesta = MessageBox.Show(
            $"No se pudo extraer \"{archivo.Nombre}\" porque SolidWorks no está abierto.\n\n" +
            "¿Quieres abrirlo ahora con SolidWorks? Cuando termine de cargar podrás reintentar la extracción.",
            "SolidWorks no está abierto", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (respuesta == MessageBoxResult.Yes)
            AbrirArchivo(archivo);
    }

    // Navegación desde otras secciones (ej. doble clic en un reporte del Explorador):
    // asegura la carga y selecciona el archivo, lo que expande el panel de detalle.
    public async Task SeleccionarPorIdAsync(int archivoId)
    {
        if (ArchivosVista is null) await CargarAsync();
        var archivo = _archivos.FirstOrDefault(a => a.Id == archivoId);
        if (archivo is null) return;
        ArchivoSeleccionado = archivo;
        if (!ArchivosSeleccionados.Contains(archivo))
            ArchivosSeleccionados.Add(archivo);
    }

    [RelayCommand]
    private void LimpiarFiltros()
    {
        FiltroTexto  = "";
        FiltroTipo   = "Todos";
        FiltroEstado = "Todos";
    }

    // Abre el archivo con la aplicación asociada en Windows (SolidWorks si está instalado).
    [RelayCommand]
    private void AbrirArchivo(Archivo? archivo)
    {
        archivo ??= ArchivoSeleccionado;
        if (archivo is null) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(archivo.Ruta)
            {
                UseShellExecute = true
            });
            MensajeEstado = $"Abriendo {archivo.Nombre}…";
        }
        catch (Exception ex)
        {
            MensajeEstado = $"No se pudo abrir el archivo: {ex.Message}";
        }
    }

    // Abre el Explorador de Windows con el archivo seleccionado (no requiere que exista un
    // programa asociado, útil si el archivo fue movido/renombrado o el visor falla).
    [RelayCommand]
    private void AbrirCarpetaContenedora(Archivo? archivo)
    {
        archivo ??= ArchivoSeleccionado;
        if (archivo is null) return;
        try
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{archivo.Ruta}\"");
        }
        catch (Exception ex)
        {
            MensajeEstado = $"No se pudo abrir el explorador: {ex.Message}";
        }
    }
}
