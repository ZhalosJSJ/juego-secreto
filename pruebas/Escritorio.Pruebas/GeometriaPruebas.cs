using Escritorio;
using Xunit;

namespace Escritorio.Pruebas;

public class GeometriaPruebas
{
    static Pantalla Crear(string nombre, int x, int y, int ancho, int alto, bool principal = false, double escala = 1.0) =>
        new(nombre, new Rect(x, y, ancho, alto), new Rect(x, y, ancho, alto - 48), principal, escala);

    static readonly Pantalla Principal = Crear(@"\\.\DISPLAY1", 0, 0, 1920, 1080, principal: true);
    static readonly Pantalla Derecha = Crear(@"\\.\DISPLAY2", 1920, 0, 2560, 1440);
    static readonly Pantalla Izquierda = Crear(@"\\.\DISPLAY3", -1280, 0, 1280, 1024);

    [Fact] public void SecundariaPorDefecto() => Assert.Equal(Derecha, Geometria.ElegirPantalla([Principal, Derecha]));
    [Fact] public void UnaSolaPantallaUsaLaPrincipal() => Assert.Equal(Principal, Geometria.ElegirPantalla([Principal]));
    [Fact] public void SecundariaALaIzquierda() => Assert.Equal(Izquierda, Geometria.ElegirPantalla([Principal, Izquierda]));
    [Fact] public void Principal_() => Assert.Equal(Principal, Geometria.ElegirPantalla([Derecha, Principal], "principal"));
    [Fact] public void NumeroInexistenteUsaLaPrincipal() => Assert.Equal(Principal, Geometria.ElegirPantalla([Principal, Derecha], "5"));
    [Fact] public void SinPantallas() => Assert.Throws<InvalidOperationException>(() => Geometria.ElegirPantalla([]));

    [Fact]
    public void NumeroCuentaDeIzquierdaADerecha()
    {
        Pantalla[] todas = [Principal, Derecha, Izquierda];
        Assert.Equal(Izquierda, Geometria.ElegirPantalla(todas, "1"));
        Assert.Equal(Principal, Geometria.ElegirPantalla(todas, "2"));
        Assert.Equal(Derecha, Geometria.ElegirPantalla(todas, "3"));
    }

    [Fact]
    public void DescribirEsElInversoDeElegir()
    {
        Pantalla[] todas = [Principal, Derecha, Izquierda];
        foreach (var pantalla in todas)
            Assert.Equal(pantalla, Geometria.ElegirPantalla(todas, Geometria.Describir(pantalla, todas)));
    }

    [Fact]
    public void DerechaATodoLoAlto()
    {
        var t = Derecha.Trabajo;
        var r = Geometria.Posicionar(t, 480, 0, "derecha", Geometria.Margen);
        Assert.Equal(480, r.Ancho);
        Assert.Equal(t.Alto - 2 * Geometria.Margen, r.Alto);
        Assert.Equal(t.Derecha - Geometria.Margen, r.Derecha);
        Assert.Equal(t.Y + Geometria.Margen, r.Y);
    }

    [Fact]
    public void LadoDesconocidoVaALaDerecha() =>
        Assert.Equal(Geometria.Posicionar(Derecha.Trabajo, 300, 200, "derecha", 12), Geometria.Posicionar(Derecha.Trabajo, 300, 200, "???", 12));

    [Fact]
    public void MasGrandeQueLaPantallaSeAchica()
    {
        var r = Geometria.Posicionar(Derecha.Trabajo, 9000, 9000, "centro", 12);
        Assert.Equal(Derecha.Trabajo.Ancho - 24, r.Ancho);
        Assert.Equal(Derecha.Trabajo.Alto - 24, r.Alto);
    }

    [Fact]
    public void DentroCorrigeLoQueSeSale()
    {
        var t = Derecha.Trabajo;
        var r = Geometria.Dentro(new Rect(-500, 5000, 300, 200), t);
        Assert.Equal((t.X, t.Abajo - 200), (r.X, r.Y));
    }

    [Fact]
    public void WidgetSeEscalaConLaPantalla()
    {
        var p = Crear("D", 0, 0, 2880, 1800, principal: true, escala: 1.5);
        var r = Geometria.UbicarWidget(p, 280, 120, "arriba-izquierda", null);
        Assert.Equal(new Rect(18, 18, 420, 180), r);
    }

    [Fact]
    public void PosicionGuardadaEsRelativaALaPantalla()
    {
        var r = Geometria.UbicarWidget(Derecha, 300, 200, "derecha", new Posicion { Dx = 100, Dy = 50 });
        Assert.Equal((Derecha.Trabajo.X + 100, Derecha.Trabajo.Y + 50), (r.X, r.Y));
    }

    [Fact]
    public void PosicionGuardadaEnPantallaMasChicaQuedaDentro()
    {
        var t = Principal.Trabajo;
        var r = Geometria.UbicarWidget(Principal, 300, 200, "derecha", new Posicion { Dx = 2400, Dy = 1300 });
        Assert.Equal(r, Geometria.Dentro(r, t));
        Assert.Equal(t.Derecha, r.Derecha);
    }

    [Fact]
    public void PantallaDeUnaVentana()
    {
        Pantalla[] todas = [Principal, Derecha];
        Assert.Equal(Derecha, Geometria.PantallaDe(new Rect(2500, 100, 800, 600), todas));
        Assert.Equal(Derecha, Geometria.PantallaDe(new Rect(9000, 100, 800, 600), todas));  // fuera de todas: la más cercana
    }
}
