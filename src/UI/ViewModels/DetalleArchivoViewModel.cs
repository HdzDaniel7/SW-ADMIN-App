using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using SWDataExtractor.Application.Servicios;
using SWDataExtractor.Data;
using SWDataExtractor.Data.Entities;

namespace SWDataExtractor.UI.ViewModels;

// Filas con origen para el modo "incluir componentes del BOM": Componente = nombre del
// archivo del que proviene la fila (null/vacío para el archivo propio del detalle).
public record FilaConfigDetalle(string? Componente, Configuracion C);
public record FilaRoscaDetalle(string? Componente, Rosca R);
public record FilaFeatureDetalle(string? Componente, Feature F);
public record FilaFisicaDetalle(string? Componente, PropiedadFisica F);

public partial class DetalleArchivoViewModel : ObservableObject
{
    private readonly AppDbContext _db;
    private readonly ServicioBom _servicioBom;
    private readonly ServicioPropiedades _servicioPropiedades;

    [ObservableProperty] private Archivo? _archivo;
    [ObservableProperty] private System.Windows.Media.ImageSource? _preview;
    [ObservableProperty] private string _tabActiva = "Propiedades";
    [ObservableProperty] private bool _esEnsamble;

    // Toggle "incluir componentes del BOM": las pestañas Propiedades/Configuraciones/
    // Features/Físicas/Roscas muestran también los datos de TODAS las piezas del BOM,
    // con la columna Componente indicando el origen. Persiste entre selecciones (sticky).
    [ObservableProperty] private bool _incluirComponentesBom;

    public ObservableCollection<PropiedadVista> Propiedades { get; } = [];
    public ObservableCollection<FilaConfigDetalle> Configuraciones { get; } = [];
    public ObservableCollection<ItemBomSeleccionable> Bom { get; } = [];
    public ObservableCollection<ItemWhereUsed> WhereUsed { get; } = [];
    public ObservableCollection<FilaRoscaDetalle> Roscas { get; } = [];
    public ObservableCollection<FilaFeatureDetalle> Features { get; } = [];
    public ObservableCollection<FilaFisicaDetalle> PropsFisicas { get; } = [];

    public DetalleArchivoViewModel(
        AppDbContext db, ServicioBom bom, ServicioPropiedades props)
    {
        _db                  = db;
        _servicioBom         = bom;
        _servicioPropiedades = props;
    }

    partial void OnIncluirComponentesBomChanged(bool value)
    {
        if (Archivo is not null)
            _ = CargarArchivoAsync(Archivo);
    }

    public async Task CargarArchivoAsync(Archivo archivo)
    {
        Archivo    = archivo;
        EsEnsamble = archivo.Tipo == "ensamble";

        // Preview: primero el PNG extraído por DocManager (si hay licencia); si no, la
        // miniatura del shell de Windows (funciona sin licencia si SW está instalado).
        Preview = null;
        if (!string.IsNullOrEmpty(archivo.RutaPreview) && File.Exists(archivo.RutaPreview))
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.UriSource   = new Uri(archivo.RutaPreview, UriKind.Absolute);
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.EndInit();
            Preview = img;
        }
        Preview ??= Servicios.ServicioMiniaturas.ObtenerMiniatura(archivo.Ruta);

        // BOM primero (solo ensambles): además de su pestaña, da los ids/nombres de los
        // componentes para el modo agregado.
        Bom.Clear();
        var nombrePorId = new Dictionary<int, string>();
        if (EsEnsamble)
        {
            var bom = await _servicioBom.ObtenerBomIndentadoAsync(archivo.Id);
            foreach (var item in bom) Bom.Add(new ItemBomSeleccionable(item));

            if (IncluirComponentesBom)
                foreach (var item in bom.Where(i => i.Nivel > 0 && i.ArchivoId is not null))
                    nombrePorId.TryAdd(item.ArchivoId!.Value, item.Nombre);
        }

        // STEP: BOM interno extraído del propio archivo (datos_extra_json, clave "bom_step")
        // — componentes que viven DENTRO del STEP, no archivos referenciados.
        if (!EsEnsamble && archivo.Tipo == "step" && !string.IsNullOrEmpty(archivo.DatosExtraJson))
            CargarBomStep(archivo);

        // ids a consultar: el archivo propio + (en modo agregado) sus componentes.
        var ids = new List<int> { archivo.Id };
        ids.AddRange(nombrePorId.Keys);
        string? Origen(int id) => id == archivo.Id ? null : nombrePorId.GetValueOrDefault(id);

        // Configuraciones
        var cfgs = await _db.Configuraciones
            .Where(c => ids.Contains(c.ArchivoId))
            .OrderBy(c => c.ArchivoId).ThenBy(c => c.Nombre)
            .ToListAsync();
        Configuraciones.Clear();
        foreach (var c in cfgs) Configuraciones.Add(new FilaConfigDetalle(Origen(c.ArchivoId), c));

        // Propiedades (por archivo, para etiquetar el origen)
        Propiedades.Clear();
        foreach (var id in ids)
        {
            var origen = Origen(id);
            foreach (var p in await _servicioPropiedades.LeerPropiedadesAsync(id))
                Propiedades.Add(p with { Componente = origen });
        }

