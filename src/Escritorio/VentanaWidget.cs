using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Escritorio;

/// <summary>
/// Un widget: una ventana con WebView2 que muestra una página. Si es «fijo», no tiene marco,
/// no aparece en la barra de tareas ni en Alt+Tab, y vuelve detrás de las demás ventanas
/// cuando deja de usarse. Se arrastra desde una franja delgada en su borde superior.
/// </summary>
internal sealed class VentanaWidget : Form
{
    const int AltoAsa = 8;
    static readonly Color Tinta = Color.FromArgb(0xCF, 0xE3, 0xFF);
    static readonly Color Luz = Color.FromArgb(0x86, 0xE6, 0xFF);

    readonly Aplicacion app;
    readonly bool fijo;
    readonly WebView2 web;
    readonly Panel asa;
    readonly Panel aviso;
    readonly Label textoAviso;
    readonly Button botonReintentar;
    readonly Button botonBuscar;
    readonly Color colorAsa;
    readonly Color colorAsaEncima;
    readonly Rect inicial;
    bool activa;
    bool conectando;
    Uri? direccion;

    public string Id { get; }
    public AjustesWidget Ajustes { get; }

    public VentanaWidget(Aplicacion app, string id, AjustesWidget ajustes, Rect rect, bool bloqueado)
    {
        this.app = app;
        Id = id;
        Ajustes = ajustes;
        fijo = ajustes.Fijo;
        var fondo = LeerColor(ajustes.Fondo);
        colorAsa = Mezclar(fondo, Color.White, 0.06);
        colorAsaEncima = Mezclar(fondo, Luz, 0.45);

        Text = ajustes.Nombre;
        Icon = app.Icono;
        AutoScaleMode = AutoScaleMode.None;  // el tamaño ya viene calculado para la pantalla
        StartPosition = FormStartPosition.Manual;
        inicial = rect;
        Bounds = new Rectangle(rect.X, rect.Y, rect.Ancho, rect.Alto);
        FormBorderStyle = fijo ? FormBorderStyle.None : FormBorderStyle.Sizable;
        ShowInTaskbar = !fijo;
        MinimumSize = new Size(120, 60);
        BackColor = fondo;

        web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = fondo, Visible = false };

