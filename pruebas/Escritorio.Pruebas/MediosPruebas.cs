using Escritorio;
using Xunit;

namespace Escritorio.Pruebas;

public class MediosPruebas
{
    [Theory]
    [InlineData("Spotify.exe", "Spotify")]
    [InlineData("spotify.exe", "Spotify")]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", "Spotify")]
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", "Reproductor multimedia")]
    [InlineData("chrome.exe", "Google Chrome")]
    [InlineData("foobar2000.exe", "foobar2000")]
    [InlineData("MiReproductor.exe", "MiReproductor")]
    [InlineData("algo", "Algo")]
    [InlineData("", "Desconocido")]
    [InlineData(null, "Desconocido")]
    public void NombreDeFuente(string? id, string esperado) => Assert.Equal(esperado, Medios.NombreDeFuente(id));

    [Fact]
    public void NombreDeProgramaPrefiereLaDescripcion()
    {
        Assert.Equal("Google Chrome", Medios.NombreDePrograma("chrome", "Google Chrome"));
        Assert.Equal("Google Chrome", Medios.NombreDePrograma("chrome", "  "));
        Assert.Equal("Discord", Medios.NombreDePrograma("Discord", null));
    }

    [Fact]
    public void ClaveCaratulaCambiaConCualquierDato()
    {
        var a = Medios.ClaveCaratula("Canción", "Artista", "Álbum");
        Assert.Equal(a, Medios.ClaveCaratula("Canción", "Artista", "Álbum"));
        Assert.NotEqual(a, Medios.ClaveCaratula("Canción", "Artista", "Otro"));
        Assert.NotEqual(a, Medios.ClaveCaratula("Canción", null, "Álbum"));
    }

    [Theory]
    [InlineData(0.5, 0.5f)]
    [InlineData(-1, 0f)]
    [InlineData(7, 1f)]
    [InlineData(double.NaN, 0f)]
    public void NivelQuedaEntreCeroYUno(double valor, float esperado) => Assert.Equal(esperado, Medios.Nivel(valor));

    [Theory]
    [InlineData("brave.exe", true)]
    [InlineData("Brave", true)]
    [InlineData("Brave.Profile1", true)]
    [InlineData("MSEdge", true)]
    [InlineData("chrome.exe", true)]
    [InlineData("firefox.exe", true)]
    [InlineData("Spotify.exe", false)]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", false)]
    [InlineData("bravesito.exe", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void EsNavegador(string? id, bool esperado) => Assert.Equal(esperado, Medios.EsNavegador(id));

    [Theory]
    [InlineData("Tití Me Preguntó", "Bad Bunny", "Bad Bunny Tití Me Preguntó")]
    [InlineData("Bad Bunny - Tití Me Preguntó (Official Video)", "BadBunnyVEVO", "Bad Bunny Tití Me Preguntó")]
    [InlineData("Shakira - Monotonía [Video Oficial] | Letra", "Shakira", "Shakira Monotonía")]
    [InlineData("Monotonía (feat. Ozuna)", "Shakira - Topic", "Shakira Monotonía")]
    [InlineData("Shakira: Monotonía", "Shakira", "Shakira: Monotonía")]
    [InlineData("Canción", "", "Canción")]
    [InlineData("Canción", null, "Canción")]
    [InlineData("  ", "Artista", "")]
    [InlineData(null, "Artista", "")]
    [InlineData("(Official Video)", "Artista", "")]
    public void TerminoDeBusqueda(string? titulo, string? artista, string esperado) =>
        Assert.Equal(esperado, Medios.TerminoDeBusqueda(titulo, artista));

    [Fact]
    public void CoincideConLaPistaYElArtista()
    {
        Assert.True(Medios.Coincide("Bad Bunny Tití Me Preguntó", "BadBunnyVEVO", "Tití Me Preguntó", "Bad Bunny"));
        Assert.True(Medios.Coincide("Shakira Hips Don't Lie", "Shakira", "Hips Don't Lie (feat. Wyclef Jean)", "Shakira"));
        // El artista solo venía en el campo del artista, no en el término.
        Assert.True(Medios.Coincide("Monotonía Remix", "Shakira", "Monotonía", "Shakira & Ozuna"));
        // Otra versión de la canción: tiene palabras que no se pidieron.
        Assert.False(Medios.Coincide("Bad Bunny Tití Me Preguntó", "Bad Bunny", "Tití Me Preguntó - Live", "Bad Bunny"));
        // Un video que no es música no recibe la carátula de una canción con una palabra en común.
        Assert.False(Medios.Coincide("Cómo cocinar pasta", "Canal de cocina", "Pasta", "Chef Equis"));
        Assert.False(Medios.Coincide("", "Artista", "Canción", "Artista"));
        Assert.False(Medios.Coincide("Canción", "Artista", "", "Artista"));
    }

    [Theory]
    [InlineData("https://is1-ssl.mzstatic.com/image/thumb/Music/x/100x100bb.jpg", "https://is1-ssl.mzstatic.com/image/thumb/Music/x/400x400bb.jpg")]
    [InlineData("https://ejemplo.test/arte/60x60.png", "https://ejemplo.test/arte/400x400bb.png")]
    [InlineData("https://ejemplo.test/arte/portada.jpg", "https://ejemplo.test/arte/portada.jpg")]
    public void CaratulaGrande(string url, string esperado) => Assert.Equal(esperado, Medios.CaratulaGrande(url));
}
