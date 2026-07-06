using System.Windows;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using SWDataExtractor.Data;
using SWDataExtractor.Data.Entities;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Wpf.Ui.Markup;

namespace SWDataExtractor.UI.Servicios;

// Aplica y persiste el tema de la app: "Plano técnico" (claro) y "Consola" (oscuro).
// Comparten el mismo acento azul base; WPF-UI deriva las variantes por tema.
//
// Diseño (2.ª iteración, ver DECISIONES.md): la paleta propia vive en diccionarios
// intercambiables (Temas/*.xaml) y el tema base de WPF-UI se cambia seteando
// ThemesDictionary.Theme DIRECTAMENTE — la búsqueda interna de ApplicationThemeManager
// puede no encontrar el diccionario y fallar silenciosa. Además, Apply() por defecto
// aplica backdrop "Mica", que QUITA el fondo real de la ventana (se ve negro cuando el
// efecto no está disponible, y al volver a claro nunca se restaura) — por eso aquí
// siempre se pasa WindowBackdropType.None.
public class ServicioTema(AppDbContext db)
{
    private const string Clave = "TemaApp";

    private static readonly Color AcentoBase = (Color)ColorConverter.ConvertFromString("#1D4E89")!;

    public async Task<ApplicationTheme> ObtenerTemaGuardadoAsync(CancellationToken ct = default)
    {
        var ajuste = await db.AjustesApp.FirstOrDefaultAsync(a => a.Clave == Clave, ct);
        return ajuste?.Valor == "Dark" ? ApplicationTheme.Dark : ApplicationTheme.Light;
    }

    public async Task GuardarTemaAsync(ApplicationTheme tema, CancellationToken ct = default)
    {
        var valor  = tema == ApplicationTheme.Dark ? "Dark" : "Light";
        var ajuste = await db.AjustesApp.FirstOrDefaultAsync(a => a.Clave == Clave, ct);
        if (ajuste is null)
        {
            db.AjustesApp.Add(new AjusteApp
            {
                Clave       = Clave,
                Valor       = valor,
                Descripcion = "Tema de la UI: Light (Plano técnico) o Dark (Consola)"
            });
        }
        else
        {
            ajuste.Valor = valor;
        }
        await db.SaveChangesAsync(ct);
    }

    public void Aplicar(ApplicationTheme tema)
    {
        var recursos = System.Windows.Application.Current.Resources;
        var merged   = recursos.MergedDictionaries;

        // 1. Tema base de WPF-UI: setear ThemesDictionary.Theme directamente garantiza el
        //    cambio de Light.xaml/Dark.xaml (colores de texto, botones, encabezados, etc.).
        foreach (var dic in merged)
            if (dic is ThemesDictionary td)
                td.Theme = tema;

        // 2. Manager oficial además, para su caché (GetAppTheme) y el modo oscuro del chrome
        //    nativo de la ventana — con None para que NUNCA quite el fondo (ver arriba).
        ApplicationThemeManager.Apply(tema, WindowBackdropType.None, updateAccent: false);

        // 3. Acento propio (mismo azul en ambos temas; el manager deriva las variantes).
        ApplicationAccentColorManager.Apply(AcentoBase, tema);

        // 4. Paleta propia: quitar el diccionario del tema anterior y agregar el nuevo AL
        //    FINAL, para que sus claves ganen sobre el tema base de WPF-UI.
        var previo = merged.FirstOrDefault(d =>
            d.Source is not null && d.Source.OriginalString.Contains("Temas/Tema"));
        if (previo is not null)
            merged.Remove(previo);

        var nombre = tema == ApplicationTheme.Dark ? "TemaConsola" : "TemaPlanoTecnico";
        merged.Add(new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Temas/{nombre}.xaml", UriKind.Absolute)
        });
    }
}
