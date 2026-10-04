using System.Diagnostics;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace Escritorio;

/// <summary>La app: crea los widgets, el ícono de la bandeja, el organizador de ventanas y las baterías.</summary>
internal sealed class Aplicacion : ApplicationContext
{
    /// <summary>Lo que hay en pantalla para el escritorio actual: cada widget y dónde va.</summary>
    sealed record Distribucion(List<WidgetEfectivo> Widgets, Dictionary<string, Rect> Rects, Pantalla Pantalla, Rect? Columna, string? Lado, int Margen);

    readonly Comandos comandos = new();
    readonly NotifyIcon bandeja;
    readonly ContextMenuStrip menu = new();
    readonly List<VentanaWidget> widgets = new();
    readonly Organizador organizador;
    readonly Baterias baterias;
    Dictionary<string, WidgetEfectivo> efectivos = new();

    public Config Config { get; private set; }
    public Estado Estado { get; private set; }
    public Icon Icono { get; }

    /// <summary>Un solo entorno de WebView2 para todos los widgets: comparten procesos y perfil.</summary>
    public Task<CoreWebView2Environment> EntornoWeb { get; }

    public Aplicacion(string? escritorioPedido = null)
    {
        Rutas.Crear();
        Rutas.ExtraerIntegrados();
        Icono = CargarIcono();
        Config = CargarConfig();
        Estado = CargarEstado();
        EntornoWeb = CoreWebView2Environment.CreateAsync(null, Rutas.WebView);
        comandos.Recibido += Ejecutar;

        bandeja = new NotifyIcon { Icon = Icono, Text = "Escritorio", ContextMenuStrip = menu, Visible = true };
        menu.Opening += (_, e) =>
        {
            ConstruirMenu();
            e.Cancel = false;
        };

        organizador = new Organizador(this);
        baterias = new Baterias(this);
        if (escritorioPedido is not null)
            Estado.Escritorio = IdEscritorio(escritorioPedido);
        AplicarEscritorio();
        organizador.Iniciar();
        if (escritorioPedido is not null && EscritorioActual is not null)
            organizador.AlEntrarA(EscritorioActual);
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

    // --- Escritorios ---

    public Escritorio? EscritorioActual =>
        Estado.Escritorio is { } id && Config.Escritorios.TryGetValue(id, out var escritorio) ? escritorio : null;

    public string? EscritorioActualId => EscritorioActual is null ? null : Estado.Escritorio;

    /// <summary>El id tal como está en config.json (sin importar mayúsculas), o null para «libre» o desconocido.</summary>
    string? IdEscritorio(string? pedido)
    {
        if (string.IsNullOrWhiteSpace(pedido) || pedido.Equals("libre", StringComparison.OrdinalIgnoreCase))
            return null;
        var id = Config.Escritorios.Keys.FirstOrDefault(k => k.Equals(pedido.Trim(), StringComparison.OrdinalIgnoreCase));
        if (id is null)
            Registro.Error($"No existe el escritorio «{pedido}» en config.json");
        return id;
    }

    /// <summary>Las reglas que valen ahora: primero las del escritorio actual, después las generales.</summary>
    public IEnumerable<IEnumerable<Regla>> ReglasActivas()
    {
        if (EscritorioActual is { } escritorio)
            yield return escritorio.Ventanas;
        yield return Config.Organizar.Reglas;
    }

    public void CambiarEscritorio(string? id)
    {
        Estado.Escritorio = id;
        GuardarEstado();
        AplicarEscritorio();
        var escritorio = EscritorioActual;
        if (escritorio is null)
        {
            Avisar("Escritorio libre: los widgets vuelven a su distribución general.");
            return;
        }
        int movidas = organizador.AlEntrarA(escritorio);
        Avisar($"Escritorio «{escritorio.Nombre(Estado.Escritorio!)}»: {(movidas == 1 ? "1 ventana acomodada" : $"{movidas} ventanas acomodadas")}.");
    }

    /// <summary>Órdenes que llegan desde otra copia del programa (Escritorio.exe --escritorio …).</summary>
    void Ejecutar(string orden)
    {
        const string prefijo = "escritorio ";
        if (orden.StartsWith(prefijo, StringComparison.Ordinal))
            CambiarEscritorio(IdEscritorio(orden[prefijo.Length..]));
    }

    // --- Widgets ---

    Pantalla PantallaDeWidgets(List<Pantalla>? pantallas = null) =>
        Geometria.ElegirPantalla(pantallas ?? Pantallas.Listar(), EscritorioActual?.Pantalla ?? Config.Pantalla);

    string ClavePosicion(string id) => EscritorioActualId is { } escritorio ? $"{escritorio}/{id}" : id;

    Distribucion CalcularDistribucion()
    {
        var pantallas = Pantallas.Listar();
        var porDefecto = PantallaDeWidgets(pantallas);
        var escritorio = EscritorioActual;
        var lista = Config.WidgetsDe(escritorio);
        var rects = new Dictionary<string, Rect>();
        var apilables = new List<Geometria.Apilable>();
        int margen = Geometria.Escalado(Geometria.Margen, porDefecto);
        foreach (var widget in lista)
        {
            var pantalla = widget.Pantalla is null ? porDefecto : Geometria.ElegirPantalla(pantallas, widget.Pantalla);
            var guardada = Estado.Posiciones.GetValueOrDefault(ClavePosicion(widget.Id));
            // Se apilan los de la pantalla de los widgets que el usuario no haya movido a mano.
            if (escritorio?.Apilar is not null && pantalla.Nombre == porDefecto.Nombre && guardada is null)
                apilables.Add(new Geometria.Apilable(widget.Id, Geometria.Escalado(widget.Ancho, porDefecto), Geometria.Escalado(widget.Alto, porDefecto)));
            else
                rects[widget.Id] = Geometria.UbicarWidget(pantalla, widget.Ancho, widget.Alto, widget.Lado, guardada);
        }
        var pila = Geometria.Apilar(porDefecto.Trabajo, apilables, escritorio?.Apilar, margen);
        foreach (var (id, rect) in pila.Posiciones)
            rects[id] = rect;
        return new Distribucion(lista, rects, porDefecto, pila.Columna, escritorio?.Apilar, margen);
    }

    /// <summary>El espacio para las ventanas de otros programas en esa pantalla: lo que no ocupa la columna de widgets.</summary>
    public Rect EspacioVentanas(Pantalla pantalla)
    {
        var distribucion = CalcularDistribucion();
        return pantalla.Nombre == distribucion.Pantalla.Nombre
            ? Geometria.ZonaLibre(pantalla.Trabajo, distribucion.Columna, distribucion.Lado, distribucion.Margen)
            : pantalla.Trabajo;
    }

    /// <summary>Deja en pantalla exactamente los widgets del escritorio actual, cada uno en su lugar.</summary>
    void AplicarEscritorio()
    {
        var distribucion = CalcularDistribucion();
        efectivos = distribucion.Widgets.ToDictionary(w => w.Id);
        foreach (var ventana in widgets.ToList())
            if (!distribucion.Rects.ContainsKey(ventana.Id))
                ventana.Close();
        foreach (var widget in distribucion.Widgets)
        {
            var rect = distribucion.Rects[widget.Id];
            var existente = widgets.FirstOrDefault(v => v.Id == widget.Id);
            if (existente is not null)
            {
                existente.Colocar(rect);
                continue;
            }
            var ventana = new VentanaWidget(this, widget.Id, widget.Ajustes, rect, Estado.Bloqueado);
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

    void PantallasCambiaron(object? sender, EventArgs e) => EnUI(AplicarEscritorio);

    /// <summary>El usuario soltó un widget: se recuerda dónde, relativo a la pantalla del widget.</summary>
    public void PosicionCambiada(VentanaWidget ventana)
    {
        var pantallas = Pantallas.Listar();
        var pantalla = efectivos.GetValueOrDefault(ventana.Id)?.Pantalla is { } propia
            ? Geometria.ElegirPantalla(pantallas, propia)
            : PantallaDeWidgets(pantallas);
        Estado.Posiciones[ClavePosicion(ventana.Id)] = new Posicion
        {
            Dx = ventana.Left - pantalla.Trabajo.X,
            Dy = ventana.Top - pantalla.Trabajo.Y,
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
        if (comandos.IsDisposed)
            return;
        if (comandos.InvokeRequired)
            comandos.BeginInvoke(accion);
        else
            accion();
    }

    public void Avisar(string texto) => bandeja.ShowBalloonTip(4000, "Escritorio", texto, ToolTipIcon.Info);

    // --- Menú de la bandeja ---

    void ConstruirMenu()
    {
        menu.Items.Clear();

        var escritorios = new ToolStripMenuItem("Escritorios");
        escritorios.DropDownItems.Add(new ToolStripMenuItem("Libre (sin escritorio)", null, (_, _) => CambiarEscritorio(null))
        {
            Checked = EscritorioActual is null,
        });
        if (Config.Escritorios.Count > 0)
            escritorios.DropDownItems.Add(new ToolStripSeparator());
        foreach (var (id, escritorio) in Config.Escritorios)
            escritorios.DropDownItems.Add(new ToolStripMenuItem(Recortar(escritorio.Nombre(id)), null, (_, _) => CambiarEscritorio(id))
            {
                Checked = id == EscritorioActualId,
            });
        if (Config.Escritorios.Count == 0)
            escritorios.DropDownItems.Add(new ToolStripMenuItem("Agregue escritorios en config.json") { Enabled = false });
        menu.Items.Add(escritorios);
        menu.Items.Add(new ToolStripSeparator());

        var pantallas = Geometria.Ordenar(Pantallas.Listar());
        var actual = PantallaDeWidgets(pantallas);
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

    /// <summary>Texto para el menú: corto, y con «&amp;» escapado (si no, el menú lo toma como atajo).</summary>
    public static string Recortar(string texto) => (texto.Length <= 70 ? texto : texto[..67] + "…").Replace("&", "&&");

    void ElegirPantalla(Pantalla pantalla, List<Pantalla> pantallas)
    {
        var descripcion = Geometria.Describir(pantalla, pantallas);
        if (EscritorioActual is { } escritorio && escritorio.Pantalla is not null)
            escritorio.Pantalla = descripcion;  // este escritorio tiene pantalla propia: se cambia esa
        else
            Config.Pantalla = descripcion;
        GuardarConfig();
        AplicarEscritorio();
    }

    void AlternarBloqueo()
    {
        Estado.Bloqueado = !Estado.Bloqueado;
        GuardarEstado();
        foreach (var ventana in widgets)
            ventana.Bloquear(Estado.Bloqueado);
    }

    /// <summary>Olvida las posiciones movidas a mano en el escritorio actual (o en el modo libre).</summary>
    void Restablecer()
    {
        var prefijo = EscritorioActualId is { } escritorio ? escritorio + "/" : "";
        foreach (var clave in Estado.Posiciones.Keys.ToList())
            if (prefijo.Length > 0 ? clave.StartsWith(prefijo, StringComparison.Ordinal) : !clave.Contains('/'))
                Estado.Posiciones.Remove(clave);
        GuardarEstado();
        AplicarEscritorio();
    }

    /// <summary>Cierra los widgets y los vuelve a crear con lo que diga config.json ahora.</summary>
    void Recargar()
    {
        CerrarWidgets();
        Config = CargarConfig();
        Estado = CargarEstado();
        AplicarEscritorio();
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
        comandos.Dispose();
        ExitThread();
    }
}
