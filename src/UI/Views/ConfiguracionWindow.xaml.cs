using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Win32;
using SWDataExtractor.Application.Config;
using SWDataExtractor.Application.Servicios;
using SWDataExtractor.Data;
using SWDataExtractor.UI.Servicios;
using SWDataExtractor.UI.ViewModels;

namespace SWDataExtractor.UI.Views;

public partial class ConfiguracionWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly ServicioTareaProgramada _tarea;
    private readonly ConfiguracionExtraccion _cfg;
    private readonly ServicioRoles           _roles;
    private readonly FuncionalidadesViewModel _funcVm;
    private readonly AppDbContext            _db;
    private readonly EscaneadorCarpetas      _escaneador;
    private readonly ServicioLicencias       _licencias;
    private readonly ServicioNotificaciones  _notificaciones;

    public ConfiguracionWindow(
        ServicioTareaProgramada tarea,
        IOptions<ConfiguracionExtraccion> opciones,
        ServicioRoles roles,
        FuncionalidadesViewModel funcVm,
        AppDbContext db,
        EscaneadorCarpetas escaneador,
        ServicioLicencias licencias,
        ServicioNotificaciones notificaciones)
    {
        InitializeComponent();
        _tarea          = tarea;
        _cfg            = opciones.Value;
        _roles          = roles;
        _funcVm         = funcVm;
        _db             = db;
        _escaneador     = escaneador;
        _licencias      = licencias;
        _notificaciones = notificaciones;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Pestaña 1: carpetas — leer desde BD, fallback a appsettings.json
        var carpetas = await _escaneador.ObtenerCarpetasGuardadasAsync(CancellationToken.None);
        if (!carpetas.Any()) carpetas = _cfg.CarpetasRaiz.ToList();
        foreach (var c in carpetas) ListaCarpetas.Items.Add(c);

        TxtExtensiones.Text  = string.Join("  ", _cfg.ExtensionesIncluidas);
        TxtPatrones.Text     = _cfg.PatronesExcluidos.Any()
                                   ? string.Join("  ", _cfg.PatronesExcluidos)
                                   : "(ninguno)";

        var ruta = System.IO.Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        TxtCadenaConexion.Text = System.IO.File.Exists(ruta)
            ? $"appsettings.json en: {ruta}"
            : "appsettings.json no encontrado";

        // Pestaña Licencias: clave DocManager guardada (si hay alguna)
        var claveGuardada = await _licencias.ObtenerClaveDocManagerAsync();
        TxtLicenciaEstado.Text = string.IsNullOrEmpty(claveGuardada)
            ? "Sin clave guardada — el modo Rápido usará SwApi (requiere SolidWorks abierto)."
            : $"Clave guardada (termina en …{claveGuardada[^Math.Min(4, claveGuardada.Length)..]}).";

        // Pestaña Roles: cargar opciones y rol actual
        CboRol.ItemsSource   = Enum.GetValues<Rol>();
        CboRol.SelectedItem  = _funcVm.RolActual;
        CboRol.SelectionChanged += (_, _) => ActualizarDescripcionPermisos();
        ActualizarDescripcionPermisos();

        // Pestaña Tarea programada: estado inicial
        await RefrescarEstadoTareaAsync();

        TxtRutaExe.Text = System.IO.Path.Combine(
            AppContext.BaseDirectory, "SWDataExtractor.Batch.exe");
    }

    private void ActualizarDescripcionPermisos()
    {
        if (CboRol.SelectedItem is not Rol rol) return;
        var cfg = ServicioRoles.FuncionalidadesParaRol(rol);
        TxtPermisos.Text = $"Extracción profunda: {Si(cfg.ExtraccionProfunda)}  " +
                           $"Escritura propiedades: {Si(cfg.EscrituraPropiedades)}  " +
                           $"Etiquetas: {Si(cfg.Etiquetas)}  " +
                           $"Comparación BOM: {Si(cfg.ComparacionBom)}  " +
                           $"Exportar Excel: {Si(cfg.ExportacionExcel)}";
    }

    private static string Si(bool v) => v ? "Sí" : "No";

    private async void AplicarRol_Click(object sender, RoutedEventArgs e)
    {
        if (CboRol.SelectedItem is not Rol rol) return;
        try
        {
            await _roles.CambiarRolAsync(rol);
            _funcVm.Aplicar(rol, ServicioRoles.FuncionalidadesParaRol(rol));
            TxtRolResultado.Text = $"Rol aplicado: {rol}";
        }
        catch (Exception ex)
        {
            TxtRolResultado.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E5484D")!);
            TxtRolResultado.Text = $"Error: {ex.Message}";
        }
    }

    private async void ActualizarEstadoTarea_Click(object sender, RoutedEventArgs e) =>
        await RefrescarEstadoTareaAsync();

    private async Task RefrescarEstadoTareaAsync()
    {
        try
        {
            var estado = await _tarea.ObtenerEstadoAsync();
            TxtEstadoTarea.Text = estado.Existe ? estado.Estado ?? "Registrada" : "No registrada";
            TxtProxima.Text     = estado.ProximaEjecucion ?? "—";
            TxtUltima.Text      = estado.UltimaEjecucion  ?? "—";
        }
        catch (Exception ex)
        {
            TxtEstadoTarea.Text = $"Error: {ex.Message}";
        }
    }

    private void BuscarExe_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter      = "Ejecutable|*.exe",
            FileName    = "SWDataExtractor.Batch.exe",
            Title       = "Seleccionar SWDataExtractor.Batch.exe"
        };
        if (dlg.ShowDialog(this) == true)
            TxtRutaExe.Text = dlg.FileName;
    }

    private async void RegistrarTarea_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtRutaExe.Text))
        {
            MessageBox.Show("Ingrese la ruta al ejecutable.", "Validación",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            await _tarea.RegistrarAsync(TxtRutaExe.Text, TxtHoraInicio.Text);
            _notificaciones.Exito("Tarea registrada correctamente.");
            await RefrescarEstadoTareaAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void EliminarTarea_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("¿Eliminar la tarea programada?", "Confirmar",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            await _tarea.EliminarAsync();
            _notificaciones.Exito("Tarea eliminada.");
            await RefrescarEstadoTareaAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void EjecutarAhora_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _tarea.EjecutarAhoraAsync();
            _notificaciones.Exito("Tarea iniciada.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Gestión de carpetas ───────────────────────────────────────────────────
    // La lectura/escritura/purga vive en EscaneadorCarpetas (capa Application);
    // esta ventana solo recolecta la entrada del usuario y muestra confirmaciones.

    private void AgregarCarpeta_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Seleccionar carpeta raíz de escaneo",
            Multiselect = true
        };
        if (dlg.ShowDialog(this) != true) return;
        foreach (var folder in dlg.FolderNames)
        {
            if (!ListaCarpetas.Items.Contains(folder))
                ListaCarpetas.Items.Add(folder);
        }
        TxtCarpetasMsg.Text = "";
    }

    private void QuitarCarpeta_Click(object sender, RoutedEventArgs e)
    {
        if (ListaCarpetas.SelectedItem is string sel)
        {
            ListaCarpetas.Items.Remove(sel);
            TxtCarpetasMsg.Text = "";
        }
    }

    private async void GuardarCarpetas_Click(object sender, RoutedEventArgs e)
    {
        var carpetasAnteriores = await _escaneador.ObtenerCarpetasGuardadasAsync(CancellationToken.None);
        var lista = ListaCarpetas.Items.Cast<string>().ToList();

        await _escaneador.GuardarCarpetasAsync(lista, CancellationToken.None);

        // Carpetas quitadas de la lista: ofrecer borrar de la BD los archivos indexados
        // que estaban debajo de ellas, para que la BD no acumule registros huérfanos.
        var quitadas = carpetasAnteriores
            .Where(anterior => !lista.Any(actual => string.Equals(actual, anterior, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        int borrados = 0;
        if (quitadas.Count > 0)
        {
            var confirmar = MessageBox.Show(
                $"Se quitaron {quitadas.Count} carpeta(s) de la lista de escaneo:\n" +
                string.Join("\n", quitadas) +
                "\n\n¿Eliminar de la base de datos los archivos indexados que están dentro de esas carpetas " +
                "(propiedades, features, BOM, historial, etc.)? Esta acción no se puede deshacer.",
                "Carpetas quitadas", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirmar == MessageBoxResult.Yes)
                borrados = await _escaneador.BorrarArchivosBajoCarpetasAsync(quitadas, CancellationToken.None);
        }

        // Re-escanear de inmediato: refleja carpetas agregadas (nuevos pendientes) y
        // marca "omitido" lo que haya quedado bajo carpetas quitadas sin purgar, sin
        // tener que tocar ningún archivo de configuración ni reiniciar la aplicación.
        TxtCarpetasMsg.Text = "Escaneando…";
        var r = await _escaneador.EscanearAsync(CancellationToken.None);
        TxtCarpetasMsg.Text =
            $"✔ Guardadas {lista.Count} carpeta(s). Escaneo: +{r.Nuevos} nuevos, {r.Actualizados} actualizados, " +
            $"{r.Eliminados} marcados omitidos" +
            (borrados > 0 ? $", {borrados} eliminados de la BD." : ".");
    }

    private async void PurgarOmitidos_Click(object sender, RoutedEventArgs e)
    {
        var pendientes = await _db.Archivos.CountAsync(a => a.EstadoRapido == "omitido");
        if (pendientes == 0)
        {
            MessageBox.Show("No hay archivos marcados como omitidos.", "Purgar omitidos",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirmar = MessageBox.Show(
            $"Se eliminarán {pendientes} archivo(s) marcados como \"omitido\" (ya no encontrados en disco) " +
            "y todos sus datos asociados (propiedades, features, BOM, historial). Esta acción no se puede deshacer.",
            "Purgar omitidos", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirmar != MessageBoxResult.Yes) return;

        var borrados = await _escaneador.PurgarOmitidosAsync(CancellationToken.None);
        TxtCarpetasMsg.Text = $"✔ {borrados} archivo(s) omitido(s) eliminados de la BD.";
    }

    // ── Licencias ──────────────────────────────────────────────────────────────

    private async void GuardarLicencia_Click(object sender, RoutedEventArgs e)
    {
        var clave = TxtLicenciaClave.Password;
        if (string.IsNullOrWhiteSpace(clave))
        {
            MessageBox.Show("Ingrese una clave antes de guardar (o use \"Quitar clave\" para borrarla).",
                "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        await _licencias.GuardarClaveDocManagerAsync(clave);
        TxtLicenciaClave.Password = "";
        TxtLicenciaEstado.Text = $"✔ Clave guardada (termina en …{clave[^Math.Min(4, clave.Length)..]}). " +
                                  "Reinicia la aplicación para aplicarla.";
    }

    private async void QuitarLicencia_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("¿Quitar la clave DocManager guardada?", "Confirmar",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        await _licencias.GuardarClaveDocManagerAsync(null);
        TxtLicenciaClave.Password = "";
        TxtLicenciaEstado.Text = "Sin clave guardada — el modo Rápido usará SwApi (requiere SolidWorks abierto). " +
                                  "Reinicia la aplicación para aplicarlo.";
    }

    private void Cerrar_Click(object sender, RoutedEventArgs e) => Close();
}
