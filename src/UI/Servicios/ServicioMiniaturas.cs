using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SWDataExtractor.UI.Servicios;

// Miniaturas vía el shell de Windows (IShellItemImageFactory) — el MISMO mecanismo que usa
// el Explorador de Windows para mostrar previews de .SLDPRT/.SLDASM. SolidWorks instala un
// thumbnail provider al instalarse, así que esto funciona SIN licencia DocManager y SIN SW
// abierto (principio "instala y funciona"). Si SW no está instalado, el shell devuelve un
// ícono genérico o falla → se retorna null y la UI simplemente no muestra preview.
// APIs de Windows documentadas (shell32), no de SolidWorks — no aplica regla VERIFICAR-API.
public static class ServicioMiniaturas
{
    public static ImageSource? ObtenerMiniatura(string ruta, int tamano = 256)
    {
        if (!File.Exists(ruta)) return null;

        IntPtr hBitmap = IntPtr.Zero;
        try
        {
            var riid = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(ruta, IntPtr.Zero, ref riid, out var factory);

            var size = new SIZE { cx = tamano, cy = tamano };
            // SIIGBF_BIGGERSIZEOK (0x1): acepta un bitmap mayor al pedido si ya está cacheado.
            int hr = factory.GetImage(size, 0x1, out hBitmap);
            Marshal.ReleaseComObject(factory);
            if (hr != 0 || hBitmap == IntPtr.Zero) return null;

            var fuente = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            fuente.Freeze();
            return fuente;
        }
        catch
        {
            // Preview es opcional: cualquier fallo (sin provider, archivo bloqueado, etc.) → sin imagen.
            return null;
        }
        finally
        {
            if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx; public int cy; }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        string pszPath, IntPtr pbc, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}
