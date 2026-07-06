using Wpf.Ui.Controls;

namespace SWDataExtractor.UI.Servicios;

// Envuelve el SnackbarPresenter de WPF-UI para mostrar confirmaciones no bloqueantes
// (p. ej. "tarea registrada") sin interrumpir al usuario con un MessageBox modal. Las
// decisiones sí/no y los errores siguen usando MessageBox — solo esto es para avisos.
// Singleton: MainWindow conecta el Presenter una vez al arrancar; cualquier ViewModel o
// code-behind puede inyectar este servicio para notificar sin conocer la ventana.
public class ServicioNotificaciones
{
    public SnackbarPresenter? Presenter { get; set; }

    public void Exito(string mensaje, string? titulo = null) =>
        Mostrar(titulo ?? "Listo", mensaje, ControlAppearance.Success);

    public void Error(string mensaje, string? titulo = null) =>
        Mostrar(titulo ?? "Error", mensaje, ControlAppearance.Danger);

    private void Mostrar(string titulo, string mensaje, ControlAppearance apariencia)
    {
        if (Presenter is null) return;
        var snackbar = new Snackbar(Presenter)
        {
            Title      = titulo,
            Content    = mensaje,
            Appearance = apariencia,
            Timeout    = TimeSpan.FromSeconds(4)
        };
        Presenter.AddToQue(snackbar);
    }
}
