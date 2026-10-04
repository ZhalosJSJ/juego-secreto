namespace Escritorio;

internal static class Pantallas
{
    /// <summary>
    /// Las pantallas conectadas, en píxeles físicos (la app declara PerMonitorV2, así que
    /// Screen y SetWindowPos usan las mismas coordenadas), con la escala de cada una.
    /// </summary>
    public static List<Pantalla> Listar() =>
        Screen.AllScreens.Select(s => new Pantalla(
            s.DeviceName,
            new Rect(s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height),
            new Rect(s.WorkingArea.X, s.WorkingArea.Y, s.WorkingArea.Width, s.WorkingArea.Height),
            s.Primary,
            Escala(s))).ToList();

    static double Escala(Screen pantalla)
    {
        var centro = new Win32.POINT
        {
            X = pantalla.Bounds.X + pantalla.Bounds.Width / 2,
            Y = pantalla.Bounds.Y + pantalla.Bounds.Height / 2,
        };
        var monitor = Win32.MonitorFromPoint(centro, Win32.MONITOR_DEFAULTTONEAREST);
        return Win32.GetDpiForMonitor(monitor, Win32.MDT_EFFECTIVE_DPI, out var dpi, out _) == 0 && dpi > 0 ? dpi / 96.0 : 1.0;
    }
}
