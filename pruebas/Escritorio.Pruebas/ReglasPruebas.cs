using Escritorio;
using Xunit;

namespace Escritorio.Pruebas;

public class ReglasPruebas
{
    static readonly Pantalla Principal = new(@"\\.\DISPLAY1", new Rect(0, 0, 1920, 1080), new Rect(0, 0, 1920, 1032), true);
    static readonly Pantalla Segunda = new(@"\\.\DISPLAY2", new Rect(1920, 0, 2560, 1440), new Rect(1920, 0, 2560, 1392), false);
    static readonly Pantalla[] Todas = [Principal, Segunda];
    static Rect Trabajo(Pantalla p) => p.Trabajo;

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
    public void LaCapaDelEscritorioGanaALasGenerales()
    {
        var general = new Regla { Programa = "Discord.exe", Titulo = "Discord", Lado = "derecha" };
        var delEscritorio = new Regla { Programa = "Discord.exe", Lado = "izquierda" };
        List<Regla>[] capas = [[delEscritorio], [general]];
        Assert.Same(delEscritorio, Reglas.Buscar(capas, "Discord", "Discord"));
        Assert.Same(general, Reglas.Buscar([new List<Regla>(), [general]], "Discord", "Discord"));
        Assert.Null(Reglas.Buscar([new List<Regla>(), [general]], "Spotify", "Spotify"));
    }

    [Fact]
    public void DestinoExactoEsRelativoYQuedaDentro()
    {
        var regla = new Regla { Programa = "x", X = 100, Y = 50, Ancho = 800, Alto = 600 };
        Assert.Equal(new Rect(2020, 50, 800, 600), Reglas.Destino(regla, Segunda.Trabajo, new Rect(0, 0, 10, 10)));
        // La misma regla en una pantalla más chica no se sale.
        var grande = new Regla { Programa = "x", X = 2000, Y = 1000, Ancho = 800, Alto = 600 };
        var r = Reglas.Destino(grande, Principal.Trabajo, default);
        Assert.Equal(r, Geometria.Dentro(r, Principal.Trabajo));
    }

    [Fact]
    public void SinPosicionConservaElTamanoYCentra()
    {
        var r = Reglas.Destino(new Regla { Programa = "x" }, Segunda.Trabajo, new Rect(10, 10, 1000, 700));
        Assert.Equal((1000, 700), (r.Ancho, r.Alto));
        Assert.Equal(Segunda.Trabajo.X + (Segunda.Trabajo.Ancho - 1000) / 2, r.X);
    }

    [Fact]
    public void LadoPegaAlBordeSinMargen()
    {
        var r = Reglas.Destino(new Regla { Programa = "x", Lado = "abajo-derecha" }, Segunda.Trabajo, new Rect(0, 0, 600, 400));
        Assert.Equal((Segunda.Trabajo.Derecha, Segunda.Trabajo.Abajo), (r.Derecha, r.Abajo));
    }

    [Fact]
    public void PorcentajesSonDelEspacioDisponible()
    {
        var espacio = new Rect(1920, 0, 2000, 1392);  // lo que dejan los widgets
        var regla = new Regla { Programa = "x", Lado = "izquierda", Ancho = Medida.Porcentaje(100), Alto = Medida.Porcentaje(100) };
        Assert.Equal(espacio, Reglas.Destino(regla, espacio, new Rect(0, 0, 800, 600)));
        var mitad = new Regla { Programa = "x", Lado = "derecha", Ancho = Medida.Porcentaje(50) };
        var r = Reglas.Destino(mitad, espacio, new Rect(0, 0, 800, 600));
        Assert.Equal((1000, 600, espacio.Derecha), (r.Ancho, r.Alto, r.Derecha));
    }

    [Fact]
    public void SoloAnchoConservaElAlto()
    {
        var r = Reglas.Destino(new Regla { Programa = "x", Ancho = 500 }, Segunda.Trabajo, new Rect(0, 0, 800, 600));
        Assert.Equal((500, 600), (r.Ancho, r.Alto));
    }

    [Fact]
    public void RecordarGuardaPosicionRelativaYLaPantalla()
    {
        var regla = Reglas.Recordar("Discord", "Discord", new Rect(2020, 40, 900, 700), maximizada: false, Todas, Trabajo);
        Assert.Equal("Discord.exe", regla.Programa);
        Assert.Equal("secundaria", regla.Pantalla);
        Assert.Equal((100, 40), (regla.X, regla.Y));
        Assert.Equal((900, 700), (regla.Ancho!.Value.Pixeles, regla.Alto!.Value.Pixeles));
        Assert.Null(regla.Titulo);
        // Aplicarla devuelve la ventana exactamente al mismo lugar.
        var pantalla = Geometria.ElegirPantalla(Todas, regla.Pantalla);
        Assert.Equal(new Rect(2020, 40, 900, 700), Reglas.Destino(regla, pantalla.Trabajo, default));
    }

    [Fact]
    public void RecordarUsaElEspacioQueDejanLosWidgets()
    {
        // Con los widgets apilados a la derecha, el espacio empieza igual pero termina antes.
        Rect Espacio(Pantalla p) => p == Segunda ? new Rect(1920, 0, 2000, 1392) : p.Trabajo;
        var regla = Reglas.Recordar("Discord", "Discord", new Rect(1920, 0, 2000, 1392), maximizada: false, Todas, Espacio);
        Assert.Equal((0, 0), (regla.X, regla.Y));
        Assert.Equal(new Rect(1920, 0, 2000, 1392), Reglas.Destino(regla, Espacio(Segunda), default));
    }

    [Fact]
    public void RecordarMaximizadaNoGuardaTamano()
    {
        var regla = Reglas.Recordar("Spotify", "Spotify", new Rect(-8, -8, 1936, 1048), maximizada: true, Todas, Trabajo);
        Assert.True(regla.Maximizar);
        Assert.Null(regla.Ancho);
        Assert.False(regla.TienePosicion);
        Assert.Equal("principal", regla.Pantalla);
    }

    [Fact]
    public void AppsDeLaTiendaSeDistinguenPorTitulo()
    {
        var regla = Reglas.Recordar(Reglas.ProcesoTienda, "Calculadora", new Rect(100, 100, 300, 500), false, Todas, Trabajo);
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

    [Theory]
    [InlineData("60%", 2000, 1200)]
    [InlineData(" 33.3 % ", 1000, 333)]
    [InlineData("800", 2000, 800)]
    public void MedidaEnTextoSeResuelve(string texto, int total, int esperado)
    {
        Assert.True(Medida.TryParse(texto, out var medida));
        Assert.Equal(esperado, medida.Resolver(total));
    }

    [Fact]
    public void MedidaInvalidaNoSeAcepta() => Assert.False(Medida.TryParse("ancha", out _));
}
