using Escritorio;
using Xunit;

namespace Escritorio.Pruebas;

public class VersionesPruebas
{
    [Theory]
    [InlineData("0.4.0", "0.4.0")]
    [InlineData("0.4.0+9f3a1c2", "0.4.0")]
    [InlineData("0.4.0.0", "0.4.0")]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2", "1.2.0")]
    public void ParsearNormaliza(string texto, string esperado) => Assert.Equal(new Version(esperado), Versiones.Parsear(texto));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("anterior")]
    public void ParsearRechazaLoRaro(string? texto) => Assert.Null(Versiones.Parsear(texto));

    [Fact]
    public void UnaVersionNuevaReemplaza() =>
        Assert.Equal(Versiones.Decision.Reemplazar, Versiones.Decidir(new Version("0.5.0"), new Version("0.4.0"), mismoArchivo: false));

    [Fact]
    public void LaMismaVersionConOtroContenidoReemplaza() =>
        Assert.Equal(Versiones.Decision.Reemplazar, Versiones.Decidir(new Version("0.4.0"), new Version("0.4.0"), mismoArchivo: false));

    [Fact]
    public void ElMismoArchivoSoloAbreLaInstalada() =>
        Assert.Equal(Versiones.Decision.AbrirInstalada, Versiones.Decidir(new Version("0.4.0"), new Version("0.4.0"), mismoArchivo: true));

    [Fact]
    public void UnaVersionViejaNoDegrada() =>
        Assert.Equal(Versiones.Decision.NoDegradar, Versiones.Decidir(new Version("0.3.0"), new Version("0.4.0"), mismoArchivo: false));

    [Fact]
    public void SinVersionLegibleDecideElContenido()
    {
        Assert.Equal(Versiones.Decision.Reemplazar, Versiones.Decidir(null, new Version("0.4.0"), mismoArchivo: false));
        Assert.Equal(Versiones.Decision.AbrirInstalada, Versiones.Decidir(new Version("0.4.0"), null, mismoArchivo: true));
    }
}
