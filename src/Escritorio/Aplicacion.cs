using System.Diagnostics;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace Escritorio;

/// <summary>La app: crea los widgets, el ícono de la bandeja, el organizador de ventanas y las baterías.</summary>
internal sealed class Aplicacion : ApplicationContext
{
    readonly Control invocador = new();
    readonly NotifyIcon bandeja;
    readonly ContextMenuStrip menu = new();
    readonly List<VentanaWidget> widgets = new();
    readonly Organizador organizador;
    readonly Baterias baterias;

    public Config Config { get; private set; }
    public Estado Estado { get; private set; }
    public Icon Icono { get; }

    /// <summary>Un solo entorno de WebView2 para todos los widgets: comparten procesos y perfil.</summary>
    public Task<CoreWebView2Environment> EntornoWeb { get; }

    public Aplicacion()
    {
        _ = invocador.Handle;  // para poder volver al hilo de la interfaz desde otros hilos
        Rutas.Crear();
        Rutas.ExtraerIntegrados();
        Icono = CargarIcono();
        Config = CargarConfig();
        Estado = CargarEstado();
        EntornoWeb = CoreWebView2Environment.CreateAsync(null, Rutas.WebView);

        bandeja = new NotifyIcon { Icon = Icono, Text = "Escritorio", ContextMenuStrip = menu, Visible = true };
        menu.Opening += (_, e) =>
        {
            ConstruirMenu();
            e.Cancel = false;
        };

        organizador = new Organizador(this);
        baterias = new Baterias(this);
        CrearWidgets();
        organizador.Iniciar();
        SystemEvents.DisplaySettingsChanged += PantallasCambiaron;
        Registro.Info($"Escritorio {Instalacion.Version} iniciado");
    }

    // --- Configuración y estado ---

