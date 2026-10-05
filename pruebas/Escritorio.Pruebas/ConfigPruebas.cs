using System.Text.Json;
using Escritorio;
using Xunit;

namespace Escritorio.Pruebas;

public class ConfigPruebas
{
    [Fact]
    public void LaConfiguracionPorDefectoTraeLosWidgetsIncluidos()
    {
        var config = Config.PorDefecto();
        Assert.Equal(["gran-sabio", "reloj", "baterias", "reproductor", "rendimiento"], config.Widgets.Keys);
        Assert.Equal(Config.FormatoActual, config.Formato);
        Assert.Equal(["trabajo", "widgets"], config.Escritorios.Keys);
        Assert.Equal("secundaria", config.Pantalla);
        Assert.Equal("no", config.Widgets["gran-sabio"].Entorno!["NAVEGADOR"]);
        Assert.Equal("tarjeta", config.Widgets["reproductor"].Opciones!["diseno"]);
    }

    [Fact]
    public void UnaConfiguracionViejaRecibeLosWidgetsNuevosUnaSolaVez()
    {
        // Como la que creó la versión 0.3: sin reproductor ni rendimiento, y sin «formato».
        const string texto = """{ "widgets": { "reloj": { "integrado": "reloj" }, "mio": { "archivo": "mio/index.html" } } }""";
        var config = JsonSerializer.Deserialize<Config>(texto, Json.Opciones)!;
        Assert.Equal(0, config.Formato);
        Assert.Equal(["baterias", "reproductor", "rendimiento"], config.IncluidosQueFaltan().Select(f => f.Id));

        Assert.True(config.Completar());
        Assert.Equal(["reloj", "mio", "baterias", "reproductor", "rendimiento"], config.Widgets.Keys);
        Assert.Equal(Config.FormatoActual, config.Formato);
        Assert.Empty(config.IncluidosQueFaltan());

        // Si después el usuario quita uno, no se vuelve a agregar.
        config.Widgets.Remove("rendimiento");
        Assert.False(config.Completar());
        Assert.DoesNotContain("rendimiento", config.Widgets.Keys);
    }

    [Fact]
    public void IdLibreNoPisaLosExistentes()
    {
        var config = Config.PorDefecto();
        Assert.Equal("notas", config.IdLibre("notas"));
        Assert.Equal("reloj-2", config.IdLibre("reloj"));
        config.Widgets["reloj-2"] = new AjustesWidget();
        Assert.Equal("reloj-3", config.IdLibre("reloj"));
    }

    [Fact]
    public void ElEscritorioSeActivaConSusProgramas()
    {
        var escritorio = new Escritorio { ActivarCon = ["League of Legends.exe", "VALORANT-Win64-Shipping"] };
        Assert.True(escritorio.SeActivaCon("League of Legends"));
        Assert.True(escritorio.SeActivaCon("valorant-win64-shipping.exe"));
        Assert.False(escritorio.SeActivaCon("Discord"));
        Assert.False(new Escritorio().SeActivaCon("Discord"));
    }

    [Fact]
    public void LasOpcionesDelWidgetSeConservan()
    {
        const string texto = """{ "widgets": { "r": { "integrado": "reproductor", "opciones": { "diseno": "vinilo", "otra": "1" } } } }""";
        var config = JsonSerializer.Deserialize<Config>(texto, Json.Opciones)!;
        Assert.Equal(("vinilo", "1"), (config.Widgets["r"].Opciones!["diseno"], config.Widgets["r"].Opciones!["otra"]));
        Assert.Contains("\"opciones\"", JsonSerializer.Serialize(config, Json.Opciones));
    }

    [Fact]
    public void IdaYVueltaSinPerderNada()
    {
        var original = Config.PorDefecto();
        original.Organizar.Reglas.Add(new Regla { Programa = "Discord.exe", Pantalla = "2", X = 10, Y = 20, Ancho = 800, Alto = Medida.Porcentaje(50) });
        var texto = JsonSerializer.Serialize(original, Json.Opciones);
        var copia = JsonSerializer.Deserialize<Config>(texto, Json.Opciones)!;
        Assert.Equal(texto, JsonSerializer.Serialize(copia, Json.Opciones));
        Assert.Contains("\"pantalla\": 2", texto);   // un número se guarda como número
        Assert.Contains("Baterías", texto);          // sin escapar las tildes
    }

    [Fact]
    public void AceptaPantallaComoNumeroComentariosYComasFinales()
    {
        const string texto = """
            {
              // los widgets en la pantalla de más a la izquierda
              "pantalla": 1,
              "widgets": { "notas": { "archivo": "notas/index.html", "fijo": false, }, },
            }
            """;
        var config = JsonSerializer.Deserialize<Config>(texto, Json.Opciones)!;
        Assert.Equal("1", config.Pantalla);
        var notas = config.Widgets["notas"];
        Assert.False(notas.Fijo);
        Assert.True(notas.Activo);                 // valores por defecto para lo que no se escribió
        Assert.Equal(400, notas.Ancho);
        Assert.True(config.Organizar.Activo);
    }
}