        // Where-used (solo del archivo propio; el agregado no aplica aquí)
        var wu = await _servicioBom.ObtenerWhereUsedAsync(archivo.Id);
        WhereUsed.Clear();
        foreach (var item in wu) WhereUsed.Add(item);

        // Roscas
        var roscas = await _db.Roscas
            .Where(r => ids.Contains(r.ArchivoId))
            .OrderBy(r => r.ArchivoId).ThenBy(r => r.Designacion)
            .ToListAsync();
        Roscas.Clear();
        foreach (var r in roscas) Roscas.Add(new FilaRoscaDetalle(Origen(r.ArchivoId), r));

        // Features
        var features = await _db.Features
            .Where(f => ids.Contains(f.ArchivoId))
            .OrderBy(f => f.ArchivoId).ThenBy(f => f.Orden)
            .ToListAsync();
        Features.Clear();
        foreach (var f in features) Features.Add(new FilaFeatureDetalle(Origen(f.ArchivoId), f));

        // Propiedades físicas
        var fisicas = await _db.PropiedadesFisicas
            .Where(pf => ids.Contains(pf.ArchivoId))
            .OrderBy(pf => pf.ArchivoId)
            .ToListAsync();
        PropsFisicas.Clear();
        foreach (var pf in fisicas) PropsFisicas.Add(new FilaFisicaDetalle(Origen(pf.ArchivoId), pf));
    }

    // Llena la pestaña BOM con el árbol interno de un STEP (generado por ExtractorStep).
    // Fila 0 = el propio archivo; los componentes no tienen ruta propia (viven en el STEP).
    private void CargarBomStep(Archivo archivo)
    {
        try
        {
            using var json = System.Text.Json.JsonDocument.Parse(archivo.DatosExtraJson!);
            if (!json.RootElement.TryGetProperty("bom_step", out var bom)) return;

            var raiz = bom.TryGetProperty("raiz", out var r) ? r.GetString() : null;
            Bom.Add(new ItemBomSeleccionable(new ItemBom(
                0, archivo.Id, raiz ?? archivo.Nombre, archivo.Ruta, "step",
                CantidadEnPadre: 1, EsToolbox: false, EsEnvelope: false, EsSuprimido: false)));

            if (bom.TryGetProperty("componentes", out var comps) &&
                comps.ValueKind == System.Text.Json.JsonValueKind.Array)
                AgregarNodosBomStep(comps, nivel: 1);
        }
        catch { /* JSON ajeno o corrupto: la pestaña BOM queda vacía, sin romper el detalle */ }
    }

    private void AgregarNodosBomStep(System.Text.Json.JsonElement nodos, int nivel)
    {
        foreach (var n in nodos.EnumerateArray())
        {
            var nombre = n.TryGetProperty("nombre", out var nm) ? nm.GetString() : null;
            int cant   = n.TryGetProperty("cantidad", out var c) && c.TryGetInt32(out var v) ? v : 1;
            Bom.Add(new ItemBomSeleccionable(new ItemBom(
                nivel, null, nombre ?? "(sin nombre)", "(interno del STEP)", "step",
                CantidadEnPadre: cant, EsToolbox: false, EsEnvelope: false, EsSuprimido: false)));

            if (n.TryGetProperty("hijos", out var hijos) &&
                hijos.ValueKind == System.Text.Json.JsonValueKind.Array)
                AgregarNodosBomStep(hijos, nivel + 1);
        }
    }

    [RelayCommand]
    private void ExportarPropiedades()
    {
        if (Archivo is null) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter   = "Excel|*.xlsx",
            FileName = $"Props_{Path.GetFileNameWithoutExtension(Archivo.Nombre)}.xlsx"
        };
        if (dlg.ShowDialog() != true) return;
        Servicios.ExportadorExcel.ExportarPropiedades(Propiedades.ToList(), Archivo.Nombre, dlg.FileName);
    }

    [RelayCommand]
    private void MarcarTodosBom()
    {
        foreach (var item in Bom) item.Incluido = true;
    }

    [RelayCommand]
    private void DesmarcarTodosBom()
    {
        foreach (var item in Bom) item.Incluido = false;
    }

    [RelayCommand]
    private async Task ExportarBomExcelAsync()
    {
        if (Archivo is null || Archivo.Tipo != "ensamble") return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter   = "Excel|*.xlsx",
            FileName = $"BOM_{Path.GetFileNameWithoutExtension(Archivo.Nombre)}.xlsx"
        };
        if (dlg.ShowDialog() != true) return;

        var indentado = Bom.Where(b => b.Incluido).Select(b => b.Item).ToList();

        // El aplanado excluye por pieza (Ruta), no por instancia: si una pieza desmarcada
        // aparece en varios puntos del árbol, se excluye de todas sus ocurrencias.
        var rutasExcluidas = Bom.Where(b => !b.Incluido)
            .Select(b => b.Item.Ruta)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var aplanadoCompleto = await _servicioBom.ObtenerBomAplanadoAsync(Archivo.Id);
        var aplanado = aplanadoCompleto.Where(a => !rutasExcluidas.Contains(a.Ruta)).ToList();

        Servicios.ExportadorExcel.ExportarBomCompleto(indentado, aplanado, Archivo.Nombre, dlg.FileName);
    }
}
