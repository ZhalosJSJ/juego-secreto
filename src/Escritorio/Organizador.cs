using System.Diagnostics;

namespace Escritorio;

/// <summary>Una ventana de otro programa, como aparece en el menú.</summary>
internal sealed record VentanaAbierta(IntPtr Hwnd, string Proceso, string Titulo);

/// <summary>
/// Organiza las ventanas de otros programas según las reglas de la configuración: cuando aparece
/// una ventana nueva (Windows avisa con un gancho de eventos, sin revisar nada periódicamente),
/// se la manda a su pantalla y posición. Cada ventana se acomoda una sola vez: después, el usuario
/// la mueve donde quiera.
/// </summary>
internal sealed class Organizador : IDisposable
{
    readonly Aplicacion app;
    readonly Win32.WinEventProc funcion;  // se guarda para que el recolector no la libere mientras Windows la usa
    readonly HashSet<IntPtr> vistas = new();
    readonly uint propioPid = (uint)Environment.ProcessId;
    readonly HashSet<string> sinPermiso = new(StringComparer.OrdinalIgnoreCase);
    IntPtr gancho;

    public Organizador(Aplicacion app)
    {
        this.app = app;
        funcion = AlEvento;
    }

    AjustesOrganizar Ajustes => app.Config.Organizar;

    /// <summary>Debe llamarse desde el hilo de la interfaz: los eventos llegan por su cola de mensajes.</summary>
    public void Iniciar()
    {
        if (gancho != IntPtr.Zero)
            return;
        gancho = Win32.SetWinEventHook(Win32.EVENT_OBJECT_DESTROY, Win32.EVENT_OBJECT_SHOW, IntPtr.Zero, funcion, 0, 0,
            Win32.WINEVENT_OUTOFCONTEXT | Win32.WINEVENT_SKIPOWNPROCESS);
        if (gancho == IntPtr.Zero)
            Registro.Error("No se pudo vigilar las ventanas nuevas: no se organizarán solas");
    }

    public void Dispose()
    {
        if (gancho != IntPtr.Zero)
            Win32.UnhookWinEvent(gancho);
        gancho = IntPtr.Zero;
    }

    void AlEvento(IntPtr g, uint evento, IntPtr hwnd, int idObjeto, int idHijo, uint hilo, uint tiempo)
    {
        if (idObjeto != Win32.OBJID_WINDOW || idHijo != 0 || hwnd == IntPtr.Zero)
            return;
        if (evento == Win32.EVENT_OBJECT_DESTROY)
        {
            vistas.Remove(hwnd);
            return;
        }
        if (!Ajustes.Activo || Ajustes.Reglas.Count == 0 || vistas.Contains(hwnd) || !EsVentanaPrincipal(hwnd))
            return;
        vistas.Add(hwnd);
        _ = AplicarEnUnMomento(hwnd);
    }

    async Task AplicarEnUnMomento(IntPtr hwnd)
    {
        // Muchas apps se acomodan solas justo después de aparecer: se espera a que terminen.
        await Task.Delay(400);
        try
        {
            Aplicar(hwnd);
        }
        catch (Exception error)
        {
            Registro.Error("No se pudo ordenar una ventana", error);
        }
    }

