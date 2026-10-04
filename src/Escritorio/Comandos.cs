using System.Runtime.InteropServices;

namespace Escritorio;

/// <summary>
/// Ventana invisible que recibe órdenes de otras copias del programa (por ejemplo, de un acceso
/// directo con «Escritorio.exe --escritorio trabajo»). También sirve para volver al hilo de la interfaz.
/// </summary>
internal sealed class Comandos : Form
{
    const string TituloVentana = "Escritorio.Comandos";
    const int WM_COPYDATA = 0x004A;

    [StructLayout(LayoutKind.Sequential)]
    struct COPYDATASTRUCT
    {
        public IntPtr dwData;
        public int cbData;
        public IntPtr lpData;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindowW(string? clase, string titulo);

    [DllImport("user32.dll")]
    static extern IntPtr SendMessageW(IntPtr hwnd, int mensaje, IntPtr w, ref COPYDATASTRUCT datos);

    public event Action<string>? Recibido;

    public Comandos()
    {
        Text = TituloVentana;
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(-32000, -32000);
        _ = Handle;  // existe desde ya, sin mostrarse nunca
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_COPYDATA)
        {
            var datos = Marshal.PtrToStructure<COPYDATASTRUCT>(m.LParam);
            var texto = Marshal.PtrToStringUni(datos.lpData, datos.cbData / 2) ?? "";
            Recibido?.Invoke(texto);
            m.Result = 1;
            return;
        }
        base.WndProc(ref m);
    }

    /// <summary>Le manda una orden a la copia que ya está abierta. False si no hay ninguna.</summary>
    public static bool Enviar(string texto)
    {
        var destino = FindWindowW(null, TituloVentana);
        if (destino == IntPtr.Zero)
            return false;
        var bytes = System.Text.Encoding.Unicode.GetBytes(texto);
        var memoria = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, memoria, bytes.Length);
            var datos = new COPYDATASTRUCT { dwData = IntPtr.Zero, cbData = bytes.Length, lpData = memoria };
            SendMessageW(destino, WM_COPYDATA, IntPtr.Zero, ref datos);
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(memoria);
        }
    }
}
