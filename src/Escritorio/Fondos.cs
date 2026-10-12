using System.Runtime.InteropServices;

namespace Escritorio;

/// <summary>
/// Fondo de pantalla por escritorio: cada escritorio puede pedir una imagen para cada pantalla
/// («fondos»). Al entrar a uno sin fondos (o al modo libre) se restauran los originales, que se
/// guardan en estado.json por si la app se cerró a medias.
/// </summary>
internal sealed class Fondos
{
    readonly Aplicacion app;

    public Fondos(Aplicacion app) => this.app = app;

    public void Aplicar(Dictionary<string, string>? fondos)
    {
        try
        {
            var escritorio = (IDesktopWallpaper)new DesktopWallpaper();
            var pantallas = Pantallas.Listar();
            var monitores = Monitores(escritorio);
            var originales = app.Estado.FondosOriginales ?? new Dictionary<string, string>();

            // A qué monitor va cada imagen pedida.
            var pedidos = new Dictionary<string, string>();
            foreach (var (pantallaPedida, rutaCruda) in fondos ?? new())
            {
                var ruta = Environment.ExpandEnvironmentVariables(rutaCruda ?? "").Trim().Trim('"');
                if (!File.Exists(ruta))
                {
                    Registro.Error($"No existe la imagen de fondo «{rutaCruda}»");
                    continue;
                }
                var pantalla = Geometria.ElegirPantalla(pantallas, pantallaPedida);
                var monitor = monitores.FirstOrDefault(m => m.Rect == pantalla.Area);
                if (monitor.Id is not null)
                    pedidos[monitor.Id] = ruta;
            }

            // Los monitores que este escritorio no pide vuelven a su fondo original.
            foreach (var (id, original) in originales.ToList())
            {
                if (pedidos.ContainsKey(id))
                    continue;
                if (original.Length > 0 && monitores.Any(m => m.Id == id))
                    escritorio.SetWallpaper(id, original);
                originales.Remove(id);
            }
            foreach (var (id, ruta) in pedidos)
            {
                if (!originales.ContainsKey(id))
                {
                    escritorio.GetWallpaper(id, out var actual);
                    originales[id] = actual ?? "";
                }
                escritorio.SetWallpaper(id, ruta);
            }
            app.Estado.FondosOriginales = originales.Count > 0 ? originales : null;
            app.GuardarEstado();
        }
        catch (Exception error) when (error is COMException or InvalidCastException)
        {
            Registro.Error("No se pudo cambiar el fondo de pantalla", error);
        }
    }

    static List<(string Id, Rect Rect)> Monitores(IDesktopWallpaper escritorio)
    {
        var lista = new List<(string, Rect)>();
        escritorio.GetMonitorDevicePathCount(out uint cantidad);
        for (uint i = 0; i < cantidad; i++)
        {
            escritorio.GetMonitorDevicePathAt(i, out var id);
            if (string.IsNullOrEmpty(id))
                continue;
            try
            {
                escritorio.GetMonitorRECT(id, out var rect);
                lista.Add((id, rect.ARect()));
            }
            catch (COMException)
            {
                // Un monitor desconectado sigue en la lista pero sin rectángulo.
            }
        }
        return lista;
    }

    [ComImport, Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD")]
    class DesktopWallpaper;

    // Solo los primeros métodos de la interfaz, en su orden: no se usan los demás.
    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitor, [MarshalAs(UnmanagedType.LPWStr)] string ruta);
        void GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitor, [MarshalAs(UnmanagedType.LPWStr)] out string ruta);
        void GetMonitorDevicePathAt(uint indice, [MarshalAs(UnmanagedType.LPWStr)] out string id);
        void GetMonitorDevicePathCount(out uint cantidad);
        void GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string id, out Win32.RECT rect);
    }
}
