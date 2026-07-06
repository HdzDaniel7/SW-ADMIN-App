using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using SWDataExtractor.Application.Servicios;
using SWDataExtractor.Core.Contratos;
using SWDataExtractor.Data;

namespace SWDataExtractor.UI.ViewModels;

public record FilaPreviewLote(
    string  Archivo,
    string  Propiedad,
    string? ValorAnterior,
    string  ValorNuevo,
    bool    EsAdvertencia = false);

public record FilaResultadoLote(
    string  Archivo,
    string? ValorAnterior,
    string  ValorNuevo,
    string  Resultado,
    string? Mensaje);

public partial class PropiedadLoteViewModel : ObservableObject
{
    private readonly ServicioPropiedades       _servicio;
    private readonly AppDbContext              _db;
    private readonly IReadOnlyList<string>     _rutas;
    private IReadOnlyList<CambioPropiedad>?    _cambiosPreparados;

    [ObservableProperty] private string _propiedadNombre  = "";
    [ObservableProperty] private string _configuracion    = "";
    [ObservableProperty] private string _valorNuevo       = "";
    [ObservableProperty] private bool   _estaCargando;
    [ObservableProperty] private bool   _puedoConfirmar;
    [ObservableProperty] private bool   _ejecucionCompletada;
    [ObservableProperty] private string _mensajeEstado    =
        "Ingrese los datos y haga clic en 'Calcular diff'.";

    public string                                  CabeceraTitulo       { get; }
    public ObservableCollection<string>            PropiedadesDisponibles { get; } = [];
    public ObservableCollection<FilaPreviewLote>   Preview              { get; } = [];
    public ObservableCollection<FilaResultadoLote> Resultados           { get; } = [];

    public PropiedadLoteViewModel(
        ServicioPropiedades servicio,
        AppDbContext        db,
        IReadOnlyList<string> rutas)
    {
        _servicio = servicio;
        _db       = db;
        _rutas    = rutas;
        CabeceraTitulo = $"Editar propiedades en lote — {rutas.Count} archivo(s)";
    }

    public async Task CargarDiccionarioAsync()
    {
        var props = await _db.DiccionarioPropiedades
            .Where(d => d.Activa)
            .OrderBy(d => d.Nombre)
            .ToListAsync();
        PropiedadesDisponibles.Clear();
        foreach (var p in props) PropiedadesDisponibles.Add(p.Nombre);
    }

    [RelayCommand]
    private async Task CalcularDiffAsync()
    {
        if (string.IsNullOrWhiteSpace(PropiedadNombre))
        { MensajeEstado = "Ingrese el nombre de la propiedad."; return; }
        if (string.IsNullOrWhiteSpace(ValorNuevo))
        { MensajeEstado = "Ingrese el valor nuevo."; return; }

        EstaCargando   = true;
        PuedoConfirmar = false;
        Preview.Clear();
        _cambiosPreparados = null;
        try
        {
            string? cfg = string.IsNullOrWhiteSpace(Configuracion) ? null : Configuracion.Trim();
            var solicitudes = _rutas
                .Select(r => new CambioPropiedad(r, cfg, PropiedadNombre.Trim(), ValorNuevo.Trim()))
                .ToList();

            var preview = await _servicio.PrepararLoteAsync(solicitudes);
            _cambiosPreparados = preview.Cambios;

            foreach (var c in preview.Cambios)
            {
                string? valorAnterior = null;
                var archivo = await _db.Archivos.FirstOrDefaultAsync(a => a.Ruta == c.Ruta);
                if (archivo is not null)
                {
                    int? cfgId = cfg is null ? null
                        : (await _db.Configuraciones.FirstOrDefaultAsync(
                            x => x.ArchivoId == archivo.Id && x.Nombre == cfg))?.Id;
                    var prop = await _db.Propiedades.FirstOrDefaultAsync(
                        p => p.ArchivoId == archivo.Id && p.ConfiguracionId == cfgId && p.Nombre == c.Propiedad);
                    valorAnterior = prop?.ValorResuelto ?? prop?.Valor;
                }
                Preview.Add(new FilaPreviewLote(
                    Path.GetFileName(c.Ruta), c.Propiedad, valorAnterior, c.ValorNuevo));
            }

            foreach (var adv in preview.Advertencias)
                Preview.Add(new FilaPreviewLote("", adv, null, "", EsAdvertencia: true));

            PuedoConfirmar = preview.Cambios.Count > 0;
            MensajeEstado  = $"{preview.Cambios.Count} cambio(s) a aplicar. " +
                             $"{preview.Advertencias.Count} advertencia(s).";
        }
        catch (Exception ex) { MensajeEstado = $"Error al calcular: {ex.Message}"; }
        finally { EstaCargando = false; }
    }

    [RelayCommand]
    private async Task ConfirmarAsync()
    {
        if (_cambiosPreparados is null || _cambiosPreparados.Count == 0) return;

        EstaCargando   = true;
        PuedoConfirmar = false;
        try
        {
            var lote     = new LoteEscritura(Environment.UserName, _cambiosPreparados);
            var resultado = await _servicio.EscribirLoteAsync(lote);

            Resultados.Clear();
            int ok = 0, errores = 0;
            foreach (var r in resultado.Resultados)
            {
                Resultados.Add(new FilaResultadoLote(
                    Path.GetFileName(r.Cambio.Ruta),
                    r.ValorAnterior, r.Cambio.ValorNuevo, r.Resultado, r.Mensaje));
                if (r.Resultado == "ok") ok++;
                else errores++;
            }
            EjecucionCompletada = true;
            MensajeEstado = $"Lote {resultado.LoteId[..8]}…: {ok} OK, {errores} error(es).";
        }
        catch (Exception ex) { MensajeEstado = $"Error al escribir: {ex.Message}"; }
        finally { EstaCargando = false; }
    }
}
