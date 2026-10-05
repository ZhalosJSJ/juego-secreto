using Escritorio;
using Xunit;

namespace Escritorio.Pruebas;

public class TeclasPruebas
{
    [Theory]
    [InlineData("Ctrl+Alt+1", Teclas.Control | Teclas.Alt, 0x31u)]
    [InlineData("ctrl + alt + p", Teclas.Control | Teclas.Alt, 0x50u)]
    [InlineData("Win+Shift+F5", Teclas.Win | Teclas.Shift, 0x74u)]
    [InlineData("F12", 0u, 0x7Bu)]
    [InlineData("Ctrl+Alt+Espacio", Teclas.Control | Teclas.Alt, 0x20u)]
    [InlineData("Mayús+Alt+Derecha", Teclas.Shift | Teclas.Alt, 0x27u)]
    [InlineData("Ctrl+Numpad3", Teclas.Control, 0x63u)]
    [InlineData("MediaPlay", 0u, 0xB3u)]
    public void SeLeenLosAtajos(string texto, uint modificadores, uint tecla)
    {
        Assert.True(Teclas.TryParse(texto, out var atajo, out var error), error);
        Assert.Equal(new Teclas.Atajo(modificadores, tecla), atajo);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("P")]              // una letra sola secuestraría la tecla
    [InlineData("Ctrl+Foo")]
    [InlineData("Super+A")]
    [InlineData("Ctrl+F99")]
    public void SeRechazanLosMalFormados(string? texto)
    {
        Assert.False(Teclas.TryParse(texto, out _, out var error));
        Assert.NotEmpty(error);
    }
}
