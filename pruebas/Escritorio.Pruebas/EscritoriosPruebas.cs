using System.Text.Json;
using Escritorio;
using Xunit;

namespace Escritorio.Pruebas;

public class EscritoriosPruebas
{
    static readonly Rect Trabajo = new(1920, 0, 2560, 1392);
    const int M = Geometria.Margen;

    static Config ConfigDePrueba()
    {
        var config = new Config();
        config.Widgets["gran-sabio"] = new AjustesWidget { Titulo = "Gran Sabio", Url = "http://localhost:8765", Ancho = 480, Alto = 0, Lado = "derecha" };
        config.Widgets["reloj"] = new AjustesWidget { Integrado = "reloj", Ancho = 280, Alto = 120, Lado = "arriba-izquierda" };
        config.Widgets["baterias"] = new AjustesWidget { Integrado = "baterias", Ancho = 300, Alto = 240, Lado = "abajo-izquierda" };
        config.Widgets["apagado"] = new AjustesWidget { Integrado = "reloj", Activo = false };
        return config;
    }

    [Fact]
    public void SinEscritorioSeVenTodosLosActivos()
    {
        var ids = ConfigDePrueba().WidgetsDe(null).Select(w => w.Id);
        Assert.Equal(["gran-sabio", "reloj", "baterias"], ids);
    }

    [Fact]
    public void ElEscritorioEligeYOrdenaLosWidgets()
    {
        var escritorio = new Escritorio
        {
            Widgets = new()
            {
                ["reloj"] = new WidgetEnEscritorio { Lado = "arriba-derecha" },
                ["apagado"] = new WidgetEnEscritorio(),          // desactivado en general: no se muestra
                ["inexistente"] = new WidgetEnEscritorio(),      // no existe: se ignora
                ["gran-sabio"] = new WidgetEnEscritorio { Ancho = 600 },
                ["baterias"] = new WidgetEnEscritorio { Visible = false },
            },
        };
        var widgets = ConfigDePrueba().WidgetsDe(escritorio);
        Assert.Equal(["reloj", "gran-sabio"], widgets.Select(w => w.Id));
        Assert.Equal("arriba-derecha", widgets[0].Lado);
        Assert.Equal(120, widgets[0].Alto);           // lo que no se cambia se hereda
        Assert.Equal(600, widgets[1].Ancho);
        Assert.Same(ConfigDePrueba().Widgets["reloj"].Integrado, widgets[0].Ajustes.Integrado);
    }

    [Fact]
    public void ApilarALaDerechaRepartenElAltoLosDeAltoCero()
    {
        Geometria.Apilable[] widgets = [new("reloj", 280, 120), new("baterias", 300, 240), new("gran-sabio", 480, 0)];
        var pila = Geometria.Apilar(Trabajo, widgets, "derecha", M);
        var reloj = pila.Posiciones["reloj"];
        var baterias = pila.Posiciones["baterias"];
        var sabio = pila.Posiciones["gran-sabio"];

        // Todos pegados al borde derecho, uno debajo del otro, separados por el margen.
        Assert.Equal(Trabajo.Derecha - M, reloj.Derecha);
        Assert.Equal(Trabajo.Derecha - M, sabio.Derecha);
        Assert.Equal(Trabajo.Y + M, reloj.Y);
        Assert.Equal(reloj.Abajo + M, baterias.Y);
        Assert.Equal(baterias.Abajo + M, sabio.Y);
        // El de alto 0 ocupa lo que sobra hasta el margen inferior.
        Assert.Equal(Trabajo.Abajo - M, sabio.Abajo);
        Assert.Equal(480, sabio.Ancho);
        // La columna es tan ancha como el más ancho.
        Assert.Equal(new Rect(Trabajo.Derecha - M - 480, Trabajo.Y, 480, Trabajo.Alto), pila.Columna);
    }

