using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using SWDataExtractor.Data;
using SWDataExtractor.Data.Entities;

namespace SWDataExtractor.UI.ViewModels;

public partial class ColaTrabajoViewModel : ObservableObject
{
    private readonly AppDbContext _db;

    public ObservableCollection<TrabajoExtraccion> Trabajos { get; } = [];

    [ObservableProperty] private string _resumen = "";

    public ColaTrabajoViewModel(AppDbContext db) => _db = db;

    [RelayCommand]
    private async Task RefrescarAsync()
    {
        var lista = await _db.TrabajosExtraccion
            .Include(t => t.Archivo)
            .OrderByDescending(t => t.FechaEncolado)
            .Take(500)
            .ToListAsync();

        Trabajos.Clear();
        foreach (var t in lista) Trabajos.Add(t);

        var ok      = lista.Count(t => t.Estado == "ok");
        var error   = lista.Count(t => t.Estado is "error" or "timeout");
        var proceso = lista.Count(t => t.Estado == "en_proceso");
        Resumen = $"Total: {lista.Count} | OK: {ok} | Error: {error} | En proceso: {proceso}";
    }

    [RelayCommand]
    private async Task LimpiarCompletadosAsync()
    {
        var completados = await _db.TrabajosExtraccion
            .Where(t => t.Estado == "ok")
            .ToListAsync();
        _db.TrabajosExtraccion.RemoveRange(completados);
        await _db.SaveChangesAsync();
        await RefrescarAsync();
    }
}