        textoAviso = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Tinta,
            Font = new Font("Segoe UI", 10.5f),
        };
        botonReintentar = CrearBoton("Reintentar", (_, _) => _ = Cargar());
        botonBuscar = CrearBoton("Buscar iniciar.bat…", (_, _) => BuscarIniciar());
        var botones = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.None, WrapContents = false };
        botones.Controls.AddRange([botonReintentar, botonBuscar]);
        var tabla = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(16) };
        tabla.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        tabla.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tabla.Controls.Add(textoAviso, 0, 0);
        tabla.Controls.Add(botones, 0, 1);
        aviso = new Panel { Dock = DockStyle.Fill, BackColor = fondo, Visible = false };
        aviso.Controls.Add(tabla);

        asa = new Panel { Dock = DockStyle.Top, Height = AltoAsa, BackColor = colorAsa, Cursor = Cursors.SizeAll, Visible = !bloqueado };
        asa.MouseDown += Arrastrar;
        asa.MouseEnter += (_, _) => asa.BackColor = colorAsaEncima;
        asa.MouseLeave += (_, _) => asa.BackColor = colorAsa;

        // El que se agrega último se acomoda primero: la franja arriba y el resto debajo.
        Controls.Add(web);
        Controls.Add(aviso);
        Controls.Add(asa);
    }

    // --- Comportamiento de widget fijo ---

    protected override CreateParams CreateParams
    {
        get
        {
            var parametros = base.CreateParams;
            if (fijo)
            {
                parametros.ExStyle |= Win32.WS_EX_TOOLWINDOW;   // fuera de la barra de tareas y de Alt+Tab
                parametros.ExStyle &= ~Win32.WS_EX_APPWINDOW;
            }
            return parametros;
        }
    }

    /// <summary>Al abrirse no le quita el foco a lo que esté usando.</summary>
    protected override bool ShowWithoutActivation => true;

    protected override void WndProc(ref Message m)
    {
        // Mientras no se esté usando, cualquier intento de subirla la deja al fondo.
        if (fijo && !activa && m.Msg == Win32.WM_WINDOWPOSCHANGING)
        {
            var posicion = Marshal.PtrToStructure<Win32.WINDOWPOS>(m.LParam);
            if ((posicion.flags & Win32.SWP_NOZORDER) == 0)
            {
                posicion.hwndInsertAfter = Win32.HWND_BOTTOM;
                Marshal.StructureToPtr(posicion, m.LParam, false);
            }
        }
        base.WndProc(ref m);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (fijo)
            Win32.RedondearEsquinas(Handle);
        asa.Height = LogicalToDeviceUnits(AltoAsa);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        asa.Height = LogicalToDeviceUnits(AltoAsa);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // Si se creó en una pantalla con otra escala, WinForms pudo reescalarla: se deja como se calculó.
        Colocar(inicial);
        if (fijo)
            AlFondo();
    }

    protected override void OnActivated(EventArgs e)
    {
        activa = true;
        if (fijo)
            Win32.SetWindowPos(Handle, Win32.HWND_TOP, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
        base.OnActivated(e);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        activa = false;
        if (fijo)
            AlFondo();
        base.OnDeactivate(e);
    }

    /// <summary>Se dispara al soltar la ventana después de moverla o cambiarle el tamaño.</summary>
    protected override void OnResizeEnd(EventArgs e)
    {
        base.OnResizeEnd(e);
        app.PosicionCambiada(this);
    }

    void AlFondo() =>
        Win32.SetWindowPos(Handle, Win32.HWND_BOTTOM, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);

    void Arrastrar(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;
        Win32.ReleaseCapture();
        Win32.SendMessage(Handle, Win32.WM_NCLBUTTONDOWN, Win32.HTCAPTION, IntPtr.Zero);
    }

    public void Bloquear(bool bloqueado) => asa.Visible = !bloqueado;

    public void Colocar(Rect rect)
    {
        var destino = new Rectangle(rect.X, rect.Y, rect.Ancho, rect.Alto);
        Bounds = destino;
        // Si pasó a una pantalla con otra escala, Windows le sugiere otro tamaño: se insiste.
        if (Bounds != destino)
            Bounds = destino;
    }

    // --- Contenido ---

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        try
        {
            await web.EnsureCoreWebView2Async(await app.EntornoWeb);
            var core = web.CoreWebView2;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.SetVirtualHostNameToFolderMapping(Rutas.HostIntegrados, Rutas.Integrados, CoreWebView2HostResourceAccessKind.Allow);
            core.SetVirtualHostNameToFolderMapping(Rutas.HostPropios, Rutas.WidgetsPropios, CoreWebView2HostResourceAccessKind.Allow);
            core.PermissionRequested += PermisoPedido;
            core.WebMessageReceived += MensajeRecibido;
            core.NewWindowRequested += VentanaNuevaPedida;
            core.NavigationCompleted += NavegacionTerminada;
            core.ProcessFailed += (_, a) =>
            {
                Registro.Error($"El navegador del widget «{Id}» falló ({a.ProcessFailedKind})");
                if (a.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited)
                    _ = Cargar();
            };
            await Cargar();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MostrarAviso("Falta WebView2, el componente de Windows que dibuja los widgets. Instálelo desde Windows Update.");
        }
        catch (Exception error)
        {
            Registro.Error($"No se pudo abrir el widget «{Id}»", error);
            MostrarAviso($"No se pudo abrir el widget: {error.Message}", reintentar: true);
        }
    }

    public async Task Cargar()
    {
        if (IsDisposed || web.CoreWebView2 is null)
            return;
        if (!string.IsNullOrWhiteSpace(Ajustes.Integrado))
        {
            Mostrar(new Uri($"https://{Rutas.HostIntegrados}/{Uri.EscapeDataString(Ajustes.Integrado.Trim())}/index.html"));
        }
        else if (!string.IsNullOrWhiteSpace(Ajustes.Archivo))
        {
            var ruta = Rutas.WidgetPropio(Ajustes.Archivo);
            if (ruta is null)
            {
                MostrarAviso($"No se encontró «{Ajustes.Archivo}» en la carpeta de widgets.", reintentar: true);
                return;
            }
            var partes = Path.GetRelativePath(Rutas.WidgetsPropios, ruta).Split(Path.DirectorySeparatorChar);
            Mostrar(new Uri($"https://{Rutas.HostPropios}/{string.Join('/', partes.Select(Uri.EscapeDataString))}"));
        }
        else if (Uri.TryCreate(Ajustes.Url, UriKind.Absolute, out var url) && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps))
        {
            await Conectar(url);
        }
        else
        {
            MostrarAviso("Este widget no tiene «url», «integrado» ni «archivo» en la configuración.");
        }
    }

    /// <summary>Espera a que la dirección responda; si no responde, inicia su programa (si lo conoce).</summary>
    async Task Conectar(Uri url)
    {
        if (conectando)
            return;
        conectando = true;
        try
        {
            direccion = url;
            if (!await Responde(url))
            {
                bool sabeIniciar = !string.IsNullOrWhiteSpace(Ajustes.Iniciar);
                if (sabeIniciar)
                {
                    MostrarAviso($"Iniciando {Ajustes.Nombre}…");
                    app.Lanzar(Ajustes);
                }
                else
                {
                    MostrarAviso($"Esperando a {Ajustes.Nombre}…\n\nÁbralo como siempre, o indique dónde está su iniciar.bat para que se abra solo.", buscar: true);
                }
                var limite = DateTime.UtcNow.AddSeconds(90);
                while (!await Responde(url))
                {
                    if (IsDisposed)
                        return;
                    if (sabeIniciar && DateTime.UtcNow > limite)
                    {
                        MostrarAviso($"{Ajustes.Nombre} no responde en {url}.", reintentar: true, buscar: true);
                        return;
                    }
                    await Task.Delay(sabeIniciar ? 1000 : 3000);
                }
            }
            if (!IsDisposed)
                Mostrar(url);
        }
        finally
        {
            conectando = false;
        }
    }

    static async Task<bool> Responde(Uri url)
    {
        try
        {
            using var cliente = new TcpClient();
            using var limite = new CancellationTokenSource(TimeSpan.FromMilliseconds(800));
            await cliente.ConnectAsync(url.Host, url.Port, limite.Token);
            return true;
        }
        catch (Exception error) when (error is SocketException or OperationCanceledException)
        {
            return false;
        }
    }

    void BuscarIniciar()
    {
        using var dialogo = new OpenFileDialog
        {
            Title = $"¿Dónde está el iniciar.bat de {Ajustes.Nombre}?",
            Filter = "Programas y scripts (*.bat; *.cmd; *.exe)|*.bat;*.cmd;*.exe|Todos los archivos|*.*",
        };
        if (dialogo.ShowDialog(this) != DialogResult.OK)
            return;
        Ajustes.Iniciar = dialogo.FileName;
        app.GuardarConfig();
        if (conectando)
        {
            // Ya está esperando a que responda: basta con iniciarlo.
            MostrarAviso($"Iniciando {Ajustes.Nombre}…");
            app.Lanzar(Ajustes);
        }
        else
        {
            _ = Cargar();
        }
    }

    void Mostrar(Uri destino)
    {
        direccion = destino;
        aviso.Visible = false;
        web.Visible = true;
        web.CoreWebView2.Navigate(destino.AbsoluteUri);
    }

    void MostrarAviso(string texto, bool reintentar = false, bool buscar = false)
    {
        if (IsDisposed)
            return;
        textoAviso.Text = texto;
        botonReintentar.Visible = reintentar;
        botonBuscar.Visible = buscar;
        web.Visible = false;
        aviso.Visible = true;
    }

    // --- Eventos del navegador interno ---

    async void NavegacionTerminada(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        // Si el servidor del widget se apagó, se vuelve a esperar (e iniciar) en lugar de mostrar un error.
        bool sinServidor = e.WebErrorStatus is CoreWebView2WebErrorStatus.CannotConnect
            or CoreWebView2WebErrorStatus.ServerUnreachable or CoreWebView2WebErrorStatus.ConnectionAborted;
        if (e.IsSuccess || !sinServidor || string.IsNullOrWhiteSpace(Ajustes.Url))
            return;
        // Una pausa: si el puerto responde pero la página falla, no se reintenta sin parar.
        await Task.Delay(2000);
        if (!IsDisposed)
            await Cargar();
    }

    /// <summary>El micrófono se concede solo a la dirección del propio widget, y solo si es local (como el Gran Sabio).</summary>
    void PermisoPedido(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        if (e.PermissionKind != CoreWebView2PermissionKind.Microphone || direccion is null || !direccion.IsLoopback)
            return;
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var origen)
            && Uri.Compare(origen, direccion, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0)
        {
            e.State = CoreWebView2PermissionState.Allow;
            e.SavesInProfile = true;
        }
    }

    /// <summary>Solo los widgets incluidos en la app pueden pedirle datos (p. ej. las baterías).</summary>
    void MensajeRecibido(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!e.Source.StartsWith($"https://{Rutas.HostIntegrados}/", StringComparison.OrdinalIgnoreCase))
            return;
        try
        {
            using var mensaje = JsonDocument.Parse(e.WebMessageAsJson);
            if (mensaje.RootElement.ValueKind == JsonValueKind.Object
                && mensaje.RootElement.TryGetProperty("pedir", out var pedido)
                && pedido.ValueKind == JsonValueKind.String)
                app.Pedido(this, pedido.GetString()!);
        }
        catch (JsonException)
        {
        }
    }

    /// <summary>Los enlaces que abren otra ventana van al navegador de siempre.</summary>
    void VentanaNuevaPedida(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var enlace) && (enlace.Scheme == Uri.UriSchemeHttp || enlace.Scheme == Uri.UriSchemeHttps))
            Process.Start(new ProcessStartInfo(enlace.AbsoluteUri) { UseShellExecute = true });
    }

    /// <summary>Envía datos a la página, solo si es un widget incluido en la app.</summary>
    public void Enviar(string json)
    {
        if (!IsDisposed && web.CoreWebView2 is not null && direccion?.Host == Rutas.HostIntegrados)
            web.CoreWebView2.PostWebMessageAsJson(json);
    }

    // --- Ayudas ---

    Button CrearBoton(string texto, EventHandler alPulsar)
    {
        var boton = new Button
        {
            Text = texto,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0x12, 0x23, 0x3D),
            ForeColor = Tinta,
            Font = new Font("Segoe UI", 9.5f),
            Margin = new Padding(6),
            Padding = new Padding(8, 2, 8, 2),
            Cursor = Cursors.Hand,
            Visible = false,
        };
        boton.FlatAppearance.BorderColor = Color.FromArgb(0x2B, 0x4A, 0x72);
        boton.Click += alPulsar;
        return boton;
    }

    static Color LeerColor(string? texto)
    {
        try
        {
            return string.IsNullOrWhiteSpace(texto) ? Color.FromArgb(0x0B, 0x12, 0x20) : ColorTranslator.FromHtml(texto);
        }
        catch (Exception error) when (error is ArgumentException or FormatException)
        {
            return Color.FromArgb(0x0B, 0x12, 0x20);
        }
    }

    static Color Mezclar(Color a, Color b, double cuanto) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * cuanto),
        (int)(a.G + (b.G - a.G) * cuanto),
        (int)(a.B + (b.B - a.B) * cuanto));
}
