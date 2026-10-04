namespace Escritorio;

/// <summary>Ayudas del reproductor y del mezclador que no dependen de Windows (con pruebas).</summary>
public static class Medios
{
    static readonly Dictionary<string, string> Conocidos = new(StringComparer.OrdinalIgnoreCase)
    {
        ["spotify"] = "Spotify",
        ["chrome"] = "Google Chrome",
        ["msedge"] = "Microsoft Edge",
        ["firefox"] = "Firefox",
        ["brave"] = "Brave",
        ["opera"] = "Opera",
        ["vlc"] = "VLC",
        ["zunemusic"] = "Reproductor multimedia",
        ["zunevideo"] = "Películas y TV",
        ["windowsmediaplayer"] = "Reproductor de Windows Media",
        ["foobar2000"] = "foobar2000",
        ["musicbee"] = "MusicBee",
        ["itunes"] = "iTunes",
        ["applemusic"] = "Apple Music",
        ["youtube music"] = "YouTube Music",
        ["tidal"] = "TIDAL",
        ["deezer"] = "Deezer",
        ["amazon music"] = "Amazon Music",
        ["discord"] = "Discord",
        ["steam"] = "Steam",
        ["sistema"] = "Sonidos del sistema",
    };

    /// <summary>
    /// Nombre legible de la app que reproduce, a partir de su identificador:
    /// «Spotify.exe» → «Spotify»; «SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify» → «Spotify»;
    /// «Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic» → «Reproductor multimedia».
    /// </summary>
    public static string NombreDeFuente(string? identificador)
    {
        var texto = (identificador ?? "").Trim();
        if (texto.Length == 0)
            return "Desconocido";
        // Apps de la Tienda: lo que importa está después del «!».
        int signo = texto.LastIndexOf('!');
        if (signo >= 0)
            texto = texto[(signo + 1)..];
        // Y lo que haya después del último punto («Microsoft.ZuneMusic» → «ZuneMusic»).
        int punto = texto.LastIndexOf('.');
        if (punto >= 0 && !texto.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            texto = texto[(punto + 1)..];
        if (texto.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            texto = texto[..^4];
        if (texto.Length == 0)
            return "Desconocido";
        return Conocidos.TryGetValue(texto, out var conocido) ? conocido : char.ToUpperInvariant(texto[0]) + texto[1..];
    }

    /// <summary>Nombre para mostrar de un programa en el mezclador: su descripción, o su nombre de proceso arreglado.</summary>
    public static string NombreDePrograma(string proceso, string? descripcion)
    {
        if (!string.IsNullOrWhiteSpace(descripcion))
            return descripcion.Trim();
        return NombreDeFuente(proceso);
    }

    /// <summary>Clave con la que se decide si la carátula cambió (para no reenviarla en cada aviso).</summary>
    public static string ClaveCaratula(string? titulo, string? artista, string? album) =>
        string.Join('|', titulo ?? "", artista ?? "", album ?? "");

    /// <summary>Un nivel de volumen válido: entre 0 y 1.</summary>
    public static float Nivel(double valor) => (float)Math.Clamp(double.IsNaN(valor) ? 0 : valor, 0, 1);
}
