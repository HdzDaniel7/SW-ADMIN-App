using System.Runtime.InteropServices;

namespace SWDataExtractor.SwApi;

// Gestiona el ciclo de vida de objetos COM registrados durante una extracción.
// Uso: using var scope = new ComScope(); var obj = scope.Register(comObject);
// Todos los objetos se liberan en orden inverso al salir del using.
public sealed class ComScope : IDisposable
{
    private readonly Stack<object> _objetos = new();

    public T Register<T>(T obj) where T : class
    {
        _objetos.Push(obj);
        return obj;
    }

    public void Dispose()
    {
        while (_objetos.TryPop(out var obj))
        {
            try { Marshal.ReleaseComObject(obj); }
            catch { /* liberar en el finally incluso si falla */ }
        }
    }
}
