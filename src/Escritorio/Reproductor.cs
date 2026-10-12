using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text.Json;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Escritorio;

/// <summary>
/// Lo que se está escuchando, venga de donde venga (Spotify, el navegador, el reproductor de
/// Windows…): el mismo control multimedia que usa Windows para las teclas de reproducción.
/// Manda al widget «reproductor» la pista, la carátula, el progreso y los controles disponibles,
/// y ejecuta lo que el widget pida (pausar, siguiente, cambiar de app, ir a un punto).
/// La carátula es la que da la app, salvo que sea el logo de un navegador; para lo que suena en un
/// navegador se prefiere la del álbum buscada en Internet (Caratulas), si está permitido y se encuentra.
/// </summary>
internal sealed class Reproductor : IDisposable
{
    const int LadoMaximoCaratula = 320;

    readonly Aplicacion app;
    GlobalSystemMediaTransportControlsSessionManager? gestor;
    GlobalSystemMediaTransportControlsSession? sesion;  // la que se muestra
    string? elegida;            // app pedida por el usuario; si no, la que Windows considere actual
    string? caratulaEnviada;    // la última carátula mandada al widget (data URL)
    bool caratulaLeida;         // si ya se decidió la carátula de la sesión actual
    string? huellaLocal;        // hash de la última miniatura que dio la app, y cómo quedó codificada
    string? localCodificada;
    readonly Dictionary<string, string?> enLinea = new();   // pista → carátula buscada en Internet (null: no hay)
    readonly HashSet<string> buscando = new();
    bool activo;
    bool enviando;
    bool pendiente;
    bool pedirMedios;    // en el próximo envío, volver a leer la miniatura
    bool pedirCompleto;  // …y mandar la carátula aunque no haya cambiado (el widget recién abre)

    public Reproductor(Aplicacion app) => this.app = app;

    /// <summary>Solo se escucha el sistema mientras haya un widget de reproductor abierto.</summary>
    public async Task Activar(bool activo)
    {
        if (activo == this.activo)
            return;
        this.activo = activo;
        if (!activo)
        {
            Desenlazar();
            if (gestor is not null)
            {
                gestor.CurrentSessionChanged -= AlCambiarActual;
                gestor.SessionsChanged -= AlCambiarSesiones;
                gestor = null;
            }
            return;
        }
        try
        {
            gestor ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            gestor.CurrentSessionChanged += AlCambiarActual;
            gestor.SessionsChanged += AlCambiarSesiones;
            Enlazar();
        }
        catch (Exception error)
        {
            Registro.Error("No se pudo acceder al control multimedia del sistema", error);
        }
    }

    public void Dispose() => _ = Activar(false);

    /// <summary>El widget pidió el estado (al abrirse): se le manda todo, con carátula.</summary>
    public void Pedir() => _ = Enviar(completo: true);

    public async Task Accion(string accion, string? sesionId, double posicion)
    {
        try
        {
            if (accion == "elegir")
            {
                elegida = string.IsNullOrWhiteSpace(sesionId) ? null : sesionId;
                Enlazar();
                return;
            }
            if (sesion is null)
                return;
            switch (accion)
            {
                case "alternar": await sesion.TryTogglePlayPauseAsync(); break;
                case "reproducir": await sesion.TryPlayAsync(); break;
                case "pausar": await sesion.TryPauseAsync(); break;
                case "siguiente": await sesion.TrySkipNextAsync(); break;
                case "anterior": await sesion.TrySkipPreviousAsync(); break;
                case "ir": await sesion.TryChangePlaybackPositionAsync(TimeSpan.FromSeconds(Math.Max(posicion, 0)).Ticks); break;
            }
        }
        catch (Exception error)
        {
            Registro.Error($"No se pudo ejecutar «{accion}» en el reproductor", error);
        }
    }

    // --- Sesiones ---

    void AlCambiarActual(GlobalSystemMediaTransportControlsSessionManager g, CurrentSessionChangedEventArgs e) => app.EnUI(Enlazar);
    void AlCambiarSesiones(GlobalSystemMediaTransportControlsSessionManager g, SessionsChangedEventArgs e) => app.EnUI(Enlazar);
    void AlCambiarMedios(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) => app.EnUI(() => _ = Enviar(medios: true));
    void AlCambiarReproduccion(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) => app.EnUI(() => _ = Enviar());
    void AlCambiarLinea(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs e) => app.EnUI(() => _ = Enviar());

