using System.Diagnostics;

namespace Escritorio;

/// <summary>Una ventana de otro programa, como aparece en el menú.</summary>
internal sealed record VentanaAbierta(IntPtr Hwnd, string Proceso, string Titulo);

/// <summary>
/// Organiza las ventanas de otros programas según las reglas (las del escritorio actual y las
/// generales): cuando aparece una ventana nueva (Windows avisa con un gancho de eventos, sin revisar
/// nada periódicamente), se la manda a su pantalla y posición. Cada ventana se acomoda una sola vez:
/// después, el usuario la mueve donde quiera. Al cambiar de escritorio se acomodan todas las abiertas.
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
            if (vistas.Remove(hwnd))
                app.VentanaCerrada(hwnd);
            return;
        }
        if (vistas.Contains(hwnd) || !EsVentanaPrincipal(hwnd))
            return;
        vistas.Add(hwnd);
        app.VentanaAparecio(hwnd, Proceso(hwnd));
        if (Ajustes.Activo)
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
        var regla = Reglas.Buscar(app.ReglasActivas(), proceso, Win32.Titulo(hwnd));
        if (regla is null)
            return false;

        var pantalla = Geometria.ElegirPantalla(Pantallas.Listar(), regla.Pantalla);
        bool estabaMaximizada = Win32.IsZoomed(hwnd);
        if (estabaMaximizada)
            Win32.ShowWindow(hwnd, Win32.SW_RESTORE);
        if (Win32.RectDe(hwnd) is not { } actual)
            return false;
        var destino = Reglas.Destino(regla, app.EspacioVentanas(pantalla), actual);

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
        if (regla.Maximizar || (estabaMaximizada && !regla.TienePosicion && regla.Ancho is null && regla.Alto is null))
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

    /// <summary>Al entrar a un escritorio: abre los programas con «abrir» que falten y acomoda las ventanas abiertas.</summary>
    public int AlEntrarA(Escritorio escritorio)
    {
        var abiertas = VentanasAbiertas();
        foreach (var regla in escritorio.Ventanas.Where(r => !string.IsNullOrWhiteSpace(r.Abrir)))
        {
            if (abiertas.Any(v => Reglas.Coincide(regla, v.Proceso, v.Titulo)))
                continue;
            try
            {
                var ruta = Environment.ExpandEnvironmentVariables(regla.Abrir!.Trim().Trim('"'));
                Process.Start(new ProcessStartInfo(ruta) { UseShellExecute = true });
                Registro.Info($"Se abrió {regla.Describir()} ({ruta})");
            }
            catch (Exception error)
            {
                Registro.Error($"No se pudo abrir {regla.Describir()}", error);
            }
        }
        return abiertas.Count(v => Aplicar(v.Hwnd));
    }

    void Recordar(VentanaAbierta ventana, List<Regla> destino, string donde)
    {
        if (Win32.RectDe(ventana.Hwnd) is not { } rect)
            return;
        var regla = Reglas.Recordar(ventana.Proceso, ventana.Titulo, rect, Win32.IsZoomed(ventana.Hwnd), Pantallas.Listar(), app.EspacioVentanas);
        Reglas.Guardar(destino, regla);
        app.GuardarConfig();
        app.Avisar($"{regla.Describir()} irá a este mismo lugar cada vez que se abra {donde}.");
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

        var escritorio = app.EscritorioActual;
        var nombreEscritorio = escritorio?.Nombre(app.EscritorioActualId!);
        var recordar = new ToolStripMenuItem("Recordar dónde está…");
        foreach (var ventana in VentanasAbiertas().OrderBy(v => v.Proceso, StringComparer.OrdinalIgnoreCase))
        {
            var texto = Aplicacion.Recortar($"{ventana.Proceso} — {ventana.Titulo}");
            if (escritorio is null)
            {
                recordar.DropDownItems.Add(texto, null, (_, _) => Recordar(ventana, Ajustes.Reglas, "en todos los escritorios"));
                continue;
            }
            var opciones = new ToolStripMenuItem(texto);
            opciones.DropDownItems.Add($"Solo en «{nombreEscritorio}»", null, (_, _) => Recordar(ventana, escritorio.Ventanas, $"en «{nombreEscritorio}»"));
            opciones.DropDownItems.Add("En todos los escritorios", null, (_, _) => Recordar(ventana, Ajustes.Reglas, "en todos los escritorios"));
            recordar.DropDownItems.Add(opciones);
        }
        recordar.Enabled = recordar.DropDownItems.Count > 0;
        raiz.DropDownItems.Add(recordar);

        var olvidar = new ToolStripMenuItem("Olvidar regla");
        if (escritorio is not null)
            foreach (var regla in escritorio.Ventanas.ToList())
                olvidar.DropDownItems.Add(Aplicacion.Recortar($"«{nombreEscritorio}»: {regla.Describir()}"), null, (_, _) => Olvidar(escritorio.Ventanas, regla));
        foreach (var regla in Ajustes.Reglas.ToList())
            olvidar.DropDownItems.Add(Aplicacion.Recortar($"Siempre: {regla.Describir()}"), null, (_, _) => Olvidar(Ajustes.Reglas, regla));
        olvidar.Enabled = olvidar.DropDownItems.Count > 0;
        raiz.DropDownItems.Add(olvidar);
        return raiz;
    }

    void Olvidar(List<Regla> lista, Regla regla)
    {
        lista.Remove(regla);
        app.GuardarConfig();
    }
}
