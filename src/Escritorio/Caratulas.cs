using System.Net.Http;
using System.Text.Json;

namespace Escritorio;

/// <summary>
/// Carátulas buscadas en Internet para cuando la app que reproduce no entrega una útil: los navegadores,
/// si la página no publica la suya, mandan su propio logo como miniatura. Se consulta iTunes Search
/// (público, sin clave) con el título y el artista; del equipo no sale nada más. Se apaga en la
/// configuración («reproductor» → «caratulasEnLinea»).
/// </summary>
internal static class Caratulas
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>Los bytes de la imagen, o nada. «Definitivo»: no vale la pena volver a buscar esta pista.</summary>
    public sealed record Resultado(byte[]? Bytes, bool Definitivo);

    public static async Task<Resultado> Buscar(string? titulo, string? artista)
    {
        var termino = Medios.TerminoDeBusqueda(titulo, artista);
        if (termino.Length == 0)
            return new(null, true);
        try
        {
            if (Http.DefaultRequestHeaders.UserAgent.Count == 0)
                Http.DefaultRequestHeaders.UserAgent.ParseAdd("Escritorio/" + Instalacion.Version);
            var direccion = "https://itunes.apple.com/search?media=music&entity=song&limit=5&term=" + Uri.EscapeDataString(termino);
            using var documento = JsonDocument.Parse(await Http.GetStringAsync(direccion));
            if (documento.RootElement.TryGetProperty("results", out var resultados) && resultados.ValueKind == JsonValueKind.Array)
            {
                foreach (var resultado in resultados.EnumerateArray())
                {
                    var arte = Texto(resultado, "artworkUrl100");
                    if (arte is null || !Medios.Coincide(termino, artista, Texto(resultado, "trackName"), Texto(resultado, "artistName")))
                        continue;
                    var bytes = await Http.GetByteArrayAsync(Medios.CaratulaGrande(arte));
                    if (bytes.Length > 0)
                        return new(bytes, true);
                }
            }
            Registro.Info($"Sin carátula en iTunes para «{termino}»");
            return new(null, true);
        }
        catch (Exception error)
        {
            Registro.Info($"No se pudo buscar la carátula de «{termino}»: {error.GetType().Name}: {error.Message}");
            return new(null, false);
        }
    }

    static string? Texto(JsonElement elemento, string nombre) =>
        elemento.TryGetProperty(nombre, out var valor) && valor.ValueKind == JsonValueKind.String ? valor.GetString() : null;
}
