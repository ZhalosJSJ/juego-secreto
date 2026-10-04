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
}
