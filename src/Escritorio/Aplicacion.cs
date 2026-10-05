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
    readonly Reproductor reproductor;
    readonly Mezclador mezclador;
    readonly Rendimiento rendimiento;
    readonly System.Windows.Forms.Timer atenuador = new() { Interval = 80 };
    readonly Dictionary<int, Action> accionesDeAtajo = new();
    Dictionary<string, WidgetEfectivo> efectivos = new();
    VentanaWidget? ventanaAjustes;
    bool atenuando;
    // Escritorio automático: a cuál volver cuando se cierre la ventana que lo activó.
    IntPtr ventanaDisparadora;
    bool volverPendiente;
    string? escritorioAnterior;

    public Config Config { get; private set; }
    public Estado Estado { get; private set; }
    public Icon Icono { get; }

    /// <summary>Un solo entorno de WebView2 para todos los widgets: comparten procesos y perfil.</summary>
    public Task<CoreWebView2Environment> EntornoWeb { get; }

    public Aplicacion(string? escritorioPedido = null, string? versionAnterior = null)
    {
        Rutas.Crear();
        Rutas.ExtraerIntegrados();
        Icono = CargarIcono();
        Config = CargarConfig();
        if (Config.Completar())
        {
            GuardarConfig();
            Registro.Info("Configuración puesta al día: se agregaron los widgets incluidos que faltaban");
        }
        Estado = CargarEstado();
        EntornoWeb = CoreWebView2Environment.CreateAsync(null, Rutas.WebView);
        comandos.Recibido += Ejecutar;
        comandos.Atajo += id => { if (accionesDeAtajo.TryGetValue(id, out var accion)) accion(); };
        atenuador.Tick += (_, _) => RevisarAtenuacion();

        bandeja = new NotifyIcon { Icon = Icono, Text = "Escritorio", ContextMenuStrip = menu, Visible = true };
        menu.Opening += (_, e) =>
        {
            ConstruirMenu();
            e.Cancel = false;
        };

        organizador = new Organizador(this);
        baterias = new Baterias(this);
        reproductor = new Reproductor(this);
        mezclador = new Mezclador(this);
        rendimiento = new Rendimiento(this);
        if (escritorioPedido is not null)
            Estado.Escritorio = IdEscritorio(escritorioPedido);
        AplicarEscritorio();
        organizador.Iniciar();
        RegistrarAtajos();
        atenuador.Start();
        if (escritorioPedido is not null && EscritorioActual is not null)
            organizador.AlEntrarA(EscritorioActual);
        SystemEvents.DisplaySettingsChanged += PantallasCambiaron;
        Registro.Info($"Escritorio {Instalacion.Version} iniciado");
        if (versionAnterior is not null)
            Avisar($"Escritorio se actualizó a la versión {Instalacion.Version} y quitó la anterior ({versionAnterior}).");
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

    /// <summary>Órdenes que llegan desde otra copia del programa (Escritorio.exe --escritorio …, o una actualización).</summary>
    void Ejecutar(string orden)
    {
        const string prefijo = "escritorio ";
        if (orden.StartsWith(prefijo, StringComparison.Ordinal))
            CambiarEscritorio(IdEscritorio(orden[prefijo.Length..]));
        else if (orden == "salir")
            Salir();
        else if (orden == "configuracion")
            AbrirAjustes();
    }

    // --- Escritorio automático: al abrirse un programa de «activarCon» ---

    /// <summary>El organizador vio aparecer una ventana principal (en el hilo de la interfaz).</summary>
    public void VentanaAparecio(IntPtr hwnd, string proceso)
    {
        if (string.IsNullOrEmpty(proceso))
            return;
        foreach (var (id, escritorio) in Config.Escritorios)
        {
            if (!escritorio.SeActivaCon(proceso))
                continue;
            if (id == EscritorioActualId)
            {
                if (ventanaDisparadora == IntPtr.Zero)
                    ventanaDisparadora = hwnd;
                return;
            }
            escritorioAnterior = Estado.Escritorio;
            volverPendiente = true;
            ventanaDisparadora = hwnd;
            // Fuera del aviso de Windows, con calma.
            comandos.BeginInvoke(() => CambiarEscritorio(id));
            return;
        }
    }

    /// <summary>Se cerró una ventana: si era la que activó un escritorio, se vuelve al anterior.</summary>
    public void VentanaCerrada(IntPtr hwnd)
    {
        if (hwnd != ventanaDisparadora)
            return;
        ventanaDisparadora = IntPtr.Zero;
        if (!volverPendiente)
            return;
        volverPendiente = false;
        var volverA = escritorioAnterior;
        escritorioAnterior = null;
        comandos.BeginInvoke(() => CambiarEscritorio(volverA));
    }

    // --- Atajos de teclado globales ---

    void RegistrarAtajos()
    {
        comandos.QuitarAtajos();
        accionesDeAtajo.Clear();
        int id = 1;
        void Registrar(string? texto, string nombre, Action accion)
        {
            if (string.IsNullOrWhiteSpace(texto))
                return;
            if (!Teclas.TryParse(texto, out var atajo, out var error))
            {
                Registro.Error($"Atajo «{texto}» de {nombre}: {error}");
                return;
            }
            if (!comandos.RegistrarAtajo(id, atajo))
            {
                Registro.Error($"El atajo «{texto}» de {nombre} ya lo usa otro programa");
                return;
            }
            accionesDeAtajo[id++] = accion;
        }
        foreach (var (accion, texto) in Config.Atajos)
        {
            Action? hacer = accion switch
            {
                "alternar" => () => _ = reproductor.Accion("alternar", null, 0),
                "siguiente" => () => _ = reproductor.Accion("siguiente", null, 0),
                "anterior" => () => _ = reproductor.Accion("anterior", null, 0),
                "libre" => () => CambiarEscritorio(null),
                "configuracion" => AbrirAjustes,
                _ => null,
            };
            if (hacer is null)
                Registro.Error($"Acción de atajo desconocida: «{accion}» (valen: {string.Join(", ", Config.AccionesDeAtajo)})");
            else
                Registrar(texto, $"«{accion}»", hacer);
        }
        foreach (var (idEscritorio, escritorio) in Config.Escritorios)
            Registrar(escritorio.Atajo, $"el escritorio «{escritorio.Nombre(idEscritorio)}»", () => CambiarEscritorio(idEscritorio));
    }

    // --- Atenuar al hablar: mientras se mantiene Espacio sobre el widget elegido ---

    void RevisarAtenuacion()
    {
        var ajustes = Config.Atenuar;
        var widget = ajustes.Activo ? widgets.FirstOrDefault(w => w.Id == ajustes.Widget) : null;
        bool hablando = widget is { IsDisposed: false } && Form.ActiveForm == widget && (Win32.GetAsyncKeyState(Win32.VK_SPACE) & 0x8000) != 0;
        if (hablando && !atenuando)
        {
            mezclador.Atenuar(Math.Clamp(ajustes.Nivel, 0, 1));
            atenuando = true;
        }
        else if (!hablando && atenuando)
        {
            mezclador.Restaurar();
            atenuando = false;
        }
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
        bool atajosDeMusica = Config.Atajos.Keys.Any(k => k is "alternar" or "siguiente" or "anterior");
        _ = reproductor.Activar(atajosDeMusica || widgets.Any(w => w.Ajustes.Integrado == "reproductor"));
        rendimiento.Activar(widgets.Any(w => w.Ajustes.Integrado == "rendimiento"));
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
        if (!efectivos.ContainsKey(ventana.Id))
            return;  // la ventana de configuración y similares no guardan posición
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

    /// <summary>Un widget incluido pidió datos o una acción. Llega en el hilo de la interfaz.</summary>
    public void Mensaje(VentanaWidget ventana, JsonElement mensaje)
    {
        if (Texto(mensaje, "pedir") is { } tema)
        {
            switch (tema)
            {
                case "baterias": baterias.Pedir(ventana); break;
                case "reproductor": reproductor.Pedir(); break;
                case "mezclador": mezclador.Enviar(); break;
                case "rendimiento": rendimiento.Pedir(); break;
                case "ajustes": EnviarAjustes(ventana); break;
            }
        }
        if (Texto(mensaje, "accion") is { } accion)
        {
            switch (accion)
            {
                case "abrirJson": Abrir(Rutas.Config); break;
                case "abrirCarpeta": Abrir(Rutas.WidgetsPropios); break;
                default: _ = reproductor.Accion(accion, Texto(mensaje, "sesion"), Numero(mensaje, "posicion")); break;
            }
        }
        if (mensaje.TryGetProperty("guardar", out var guardar) && guardar.ValueKind == JsonValueKind.Object)
            GuardarDesdeAjustes(ventana, guardar);
        if (mensaje.TryGetProperty("inicioAutomatico", out var inicio) && inicio.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            if ((inicio.ValueKind == JsonValueKind.True) != Instalacion.InicioAutomatico)
                Instalacion.AlternarInicioAutomatico();
        }
        if (mensaje.TryGetProperty("volumen", out var volumen) && volumen.ValueKind == JsonValueKind.Object && Texto(volumen, "app") is { } app1)
        {
            mezclador.Volumen(app1, Numero(volumen, "nivel"));
            mezclador.Enviar();
        }
        if (mensaje.TryGetProperty("silencio", out var silencio) && silencio.ValueKind == JsonValueKind.Object && Texto(silencio, "app") is { } app2)
        {
            mezclador.Silencio(app2, silencio.TryGetProperty("valor", out var valor) && valor.ValueKind == JsonValueKind.True);
            mezclador.Enviar();
        }
    }

    // --- Ventana de configuración ---

    void AbrirAjustes()
    {
        if (ventanaAjustes is { IsDisposed: false })
        {
            ventanaAjustes.Activate();
            return;
        }
        var ajustes = new AjustesWidget { Titulo = "Configuración de Escritorio", Integrado = "ajustes", Fijo = false, Ancho = 800, Alto = 640 };
        // Donde está el usuario (la pantalla del mouse), centrada.
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        var trabajo = new Rect(area.X, area.Y, area.Width, area.Height);
        var rect = Geometria.Posicionar(trabajo, Math.Min(800, area.Width - 40), Math.Min(640, area.Height - 40), "centro", 0);
        ventanaAjustes = new VentanaWidget(this, "ajustes", ajustes, rect, bloqueado: true);
        ventanaAjustes.FormClosed += (_, _) => ventanaAjustes = null;
        ventanaAjustes.Show();
        ventanaAjustes.Activate();
    }

    void EnviarAjustes(VentanaWidget ventana)
    {
        var pantallas = Geometria.Ordenar(Pantallas.Listar());
        var json = JsonSerializer.Serialize(new
        {
            tipo = "ajustes",
            config = Config,
            pantallas = pantallas.Select((p, i) => new { valor = Geometria.Describir(p, pantallas), texto = $"Pantalla {i + 1}" + (p.Principal ? " (principal)" : "") }),
            inicioAutomatico = Instalacion.InicioAutomatico,
            instalado = Instalacion.EstaInstalado,
            version = Instalacion.Version,
            incluidos = Config.PorDefecto().Widgets.Where(w => w.Value.Integrado is not null).Select(w => new { id = w.Key, integrado = w.Value.Integrado, titulo = w.Value.Titulo, plantilla = w.Value }),
            escritorioActual = EscritorioActualId,
            lados = Geometria.Lados,
            disenos = new[] { "tarjeta", "vinilo", "minimo", "portada", "neon" },
            acciones = Config.AccionesDeAtajo,
        }, Json.Opciones);
        ventana.Enviar(json);
    }

    void GuardarDesdeAjustes(VentanaWidget ventana, JsonElement guardar)
    {
        try
        {
            var nueva = JsonSerializer.Deserialize<Config>(guardar.GetRawText(), Json.Opciones) ?? throw new JsonException("La configuración llegó vacía.");
            foreach (var (accion, texto) in nueva.Atajos)
                if (!string.IsNullOrWhiteSpace(texto) && !Teclas.TryParse(texto, out _, out var error))
                    throw new JsonException($"Atajo de «{accion}»: {error}");
            foreach (var (id, escritorio) in nueva.Escritorios)
                if (!string.IsNullOrWhiteSpace(escritorio.Atajo) && !Teclas.TryParse(escritorio.Atajo, out _, out var error))
                    throw new JsonException($"Atajo del escritorio «{escritorio.Nombre(id)}»: {error}");
            nueva.Formato = Config.FormatoActual;
            Config = nueva;
            GuardarConfig();
            AplicarConfigNueva();
            ventana.Enviar(JsonSerializer.Serialize(new { tipo = "guardado", ok = true }));
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException)
        {
            Registro.Error("No se pudo guardar la configuración desde la ventana de ajustes", error);
            ventana.Enviar(JsonSerializer.Serialize(new { tipo = "guardado", ok = false, error = error.Message }));
        }
    }

    /// <summary>Tras cambiar la configuración en memoria: se rehacen los widgets y los atajos.</summary>
    void AplicarConfigNueva()
    {
        CerrarWidgets();
        AplicarEscritorio();
        RegistrarAtajos();
    }

    // --- Menú «Widgets» ---

    ToolStripMenuItem CrearMenuWidgets()
    {
        var raiz = new ToolStripMenuItem("Widgets");
        var escritorio = EscritorioActual;
        foreach (var (id, ajustes) in Config.Widgets)
        {
            var nombre = string.IsNullOrWhiteSpace(ajustes.Titulo) ? id : ajustes.Titulo!;
            raiz.DropDownItems.Add(new ToolStripMenuItem(Recortar(nombre), null, (_, _) => AlternarWidget(id)) { Checked = efectivos.ContainsKey(id) });
        }
        var faltan = Config.IncluidosQueFaltan().ToList();
        raiz.DropDownItems.Add(new ToolStripSeparator());
        if (faltan.Count > 0)
        {
            var agregar = new ToolStripMenuItem("Agregar widget incluido");
            foreach (var (id, plantilla) in faltan)
                agregar.DropDownItems.Add(plantilla.Nombre, null, (_, _) => AgregarWidget(id, plantilla));
            raiz.DropDownItems.Add(agregar);
        }
        raiz.DropDownItems.Add("Agregar widget propio…", null, (_, _) => AgregarWidgetPropio());
        if (escritorio?.Widgets is not null)
            raiz.DropDownItems.Add(new ToolStripMenuItem($"Marcados: los que se ven en «{escritorio.Nombre(EscritorioActualId!)}»") { Enabled = false });
        return raiz;
    }

    /// <summary>Muestra u oculta un widget: en un escritorio con lista propia, en esa lista; si no, en la configuración general.</summary>
    void AlternarWidget(string id)
    {
        if (!Config.Widgets.TryGetValue(id, out var ajustes))
            return;
        var escritorio = EscritorioActual;
        if (escritorio?.Widgets is not null)
        {
            if (escritorio.Widgets.TryGetValue(id, out var enEscritorio))
                enEscritorio.Visible = !enEscritorio.Visible;
            else
                escritorio.Widgets[id] = new WidgetEnEscritorio();
            if (escritorio.Widgets[id].Visible)
                ajustes.Activo = true;
        }
        else
        {
            ajustes.Activo = !ajustes.Activo;
        }
        GuardarConfig();
        AplicarEscritorio();
    }

    void AgregarWidget(string idDeseado, AjustesWidget ajustes)
    {
        var id = Config.IdLibre(idDeseado);
        Config.Widgets[id] = ajustes;
        if (EscritorioActual?.Widgets is { } lista)
            lista[id] = new WidgetEnEscritorio();
        GuardarConfig();
        AplicarEscritorio();
        Avisar($"Se agregó el widget «{ajustes.Nombre}». Ajústelo en Configuración…");
    }

    void AgregarWidgetPropio()
    {
        using var dialogo = new OpenFileDialog
        {
            Title = "Elija el index.html de su widget (dentro de la carpeta de widgets)",
            InitialDirectory = Rutas.WidgetsPropios,
            Filter = "Páginas (*.html; *.htm)|*.html;*.htm",
        };
        if (dialogo.ShowDialog() != DialogResult.OK)
            return;
        var carpeta = Path.GetFullPath(Rutas.WidgetsPropios) + Path.DirectorySeparatorChar;
        var ruta = Path.GetFullPath(dialogo.FileName);
        if (!ruta.StartsWith(carpeta, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show($"El widget tiene que estar dentro de la carpeta de widgets:\n{Rutas.WidgetsPropios}", "Escritorio", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var relativa = Path.GetRelativePath(Rutas.WidgetsPropios, ruta).Replace(Path.DirectorySeparatorChar, '/');
        var nombre = relativa.Contains('/') ? relativa[..relativa.IndexOf('/')] : Path.GetFileNameWithoutExtension(relativa);
        AgregarWidget(nombre.ToLowerInvariant(), new AjustesWidget { Titulo = nombre, Archivo = relativa, Ancho = 320, Alto = 240, Lado = "centro" });
    }

    static string? Texto(JsonElement objeto, string clave) =>
        objeto.TryGetProperty(clave, out var valor) && valor.ValueKind == JsonValueKind.String ? valor.GetString() : null;

    static double Numero(JsonElement objeto, string clave) =>
        objeto.TryGetProperty(clave, out var valor) && valor.ValueKind == JsonValueKind.Number ? valor.GetDouble() : 0;

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
        menu.Items.Add(CrearMenuWidgets());
        menu.Items.Add("Configuración…", null, (_, _) => AbrirAjustes());
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
        menu.Items.Add("Abrir config.json", null, (_, _) => Abrir(Rutas.Config));
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

    public void Salir()
    {
        SystemEvents.DisplaySettingsChanged -= PantallasCambiaron;
        atenuador.Stop();
        if (atenuando)
            mezclador.Restaurar();
        comandos.QuitarAtajos();
        ventanaAjustes?.Close();
        organizador.Dispose();
        rendimiento.Dispose();
        baterias.Dispose();
        reproductor.Dispose();
        mezclador.Dispose();
        CerrarWidgets();
        bandeja.Visible = false;
        bandeja.Dispose();
        comandos.Dispose();
        ExitThread();
    }
}
