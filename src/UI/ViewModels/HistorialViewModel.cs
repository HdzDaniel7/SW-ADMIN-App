using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using SWDataExtractor.Data;
using SWDataExtractor.Data.Entities;

namespace SWDataExtractor.UI.ViewModels;

public partial class HistorialViewModel : ObservableObject
{
    private readonly AppDbContext _db;

    [ObservableProperty] private string _filtroPropiedad = "";
    [ObservableProperty] private string _filtroUsuario   = "";

    public ObservableCollection<HistorialPropiedad> Historial { get; } = [];

    public HistorialViewModel(AppDbContext db) => _db = db;

    [RelayCommand]
    private async Task CargarAsync()
    {
        var query = _db.HistorialPropiedades
            .Include(h => h.Archivo)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(FiltroPropiedad))
            query = query.Where(h => h.Propiedad.Contains(FiltroPropiedad));
        if (!string.IsNullOrWhiteSpace(FiltroUsuario))
            query = query.Where(h => h.Usuario.Contains(FiltroUsuario));

        var items = await query
            .OrderByDescending(h => h.Fecha)
            .Take(500)
            .ToListAsync();

        Historial.Clear();
        foreach (var h in items) Historial.Add(h);
    }
}