    [Fact]
    public void ApilarALaIzquierdaAlineaAlBordeIzquierdo()
    {
        var pila = Geometria.Apilar(Trabajo, [new("a", 300, 200), new("b", 200, 200)], "izquierda", M);
        Assert.Equal(Trabajo.X + M, pila.Posiciones["a"].X);
        Assert.Equal(Trabajo.X + M, pila.Posiciones["b"].X);
    }

    [Fact]
    public void ApilarSinWidgetsNoOcupaNada()
    {
        var pila = Geometria.Apilar(Trabajo, [], "derecha", M);
        Assert.Empty(pila.Posiciones);
        Assert.Null(pila.Columna);
        Assert.Equal(Trabajo, Geometria.ZonaLibre(Trabajo, pila.Columna, "derecha", M));
    }

    [Fact]
    public void LaZonaLibreEsLoQueQuedaJuntoALaColumna()
    {
        var columna = new Rect(Trabajo.Derecha - M - 480, Trabajo.Y, 480, Trabajo.Alto);
        var libre = Geometria.ZonaLibre(Trabajo, columna, "derecha", M);
        Assert.Equal(new Rect(Trabajo.X, Trabajo.Y, Trabajo.Ancho - 480 - 2 * M, Trabajo.Alto), libre);

        var columnaIzq = new Rect(Trabajo.X + M, Trabajo.Y, 480, Trabajo.Alto);
        var libreIzq = Geometria.ZonaLibre(Trabajo, columnaIzq, "izquierda", M);
        Assert.Equal((Trabajo.X + 480 + 2 * M, Trabajo.Derecha), (libreIzq.X, libreIzq.Derecha));
    }

    [Fact]
    public void DiscordALaIzquierdaLlenaLoQueDejanLosWidgets()
    {
        // El caso del ejemplo: widgets apilados a la derecha y Discord ocupando el resto.
        var pila = Geometria.Apilar(Trabajo, [new("gran-sabio", 480, 0)], "derecha", M);
        var libre = Geometria.ZonaLibre(Trabajo, pila.Columna, "derecha", M);
        var regla = new Regla { Programa = "Discord.exe", Lado = "izquierda", Ancho = Medida.Porcentaje(100), Alto = Medida.Porcentaje(100) };
        var destino = Reglas.Destino(regla, libre, new Rect(0, 0, 800, 600));
        Assert.Equal(libre, destino);
        Assert.True(destino.Derecha <= pila.Posiciones["gran-sabio"].X - M);  // no se pisa con el widget
    }

    [Fact]
    public void WidgetsDelEscritorioSeEscribenCortos()
    {
        var config = Config.PorDefecto();
        var texto = JsonSerializer.Serialize(config, Json.Opciones);
        Assert.Contains("\"reloj\": true", texto);                 // sin cambios: true
        Assert.Contains("\"ancho\": \"100%\"", texto);               // porcentajes como texto
        var copia = JsonSerializer.Deserialize<Config>(texto, Json.Opciones)!;
        Assert.Equal(texto, JsonSerializer.Serialize(copia, Json.Opciones));
        Assert.Equal(["trabajo", "widgets"], copia.Escritorios.Keys);
        Assert.Null(copia.Escritorios["widgets"].Widgets);          // «todos»
    }

    [Fact]
    public void SeLeenLasTresFormasDeWidgetEnEscritorio()
    {
        const string texto = """
            { "escritorios": { "x": { "widgets": { "a": true, "b": false, "c": { "lado": "centro", "pantalla": 1 } } } } }
            """;
        var config = JsonSerializer.Deserialize<Config>(texto, Json.Opciones)!;
        var widgets = config.Escritorios["x"].Widgets!;
        Assert.True(widgets["a"].Visible);
        Assert.False(widgets["b"].Visible);
        Assert.Equal(("centro", "1"), (widgets["c"].Lado, widgets["c"].Pantalla));
        Assert.True(widgets["c"].Visible);
    }
}
