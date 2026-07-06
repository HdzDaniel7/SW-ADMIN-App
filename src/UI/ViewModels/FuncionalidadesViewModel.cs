using CommunityToolkit.Mvvm.ComponentModel;
using SWDataExtractor.Application.Config;
using SWDataExtractor.Application.Servicios;

namespace SWDataExtractor.UI.ViewModels;

/// <summary>
/// Singleton que expone los permisos efectivos del rol activo.
/// Se inicializa en App.OnStartup y se actualiza al cambiar el rol desde ConfiguracionWindow.
/// </summary>
public partial class FuncionalidadesViewModel : ObservableObject
{
    [ObservableProperty] private bool _extraccionProfunda   = true;
    [ObservableProperty] private bool _escrituraPropiedades = true;
    [ObservableProperty] private bool _etiquetas            = true;
    [ObservableProperty] private bool _comparacionBom       = true;
    [ObservableProperty] private bool _exportacionExcel     = true;
    [ObservableProperty] private Rol  _rolActual            = Rol.Administrador;

    public void Aplicar(Rol rol, ConfiguracionFuncionalidades cfg)
    {
        RolActual            = rol;
        ExtraccionProfunda   = cfg.ExtraccionProfunda;
        EscrituraPropiedades = cfg.EscrituraPropiedades;
        Etiquetas            = cfg.Etiquetas;
        ComparacionBom       = cfg.ComparacionBom;
        ExportacionExcel     = cfg.ExportacionExcel;
    }
}
