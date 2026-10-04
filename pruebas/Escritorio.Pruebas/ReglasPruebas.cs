using Escritorio;
using Xunit;

namespace Escritorio.Pruebas;

public class ReglasPruebas
{
    static readonly Pantalla Principal = new(@"\\.\DISPLAY1", new Rect(0, 0, 1920, 1080), new Rect(0, 0, 1920, 1032), true);
    static readonly Pantalla Segunda = new(@"\\.\DISPLAY2", new Rect(1920, 0, 2560, 1440), new Rect(1920, 0, 2560, 1392), false);
    static readonly Pantalla[] Todas = [Principal, Segunda];

    [Theory]
    [InlineData("Discord.exe", "Discord")]
    [InlineData("discord", "Discord")]
    [InlineData("DISCORD.EXE", "discord")]
    public void CoincideSinImportarMayusculasNiExe(string programa, string proceso) =>
        Assert.True(Reglas.Coincide(new Regla { Programa = programa }, proceso, "cualquier título"));

    [Fact]
    public void NoCoincideConOtroPrograma() =>
        Assert.False(Reglas.Coincide(new Regla { Programa = "Discord.exe" }, "Spotify", "Spotify"));

    [Fact]
    public void ReglaVaciaNoCoincide() =>
        Assert.False(Reglas.Coincide(new Regla { Programa = "" }, "", ""));

    [Fact]
    public void LaReglaConTituloGana()
    {
        var general = new Regla { Programa = "chrome.exe", Pantalla = "principal" };
        var musica = new Regla { Programa = "chrome.exe", Titulo = "YouTube Music" };
        Assert.Same(musica, Reglas.Buscar([general, musica], "chrome", "Inicio - YouTube Music"));
        Assert.Same(general, Reglas.Buscar([general, musica], "chrome", "Correo"));
    }

    [Fact]
    public void DestinoExactoEsRelativoYQuedaDentro()
    {
        var regla = new Regla { Programa = "x", X = 100, Y = 50, Ancho = 800, Alto = 600 };
        Assert.Equal(new Rect(2020, 50, 800, 600), Reglas.Destino(regla, Segunda, new Rect(0, 0, 10, 10)));
        // La misma regla en una pantalla más chica no se sale.
        var grande = new Regla { Programa = "x", X = 2000, Y = 1000, Ancho = 800, Alto = 600 };
        var r = Reglas.Destino(grande, Principal, default);
        Assert.Equal(r, Geometria.Dentro(r, Principal.Trabajo));
    }

    [Fact]
    public void SinPosicionConservaElTamanoYCentra()
    {
        var r = Reglas.Destino(new Regla { Programa = "x" }, Segunda, new Rect(10, 10, 1000, 700));
        Assert.Equal((1000, 700), (r.Ancho, r.Alto));
        Assert.Equal(Segunda.Trabajo.X + (Segunda.Trabajo.Ancho - 1000) / 2, r.X);
    }

    [Fact]
    public void LadoPegaAlBordeSinMargen()
    {
        var r = Reglas.Destino(new Regla { Programa = "x", Lado = "abajo-derecha" }, Segunda, new Rect(0, 0, 600, 400));
        Assert.Equal((Segunda.Trabajo.Derecha, Segunda.Trabajo.Abajo), (r.Derecha, r.Abajo));
    }

    [Fact]
    public void RecordarGuardaPosicionRelativaYLaPantalla()
    {
        var regla = Reglas.Recordar("Discord", "Discord", new Rect(2020, 40, 900, 700), maximizada: false, Todas);
        Assert.Equal("Discord.exe", regla.Programa);
        Assert.Equal("secundaria", regla.Pantalla);
        Assert.Equal((100, 40, 900, 700), (regla.X, regla.Y, regla.Ancho, regla.Alto));
        Assert.Null(regla.Titulo);
        // Aplicarla devuelve la ventana exactamente al mismo lugar.
        Assert.Equal(new Rect(2020, 40, 900, 700), Reglas.Destino(regla, Geometria.ElegirPantalla(Todas, regla.Pantalla), default));
    }

    [Fact]
    public void RecordarMaximizadaNoGuardaTamano()
    {
        var regla = Reglas.Recordar("Spotify", "Spotify", new Rect(-8, -8, 1936, 1048), maximizada: true, Todas);
        Assert.True(regla.Maximizar);
        Assert.False(regla.TieneRect);
        Assert.Equal("principal", regla.Pantalla);
    }

    [Fact]
    public void AppsDeLaTiendaSeDistinguenPorTitulo()
    {
        var regla = Reglas.Recordar(Reglas.ProcesoTienda, "Calculadora", new Rect(100, 100, 300, 500), false, Todas);
        Assert.Equal("Calculadora", regla.Titulo);
    }

    [Fact]
    public void GuardarReemplazaLaDelMismoPrograma()
    {
        var reglas = new List<Regla> { new() { Programa = "Discord.exe", Lado = "izquierda" }, new() { Programa = "Spotify.exe" } };
        Reglas.Guardar(reglas, new Regla { Programa = "discord", Maximizar = true });
        Assert.Equal(2, reglas.Count);
        Assert.True(reglas.Single(r => Reglas.NormalizarPrograma(r.Programa) == "discord").Maximizar);
    }
}