    /// <summary>Elige qué sesión mostrar (la pedida por el usuario, o la actual de Windows) y se suscribe a sus cambios.</summary>
    void Enlazar()
    {
        if (gestor is null || !activo)
            return;
        GlobalSystemMediaTransportControlsSession? nueva = null;
        try
        {
            if (elegida is not null)
                nueva = gestor.GetSessions().FirstOrDefault(s => s.SourceAppUserModelId == elegida);
            nueva ??= gestor.GetCurrentSession();
        }
        catch (Exception error)
        {
            Registro.Error("No se pudieron leer las sesiones multimedia", error);
        }
        if (ReferenceEquals(nueva, sesion) || (nueva is not null && sesion is not null && nueva.SourceAppUserModelId == sesion.SourceAppUserModelId))
        {
            _ = Enviar();
            return;
        }
        Desenlazar();
        sesion = nueva;
        if (sesion is not null)
        {
            sesion.MediaPropertiesChanged += AlCambiarMedios;
            sesion.PlaybackInfoChanged += AlCambiarReproduccion;
            sesion.TimelinePropertiesChanged += AlCambiarLinea;
        }
        caratulaLeida = false;
        caratulaEnviada = null;
        huellaLocal = localCodificada = null;
        _ = Enviar(completo: true);
    }

    void Desenlazar()
    {
        if (sesion is null)
            return;
        sesion.MediaPropertiesChanged -= AlCambiarMedios;
        sesion.PlaybackInfoChanged -= AlCambiarReproduccion;
        sesion.TimelinePropertiesChanged -= AlCambiarLinea;
        sesion = null;
    }

    // --- Envío al widget ---

    /// <summary>
    /// Arma el estado y se lo manda al widget. Si llegan avisos en ráfaga, se agrupan. «medios»: cambió
    /// la pista o su miniatura (hay que volver a leerla); «completo»: mandar la carátula aunque sea la misma.
    /// </summary>
    async Task Enviar(bool medios = false, bool completo = false)
    {
        if (!activo)
            return;
        pedirMedios |= medios || completo;
        pedirCompleto |= completo;
        if (enviando)
        {
            pendiente = true;
            return;
        }
        enviando = true;
        try
        {
            do
            {
                pendiente = false;
                bool conMedios = pedirMedios, conTodo = pedirCompleto;
                pedirMedios = pedirCompleto = false;
                var json = await Armar(conMedios, conTodo);
                if (json is not null)
                    app.EnviarA("reproductor", json);
            } while (pendiente);
        }
        catch (Exception error)
        {
            Registro.Error("No se pudo leer el estado del reproductor", error);
        }
        finally
        {
            enviando = false;
        }
    }

    async Task<string?> Armar(bool medios, bool completo)
    {
        var sesiones = new List<object>();
        var actualId = sesion?.SourceAppUserModelId;
        if (gestor is not null)
        {
            try
            {
                foreach (var s in gestor.GetSessions())
                    sesiones.Add(new { id = s.SourceAppUserModelId, nombre = Medios.NombreDeFuente(s.SourceAppUserModelId), actual = s.SourceAppUserModelId == actualId });
            }
            catch (Exception)
            {
                // Sin lista de apps no pasa nada: el widget muestra solo la actual.
            }
        }
        if (sesion is null)
        {
            caratulaLeida = false;
            caratulaEnviada = null;
            return JsonSerializer.Serialize(new { tipo = "reproductor", estado = "nada", sesiones });
        }

        var propiedades = await sesion.TryGetMediaPropertiesAsync();
        var reproduccion = sesion.GetPlaybackInfo();
        var linea = sesion.GetTimelineProperties();
        var estado = reproduccion.PlaybackStatus switch
        {
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => "reproduciendo",
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => "pausado",
            _ => "detenido",
        };
        string? caratula = null;
        bool caratulaNueva = false;
        if (medios || completo || !caratulaLeida)
        {
            var imagen = await ElegirCaratula(propiedades, sesion.SourceAppUserModelId);
            caratulaLeida = true;
            if (completo || imagen != caratulaEnviada)
            {
                caratulaEnviada = imagen;
                caratula = imagen;
                caratulaNueva = true;
            }
        }
        return JsonSerializer.Serialize(new
        {
            tipo = "reproductor",
            estado,
            fuente = Medios.NombreDeFuente(sesion.SourceAppUserModelId),
            sesiones,
            titulo = propiedades?.Title ?? "",
            artista = propiedades?.Artist ?? "",
            album = propiedades?.AlbumTitle ?? "",
            posicion = linea.Position.TotalSeconds,
            duracion = Math.Max((linea.EndTime - linea.StartTime).TotalSeconds, 0),
            actualizado = linea.LastUpdatedTime.ToUnixTimeMilliseconds(),
            controles = new
            {
                alternar = reproduccion.Controls.IsPlayPauseToggleEnabled,
                siguiente = reproduccion.Controls.IsNextEnabled,
                anterior = reproduccion.Controls.IsPreviousEnabled,
                ir = reproduccion.Controls.IsPlaybackPositionEnabled,
            },
            // Solo cuando cambia: el widget conserva la anterior si no viene.
            caratulaNueva,
            caratula,
        });
    }

    // --- Carátula ---

