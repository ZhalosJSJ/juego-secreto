using System.Drawing.Imaging;
using System.Text.Json;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Escritorio;

/// <summary>
/// Lo que se está escuchando, venga de donde venga (Spotify, el navegador, el reproductor de
/// Windows…): el mismo control multimedia que usa Windows para las teclas de reproducción.
/// Manda al widget «reproductor» la pista, la carátula, el progreso y los controles disponibles,
/// y ejecuta lo que el widget pida (pausar, siguiente, cambiar de app, ir a un punto).
/// </summary>
internal sealed class Reproductor : IDisposable
{
    const int LadoMaximoCaratula = 320;

    readonly Aplicacion app;
    GlobalSystemMediaTransportControlsSessionManager? gestor;
    GlobalSystemMediaTransportControlsSession? sesion;  // la que se muestra
    string? elegida;         // app pedida por el usuario; si no, la que Windows considere actual
    string? claveCaratula;   // de la última carátula enviada
    bool activo;
    bool enviando;
    bool pendiente;

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
    public void Pedir() => _ = Enviar(conCaratula: true);

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
    void AlCambiarMedios(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) => app.EnUI(() => _ = Enviar());
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
        claveCaratula = null;
        _ = Enviar(conCaratula: true);
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

    /// <summary>Arma el estado y se lo manda al widget. Si llegan avisos en ráfaga, se agrupan.</summary>
    async Task Enviar(bool conCaratula = false)
    {
        if (!activo)
            return;
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
                var json = await Armar(conCaratula);
                conCaratula = false;
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

    async Task<string?> Armar(bool conCaratula)
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
            claveCaratula = null;
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
        var clave = Medios.ClaveCaratula(propiedades?.Title, propiedades?.Artist, propiedades?.AlbumTitle);
        string? caratula = null;
        bool caratulaNueva = conCaratula || clave != claveCaratula;
        if (caratulaNueva)
        {
            caratula = await LeerCaratula(propiedades?.Thumbnail);
            claveCaratula = clave;
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

    /// <summary>La carátula como data URL JPEG, reducida: el widget no necesita más de 320 px.</summary>
    static async Task<string?> LeerCaratula(IRandomAccessStreamReference? referencia)
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
            using var original = Image.FromStream(new MemoryStream(bytes));
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
            Registro.Info($"Sin carátula: {error.GetType().Name}: {error.Message}");
            return null;
        }
    }
}
