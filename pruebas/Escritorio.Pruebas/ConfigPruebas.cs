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
        Assert.Equal(["gran-sabio", "reloj", "baterias", "reproductor"], config.Widgets.Keys);
        Assert.Equal(["trabajo", "widgets"], config.Escritorios.Keys);
        Assert.Equal("secundaria", config.Pantalla);
        Assert.Equal("no", config.Widgets["gran-sabio"].Entorno!["NAVEGADOR"]);
        Assert.Equal("tarjeta", config.Widgets["reproductor"].Opciones!["diseno"]);
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