    /// <summary>
    /// La carátula que se muestra. La que da la app vale, salvo que sea el logo de un navegador. Para lo
    /// que suena en un navegador (YouTube) se prefiere la del álbum buscada en Internet, si está permitido
    /// y se encuentra: mientras se busca (o si no hay), queda la de la app.
    /// </summary>
    async Task<string?> ElegirCaratula(GlobalSystemMediaTransportControlsSessionMediaProperties? propiedades, string fuente)
    {
        var bytes = await LeerCaratula(propiedades?.Thumbnail);
        bool navegador = Medios.EsNavegador(fuente);
        string? local = null;
        if (bytes is not null)
        {
            var huella = Convert.ToHexString(SHA256.HashData(bytes));
            if (huella != huellaLocal)
            {
                huellaLocal = huella;
                localCodificada = Codificar(bytes, descartarLogo: navegador);
            }
            local = localCodificada;
        }
        if ((local is not null && !navegador) || !app.Config.Reproductor.CaratulasEnLinea)
            return local;
        var clave = Medios.ClaveCaratula(propiedades?.Title, propiedades?.Artist, propiedades?.AlbumTitle);
        if (enLinea.TryGetValue(clave, out var buscada))
            return buscada ?? local;
        _ = BuscarEnLinea(clave, propiedades?.Title, propiedades?.Artist);
        return local;
    }

    /// <summary>Busca la carátula en Internet; si la encuentra, la guarda y vuelve a mandar el estado.</summary>
    async Task BuscarEnLinea(string clave, string? titulo, string? artista)
    {
        if (!buscando.Add(clave))
            return;
        try
        {
            var resultado = await Caratulas.Buscar(titulo, artista);
            var datos = resultado.Bytes is null ? null : Codificar(resultado.Bytes, descartarLogo: false);
            if (resultado.Definitivo || datos is not null)
            {
                if (enLinea.Count >= 200)
                    enLinea.Clear();
                enLinea[clave] = datos;
            }
            if (datos is not null)
                await Enviar(medios: true);
        }
        finally
        {
            buscando.Remove(clave);
        }
    }

    /// <summary>Los bytes de la miniatura que da la app, si los hay.</summary>
    static async Task<byte[]?> LeerCaratula(IRandomAccessStreamReference? referencia)
    {
        if (referencia is null)
            return null;
        try
        {
            using var flujo = await referencia.OpenReadAsync();
            if (flujo.Size == 0 || flujo.Size > 20_000_000)
                return null;
            using var lector = new DataReader(flujo.GetInputStreamAt(0));
            await lector.LoadAsync((uint)flujo.Size);
            var bytes = new byte[flujo.Size];
            lector.ReadBytes(bytes);
            return bytes;
        }
        catch (Exception error)
        {
            Registro.Info($"Sin carátula: {error.GetType().Name}: {error.Message}");
            return null;
        }
    }

    /// <summary>
    /// La imagen como data URL JPEG, reducida: el widget no necesita más de 320 px. Con «descartarLogo»,
    /// una imagen con esquinas transparentes (el logo que mandan los navegadores cuando la página no
    /// publica carátula; una carátula o un fotograma son opacos) se desecha.
    /// </summary>
    static string? Codificar(byte[] bytes, bool descartarLogo)
    {
        try
        {
            using var original = Image.FromStream(new MemoryStream(bytes));
            if (descartarLogo)
            {
                bool logo = EsLogo(original);
                Registro.Info($"Miniatura del navegador: {original.Width}x{original.Height}, transparencia={Image.IsAlphaPixelFormat(original.PixelFormat)}, logo={logo}");
                if (logo)
                    return null;
            }
            int lado = Math.Max(original.Width, original.Height);
            using var reducida = lado > LadoMaximoCaratula
                ? new Bitmap(original, new System.Drawing.Size(original.Width * LadoMaximoCaratula / lado, original.Height * LadoMaximoCaratula / lado))
                : new Bitmap(original);
            using var salida = new MemoryStream();
            var jpeg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
            using var parametros = new EncoderParameters(1);
            parametros.Param[0] = new EncoderParameter(Encoder.Quality, 85L);
            reducida.Save(salida, jpeg, parametros);
            return "data:image/jpeg;base64," + Convert.ToBase64String(salida.ToArray());
        }
        catch (Exception error)
        {
            Registro.Info($"Carátula ilegible: {error.GetType().Name}: {error.Message}");
            return null;
        }
    }

    static bool EsLogo(Image imagen)
    {
        if (imagen is not Bitmap mapa || !Image.IsAlphaPixelFormat(imagen.PixelFormat))
            return false;
        int derecha = mapa.Width - 1, abajo = mapa.Height - 1, transparentes = 0;
        foreach (var (x, y) in new[] { (0, 0), (derecha, 0), (0, abajo), (derecha, abajo) })
        {
            if (mapa.GetPixel(x, y).A < 32)
                transparentes++;
        }
        return transparentes >= 2;
    }
}
