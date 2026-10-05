using System.Runtime.InteropServices;
using System.Text;

namespace Escritorio;

/// <summary>Las funciones de Windows que .NET no trae.</summary>
internal static class Win32
{
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_APPWINDOW = 0x00040000;

    public const int WM_WINDOWPOSCHANGING = 0x0046;
    public const int WM_NCLBUTTONDOWN = 0x00A1;
    public const int HTCAPTION = 2;

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public static readonly IntPtr HWND_TOP = IntPtr.Zero;
    public static readonly IntPtr HWND_BOTTOM = new(1);

    public const int SW_MAXIMIZE = 3;
    public const int SW_RESTORE = 9;

    public const uint GW_OWNER = 4;
    public const uint GA_ROOT = 2;

    public const uint EVENT_OBJECT_DESTROY = 0x8001;
    public const uint EVENT_OBJECT_SHOW = 0x8002;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    public const int OBJID_WINDOW = 0;

    public const int DWMWA_CLOAKED = 14;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWCP_ROUND = 2;

    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const int MDT_EFFECTIVE_DPI = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPOS
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x, y, cx, cy;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public readonly Rect ARect() => new(Left, Top, Right - Left, Bottom - Top);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X, Y;
    }

    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
    public delegate void WinEventProc(IntPtr gancho, uint evento, IntPtr hwnd, int idObjeto, int idHijo, uint hilo, uint tiempo);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hwnd, IntPtr despuesDe, int x, int y, int ancho, int alto, uint flags);

    [DllImport("user32.dll")] public static extern bool ReleaseCapture();
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hwnd, int mensaje, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int comando);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hwnd, uint relacion);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint tipo);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int indice);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder texto, int maximo);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc funcion, IntPtr lParam);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT punto, uint flags);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int tecla);
    public const int VK_SPACE = 0x20;

    [DllImport("user32.dll")]
    public static extern IntPtr SetWinEventHook(uint minimo, uint maximo, IntPtr modulo, WinEventProc funcion, uint pid, uint hilo, uint flags);

    [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr gancho);

    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int atributo, out int valor, int tamano);
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int atributo, ref int valor, int tamano);

    [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr monitor, int tipo, out uint dpiX, out uint dpiY);

    public static string Titulo(IntPtr hwnd)
    {
        int largo = GetWindowTextLength(hwnd);
        if (largo <= 0)
            return "";
        var texto = new StringBuilder(largo + 1);
        GetWindowText(hwnd, texto, texto.Capacity);
        return texto.ToString();
    }

    public static Rect? RectDe(IntPtr hwnd) => GetWindowRect(hwnd, out var r) ? r.ARect() : null;

    public static bool Oculta(IntPtr hwnd) =>
        DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int oculta, sizeof(int)) == 0 && oculta != 0;

    public static void RedondearEsquinas(IntPtr hwnd)
    {
        int preferencia = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preferencia, sizeof(int));
    }
}
