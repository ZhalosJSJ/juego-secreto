using Escritorio;
using Xunit;

namespace Escritorio.Pruebas;

public class NavegadoresPruebas
{
    [Fact]
    public void ElEstadoSeLeeDeLaListaDeExperimentos()
    {
        Assert.Equal("por-defecto", Navegadores.Estado([]));
        Assert.Equal("por-defecto", Navegadores.Estado(["otra-cosa@1"]));
        Assert.Equal("habilitado", Navegadores.Estado(["otra-cosa@1", "hardware-media-key-handling@1"]));
        Assert.Equal("deshabilitado", Navegadores.Estado(["hardware-media-key-handling@2"]));
    }

    [Fact]
    public void HabilitarPoneLaOpcionUnaSolaVezYConservaElResto()
    {
        var lista = Navegadores.ConOpcion(["otra-cosa@1", "hardware-media-key-handling@2", "hardware-media-key-handling"], habilitar: true);
        Assert.Equal(["otra-cosa@1", "hardware-media-key-handling@1"], lista);
        Assert.Equal("habilitado", Navegadores.Estado(lista));
        // Volver a habilitar no duplica.
        Assert.Equal(lista, Navegadores.ConOpcion(lista, habilitar: true));
    }

    [Fact]
    public void QuitarLaOpcionVuelveAlValorPorDefecto()
    {
        var lista = Navegadores.ConOpcion(["hardware-media-key-handling@1", "otra-cosa@2"], habilitar: false);
        Assert.Equal(["otra-cosa@2"], lista);
        Assert.Equal("por-defecto", Navegadores.Estado(lista));
    }

    [Fact]
    public void HabilitarUnNavegadorDesconocidoAvisa() =>
        Assert.Throws<ArgumentException>(() => Navegadores.Habilitar("netscape"));
}
