using CommunityToolkit.Mvvm.ComponentModel;
using SWDataExtractor.Application.Servicios;

namespace SWDataExtractor.UI.ViewModels;

// Envuelve un ItemBom (record inmutable) para agregarle estado de selección en la grilla.
public partial class ItemBomSeleccionable(ItemBom item) : ObservableObject
{
    [ObservableProperty] private bool _incluido = true;

    public ItemBom Item { get; } = item;
}
