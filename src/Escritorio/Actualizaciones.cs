using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace Escritorio;

/// <summary>
/// Busca versiones nuevas en GitHub Releases. Si hay una, avisa con un globo (o pregunta, si se
/// buscó a mano), la descarga y la abre: el .exe nuevo reemplaza al instalado solo (Instalacion).
/// </summary>
internal sealed class Actualizaciones : IDisposable
{
    static readonly TimeSpan EntreBusquedas = TimeSpan.FromHours(20);
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };

    readonly Aplicacion app;
    readonly System.Windows.Forms.Timer reloj = new() { Interval = (int)TimeSpan.FromHours(6).TotalMilliseconds };
    string? enlacePendiente;
    string? versionPendiente;
    bool buscando;

    public Actualizaciones(Aplicacion app)
    {
        this.app = app;
        if (Http.DefaultRequestHeaders.UserAgent.Count == 0)
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("Escritorio/" + Instalacion.Version);
        reloj.Tick += (_, _) => _ = Revisar(manual: false);
    }

    /// <summary>Busca a los dos minutos de arrancar y después cada tanto (como mucho una vez al día).</summary>
    public void Iniciar()
    {
        reloj.Start();
        _ = RevisarPronto();
    }

    public void Dispose() => reloj.Dispose();

    async Task RevisarPronto()
    {
        await Task.Delay(TimeSpan.FromMinutes(2));
        await Revisar(manual: false);
    }

    public async Task Revisar(bool manual)
    {
        var ajustes = app.Config.Actualizaciones;
        if (buscando)
            return;
        if (!manual)
        {
            bool reciente = app.Estado.UltimaBusqueda is { } ultima && DateTime.UtcNow - ultima < EntreBusquedas;
            // Las copias sin instalar (compilaciones de prueba) no buscan solas.
            if (!ajustes.Automatico || !Instalacion.EstaInstalado || reciente)
                return;
        }
        buscando = true;
        try
        {
            var texto = await Http.GetStringAsync($"https://api.github.com/repos/{ajustes.Repositorio}/releases/latest");
            using var documento = JsonDocument.Parse(texto);
            var raiz = documento.RootElement;
            var nueva = Versiones.Parsear(raiz.TryGetProperty("tag_name", out var etiqueta) ? etiqueta.GetString() : null);
            var actual = Versiones.Parsear(Instalacion.Version);
            string? enlace = null;
            if (raiz.TryGetProperty("assets", out var activos) && activos.ValueKind == JsonValueKind.Array)
                foreach (var activo in activos.EnumerateArray())
                {
                    var nombre = activo.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    if (nombre.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && activo.TryGetProperty("browser_download_url", out var url))
                    {
                        enlace = url.GetString();
                        break;
                    }
                }
            app.Estado.UltimaBusqueda = DateTime.UtcNow;
            app.GuardarEstado();
            if (nueva is null || actual is null || nueva <= actual || enlace is null)
            {
                if (manual)
                    app.Avisar($"Ya tiene la última versión ({Instalacion.Version}).");
                return;
            }
            enlacePendiente = enlace;
            versionPendiente = nueva.ToString();
            Registro.Info($"Hay una versión nueva: {nueva} (instalada: {actual})");
            if (manual)
            {
                var respuesta = MessageBox.Show(
                    $"Hay una versión nueva de Escritorio: {nueva} (tiene la {actual}).\n\n¿Descargarla e instalarla ahora? La app se reinicia sola.",
                    "Escritorio", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (respuesta == DialogResult.Yes)
                    await Instalar();
            }
            else
            {
                app.AvisarConAccion($"Hay una versión nueva de Escritorio ({nueva}). Haga clic aquí para instalarla.", () => _ = Instalar());
            }
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            Registro.Error("No se pudo buscar actualizaciones", error);
            if (manual)
                app.Avisar("No se pudo consultar GitHub. Revise la conexión a internet.");
        }
        finally
        {
            buscando = false;
        }
    }

    async Task Instalar()
    {
        if (enlacePendiente is null)
            return;
        try
        {
            var carpeta = Path.Combine(Rutas.Datos, "descargas");
            Directory.CreateDirectory(carpeta);
            foreach (var viejo in Directory.EnumerateFiles(carpeta, "*.exe"))
            {
                try
                {
                    File.Delete(viejo);
                }
                catch (IOException)
                {
                }
            }
            var destino = Path.Combine(carpeta, $"Escritorio-{versionPendiente}.exe");
            app.Avisar($"Descargando la versión {versionPendiente}…");
            var bytes = await Http.GetByteArrayAsync(enlacePendiente);
            await File.WriteAllBytesAsync(destino, bytes);
            Registro.Info($"Actualización descargada en {destino}; se abre para que se instale");
            // El .exe nuevo cierra esta copia, reemplaza la instalada y la vuelve a abrir.
            Process.Start(new ProcessStartInfo(destino) { UseShellExecute = false, WorkingDirectory = carpeta });
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            Registro.Error("No se pudo descargar la actualización", error);
            app.Avisar("No se pudo descargar la actualización. Puede bajarla desde GitHub.");
        }
    }
}