    /// <summary>Ventanas «de verdad»: visibles, de primer nivel, con título, sin dueño y que no son herramientas.</summary>
    static bool EsVentanaPrincipal(IntPtr hwnd)
    {
        if (!Win32.IsWindowVisible(hwnd) || Win32.GetAncestor(hwnd, Win32.GA_ROOT) != hwnd)
            return false;
        if (Win32.GetWindow(hwnd, Win32.GW_OWNER) != IntPtr.Zero)
            return false;
        long estilo = (long)Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE);
        if ((estilo & Win32.WS_EX_TOOLWINDOW) != 0)
            return false;
        return !Win32.Oculta(hwnd) && Win32.GetWindowTextLength(hwnd) > 0;
    }

    static string Proceso(IntPtr hwnd)
    {
        Win32.GetWindowThreadProcessId(hwnd, out var pid);
        try
        {
            using var proceso = Process.GetProcessById((int)pid);
            return proceso.ProcessName;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            return "";
        }
    }

    /// <summary>Aplica la regla que corresponda. Devuelve true si movió la ventana.</summary>
    bool Aplicar(IntPtr hwnd)
    {
        if (!EsVentanaPrincipal(hwnd) || Win32.IsIconic(hwnd))
            return false;
        var proceso = Proceso(hwnd);
        var regla = Reglas.Buscar(Ajustes.Reglas, proceso, Win32.Titulo(hwnd));
        if (regla is null)
            return false;

        var pantalla = Geometria.ElegirPantalla(Pantallas.Listar(), regla.Pantalla);
        bool estabaMaximizada = Win32.IsZoomed(hwnd);
        if (estabaMaximizada)
            Win32.ShowWindow(hwnd, Win32.SW_RESTORE);
        if (Win32.RectDe(hwnd) is not { } actual)
            return false;
        var destino = Reglas.Destino(regla, pantalla, actual);

        // Dos veces: al pasar a una pantalla con otra escala, la app se reajusta después del primer movimiento.
        for (int vez = 0; vez < 2; vez++)
        {
            if (!Win32.SetWindowPos(hwnd, IntPtr.Zero, destino.X, destino.Y, destino.Ancho, destino.Alto, Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE))
            {
                // Windows no deja mover ventanas de programas abiertos como administrador.
                if (sinPermiso.Add(proceso))
                    Registro.Error($"No se pudo mover {proceso}: probablemente se ejecuta como administrador");
                return false;
            }
        }
        if (regla.Maximizar || (estabaMaximizada && !regla.TieneRect))
            Win32.ShowWindow(hwnd, Win32.SW_MAXIMIZE);
        return true;
    }

    public List<VentanaAbierta> VentanasAbiertas()
    {
        var lista = new List<VentanaAbierta>();
        Win32.EnumWindows((hwnd, _) =>
        {
            Win32.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid != propioPid && EsVentanaPrincipal(hwnd))
                lista.Add(new VentanaAbierta(hwnd, Proceso(hwnd), Win32.Titulo(hwnd)));
            return true;
        }, IntPtr.Zero);
        return lista;
    }

    public int OrdenarAhora() => VentanasAbiertas().Count(v => Aplicar(v.Hwnd));

    void Recordar(VentanaAbierta ventana)
    {
        if (Win32.RectDe(ventana.Hwnd) is not { } rect)
            return;
        var regla = Reglas.Recordar(ventana.Proceso, ventana.Titulo, rect, Win32.IsZoomed(ventana.Hwnd), Pantallas.Listar());
        Reglas.Guardar(Ajustes.Reglas, regla);
        app.GuardarConfig();
        app.Avisar($"Cada vez que se abra, {regla.Describir()} irá a este mismo lugar.");
    }

    public ToolStripMenuItem CrearMenu()
    {
        var raiz = new ToolStripMenuItem("Organizar ventanas");
        raiz.DropDownItems.Add(new ToolStripMenuItem("Acomodar las nuevas automáticamente", null, (_, _) =>
        {
            Ajustes.Activo = !Ajustes.Activo;
            app.GuardarConfig();
        }) { Checked = Ajustes.Activo });
        raiz.DropDownItems.Add("Acomodar las abiertas ahora", null, (_, _) =>
        {
            int movidas = OrdenarAhora();
            app.Avisar(movidas == 1 ? "Se acomodó 1 ventana." : $"Se acomodaron {movidas} ventanas.");
        });
        raiz.DropDownItems.Add(new ToolStripSeparator());

        var recordar = new ToolStripMenuItem("Recordar dónde está…");
        foreach (var ventana in VentanasAbiertas().OrderBy(v => v.Proceso, StringComparer.OrdinalIgnoreCase))
            recordar.DropDownItems.Add(Recortar($"{ventana.Proceso} — {ventana.Titulo}"), null, (_, _) => Recordar(ventana));
        recordar.Enabled = recordar.DropDownItems.Count > 0;
        raiz.DropDownItems.Add(recordar);

        var olvidar = new ToolStripMenuItem("Olvidar regla");
        foreach (var regla in Ajustes.Reglas.ToList())
            olvidar.DropDownItems.Add(Recortar(regla.Describir()), null, (_, _) =>
            {
                Ajustes.Reglas.Remove(regla);
                app.GuardarConfig();
            });
        olvidar.Enabled = olvidar.DropDownItems.Count > 0;
        raiz.DropDownItems.Add(olvidar);
        return raiz;
    }

    /// <summary>Texto para el menú: corto, y con «&amp;» escapado (si no, el menú lo toma como atajo).</summary>
    static string Recortar(string texto) => (texto.Length <= 70 ? texto : texto[..67] + "…").Replace("&", "&&");
}