    static Config CargarConfig()
    {
        if (!File.Exists(Rutas.Config))
        {
            var nueva = Config.PorDefecto();
            Json.Escribir(Rutas.Config, nueva);
            return nueva;
        }
        try
        {
            return Json.Leer(Rutas.Config, Config.PorDefecto);
        }
        catch (JsonException error)
        {
            // No se sobrescribe: el usuario puede corregir su archivo.
            MessageBox.Show($"config.json tiene un error y se usará la configuración por defecto hasta que lo corrija:\n\n{error.Message}",
                "Escritorio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return Config.PorDefecto();
        }
    }

    static Estado CargarEstado()
    {
        try
        {
            return Json.Leer(Rutas.Estado, () => new Estado());
        }
        catch (JsonException)
        {
            return new Estado();
        }
    }

    public void GuardarConfig() => Json.Escribir(Rutas.Config, Config);

    void GuardarEstado() => Json.Escribir(Rutas.Estado, Estado);

    static Icon CargarIcono()
    {
        using var recurso = typeof(Aplicacion).Assembly.GetManifestResourceStream("escritorio.ico");
        return recurso is null ? SystemIcons.Application : new Icon(recurso, SystemInformation.SmallIconSize);
    }

    // --- Widgets ---

    Pantalla PantallaDeWidgets() => Geometria.ElegirPantalla(Pantallas.Listar(), Config.Pantalla);

    Rect RectPara(VentanaWidget widget, Pantalla pantalla) => RectPara(widget.Id, widget.Ajustes, pantalla);

    Rect RectPara(string id, AjustesWidget ajustes, Pantalla pantalla) =>
        Geometria.UbicarWidget(pantalla, ajustes.Ancho, ajustes.Alto, ajustes.Lado, Estado.Posiciones.GetValueOrDefault(id));

    void CrearWidgets()
    {
        var pantalla = PantallaDeWidgets();
        foreach (var (id, ajustes) in Config.Widgets)
        {
            if (!ajustes.Activo)
                continue;
            var ventana = new VentanaWidget(this, id, ajustes, RectPara(id, ajustes, pantalla), Estado.Bloqueado);
            ventana.FormClosed += (_, _) => widgets.Remove(ventana);
            widgets.Add(ventana);
            ventana.Show();
        }
        baterias.Activar(widgets.Any(w => w.Ajustes.Integrado == "baterias"));
    }

    void CerrarWidgets()
    {
        foreach (var ventana in widgets.ToList())
            ventana.Close();
    }

    void Recolocar()
    {
        var pantalla = PantallaDeWidgets();
        foreach (var ventana in widgets)
            ventana.Colocar(RectPara(ventana, pantalla));
    }

    void PantallasCambiaron(object? sender, EventArgs e) => EnUI(Recolocar);

    /// <summary>El usuario soltó un widget: se recuerda dónde, relativo a la pantalla de los widgets.</summary>
    public void PosicionCambiada(VentanaWidget ventana)
    {
        var trabajo = PantallaDeWidgets().Trabajo;
        Estado.Posiciones[ventana.Id] = new Posicion
        {
            Dx = ventana.Left - trabajo.X,
            Dy = ventana.Top - trabajo.Y,
            Ancho = ventana.Ajustes.Fijo ? null : ventana.Width,
            Alto = ventana.Ajustes.Fijo ? null : ventana.Height,
        };
        GuardarEstado();
    }

    /// <summary>Inicia el programa de un widget (p. ej. el iniciar.bat del Gran Sabio) con su consola minimizada.</summary>
    public void Lanzar(AjustesWidget ajustes)
    {
        try
        {
            var ruta = Environment.ExpandEnvironmentVariables(ajustes.Iniciar ?? "").Trim().Trim('"');
            if (!File.Exists(ruta))
            {
                Registro.Error($"No se encontró «{ruta}» para iniciar {ajustes.Nombre}");
                return;
            }
            var titulo = ajustes.Nombre.Replace("\"", "");
            // «start /min» abre el programa minimizado y sin robar el foco. Las variables de «entorno»
            // (como NAVEGADOR=no para el Gran Sabio) se le pasan a este cmd y «start» las hereda.
            var inicio = new ProcessStartInfo("cmd.exe", $"/c start \"{titulo}\" /min \"{ruta}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(ruta)!,
            };
            foreach (var (nombre, valor) in ajustes.Entorno ?? new())
                inicio.Environment[nombre] = valor;
            Process.Start(inicio);
            Registro.Info($"Se inició {ajustes.Nombre} con {Path.GetFileName(ruta)}");
        }
        catch (Exception error)
        {
            Registro.Error($"No se pudo iniciar {ajustes.Nombre}", error);
        }
    }

    /// <summary>Un widget incluido pidió datos.</summary>
    public void Pedido(VentanaWidget ventana, string tema)
    {
        if (tema == "baterias")
            baterias.Pedir(ventana);
    }

    public void EnviarA(string integrado, string json)
    {
        foreach (var ventana in widgets.Where(w => w.Ajustes.Integrado == integrado))
            ventana.Enviar(json);
    }

    public void EnUI(Action accion)
    {
        if (invocador.IsDisposed)
            return;
        if (invocador.InvokeRequired)
            invocador.BeginInvoke(accion);
        else
            accion();
    }

    public void Avisar(string texto) => bandeja.ShowBalloonTip(4000, "Escritorio", texto, ToolTipIcon.Info);

    // --- Menú de la bandeja ---

    void ConstruirMenu()
    {
        menu.Items.Clear();

        var pantallas = Geometria.Ordenar(Pantallas.Listar());
        var actual = Geometria.ElegirPantalla(pantallas, Config.Pantalla);
        var submenu = new ToolStripMenuItem("Pantalla de los widgets");
        for (int i = 0; i < pantallas.Count; i++)
        {
            var pantalla = pantallas[i];
            var texto = $"Pantalla {i + 1}" + (pantalla.Principal ? " (principal)" : "");
            submenu.DropDownItems.Add(new ToolStripMenuItem(texto, null, (_, _) => ElegirPantalla(pantalla, pantallas))
            {
                Checked = pantalla.Nombre == actual.Nombre,
            });
        }
        menu.Items.Add(submenu);
        menu.Items.Add(new ToolStripMenuItem("Bloquear posición", null, (_, _) => AlternarBloqueo()) { Checked = Estado.Bloqueado });
        menu.Items.Add("Restablecer posiciones", null, (_, _) => Restablecer());
        menu.Items.Add("Recargar (relee la configuración)", null, (_, _) => Recargar());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(organizador.CrearMenu());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Iniciar con Windows", null, (_, _) => Instalacion.AlternarInicioAutomatico())
        {
            Checked = Instalacion.InicioAutomatico,
        });
        if (!Instalacion.EstaInstalado)
            menu.Items.Add("Instalar en este equipo", null, (_, _) => Instalar());
        menu.Items.Add("Abrir configuración", null, (_, _) => Abrir(Rutas.Config));
        menu.Items.Add("Abrir carpeta de widgets", null, (_, _) => Abrir(Rutas.WidgetsPropios));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => Salir());
    }

    void ElegirPantalla(Pantalla pantalla, List<Pantalla> pantallas)
    {
        Config.Pantalla = Geometria.Describir(pantalla, pantallas);
        GuardarConfig();
        Recolocar();
    }

    void AlternarBloqueo()
    {
        Estado.Bloqueado = !Estado.Bloqueado;
        GuardarEstado();
        foreach (var ventana in widgets)
            ventana.Bloquear(Estado.Bloqueado);
    }

    void Restablecer()
    {
        Estado.Posiciones.Clear();
        GuardarEstado();
        Recolocar();
    }

    /// <summary>Cierra los widgets y los vuelve a crear con lo que diga config.json ahora.</summary>
    void Recargar()
    {
        CerrarWidgets();
        Config = CargarConfig();
        Estado = CargarEstado();
        CrearWidgets();
    }

    void Instalar()
    {
        try
        {
            Instalacion.Instalar();
        }
        catch (Exception error)
        {
            Registro.Error("No se pudo instalar", error);
            MessageBox.Show($"No se pudo instalar:\n\n{error.Message}", "Escritorio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        // La copia instalada espera a que esta se cierre y toma su lugar.
        Process.Start(new ProcessStartInfo(Instalacion.Exe, "--reemplazar") { UseShellExecute = false });
        Salir();
    }

    static void Abrir(string ruta)
    {
        try
        {
            Process.Start(new ProcessStartInfo(ruta) { UseShellExecute = true });
        }
        catch (Exception) when (ruta.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            // Sin programa asociado a .json: el Bloc de notas siempre está.
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{ruta}\"") { UseShellExecute = false });
        }
    }

    void Salir()
    {
        SystemEvents.DisplaySettingsChanged -= PantallasCambiaron;
        organizador.Dispose();
        baterias.Dispose();
        CerrarWidgets();
        bandeja.Visible = false;
        bandeja.Dispose();
        invocador.Dispose();
        ExitThread();
    }
}
