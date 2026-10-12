using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

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

    static readonly string[] Navegadores = ["brave", "chrome", "chromium", "msedge", "firefox", "opera", "operagx", "vivaldi"];

    /// <summary>
    /// Si la app que reproduce es un navegador («brave.exe», «Brave», «Brave.Profile1»…). Los navegadores,
    /// cuando la página no publica carátula, mandan su propio logo como miniatura; y para una canción
    /// de YouTube la carátula del álbum es mejor que un fotograma del video.
    /// </summary>
    public static bool EsNavegador(string? identificador)
    {
        var texto = (identificador ?? "").Trim();
        int signo = texto.LastIndexOf('!');
        if (signo >= 0)
            texto = texto[(signo + 1)..];
        if (texto.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            texto = texto[..^4];
        return Navegadores.Any(n => texto.StartsWith(n, StringComparison.OrdinalIgnoreCase) && (texto.Length == n.Length || texto[n.Length] == '.'));
    }

    static readonly Regex EntreParentesis = new(@"\s*[\(\[\{][^\)\]\}]*[\)\]\}]");
    static readonly Regex Espacios = new(@"\s+");
    static readonly Regex Guion = new(@"\s+[-–—]\s+");
    static readonly Regex ColaDeCanal = new(@"(\s*-\s*Topic|VEVO|\s+Official|\s+Oficial)$", RegexOptions.IgnoreCase);
    static readonly Regex TamanoDeArte = new(@"/\d+x\d+(bb)?\.(jpg|png)$", RegexOptions.IgnoreCase);

    /// <summary>
    /// Con qué buscar la carátula en Internet: título y artista sin lo que agregan los sitios de video
    /// («(Official Video)», «[Letra]», «| 4K», el «VEVO» del canal). Si el título ya trae «Artista - Canción»,
    /// se usa solo. Vacío si no hay título.
    /// </summary>
    public static string TerminoDeBusqueda(string? titulo, string? artista)
    {
        var pista = Limpiar(titulo);
        if (pista.Length == 0)
            return "";
        if (Guion.IsMatch(pista))
            return Guion.Replace(pista, " ");
        var quien = ColaDeCanal.Replace(Limpiar(artista), "").Trim();
        if (quien.Length == 0 || pista.Contains(quien, StringComparison.OrdinalIgnoreCase))
            return pista;
        return quien + " " + pista;
    }

    /// <summary>
    /// Si lo que devolvió la búsqueda es de verdad esta canción: todas las palabras del nombre de la pista
    /// encontrada tienen que estar en el término, y alguna del artista encontrado en el término o en el
    /// artista original. Sin esto, un video que no es música recibiría la carátula de cualquier canción parecida.
    /// </summary>
    public static bool Coincide(string termino, string? artista, string? pistaEncontrada, string? artistaEncontrado)
    {
        var texto = Normalizar(termino).Replace(" ", "");
        var pista = Palabras(pistaEncontrada);
        if (texto.Length == 0 || pista.Count == 0 || !pista.All(p => texto.Contains(p)))
            return false;
        var conArtista = texto + Normalizar(artista).Replace(" ", "");
        var quien = Palabras(artistaEncontrado);
        return quien.Count == 0 || quien.Any(p => conArtista.Contains(p));
    }

    /// <summary>iTunes da la carátula de 100 px; la misma dirección sirve otros tamaños.</summary>
    public static string CaratulaGrande(string url) => TamanoDeArte.Replace(url, "/400x400bb.$2");

    /// <summary>Sin paréntesis, corchetes ni lo que sigue a «|»; espacios y guiones sobrantes fuera.</summary>
    static string Limpiar(string? texto)
    {
        var s = EntreParentesis.Replace(texto ?? "", " ");
        int barra = s.IndexOf('|');
        if (barra >= 0)
            s = s[..barra];
        return Espacios.Replace(s, " ").Trim(' ', '-', '–', '—', ':');
    }

    /// <summary>Minúsculas, sin tildes y solo letras y números (lo demás, espacios).</summary>
    static string Normalizar(string? texto)
    {
        var sb = new StringBuilder();
        foreach (var c in (texto ?? "").Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }
        return sb.ToString();
    }

    /// <summary>Las palabras que importan: de tres letras o más (si no hay, de dos).</summary>
    static List<string> Palabras(string? texto)
    {
        var partes = Normalizar(Limpiar(texto)).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var largas = partes.Where(p => p.Length >= 3).ToList();
        return largas.Count > 0 ? largas : partes.Where(p => p.Length >= 2).ToList();
    }
}
