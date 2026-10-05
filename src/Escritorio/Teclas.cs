namespace Escritorio;

/// <summary>Atajos de teclado escritos como texto («Ctrl+Alt+P»), sin dependencias de Windows (con pruebas).</summary>
public static class Teclas
{
    // Los valores que espera RegisterHotKey.
    public const uint Alt = 1, Control = 2, Shift = 4, Win = 8;

    /// <summary>Modificadores y tecla virtual, listos para registrar.</summary>
    public readonly record struct Atajo(uint Modificadores, uint Tecla);

    static readonly Dictionary<string, uint> Modificadores = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = Control, ["control"] = Control,
        ["alt"] = Alt,
        ["shift"] = Shift, ["mayus"] = Shift, ["mayús"] = Shift,
        ["win"] = Win, ["windows"] = Win,
    };

    static readonly Dictionary<string, uint> Especiales = new(StringComparer.OrdinalIgnoreCase)
    {
        ["space"] = 0x20, ["espacio"] = 0x20, ["enter"] = 0x0D, ["intro"] = 0x0D, ["esc"] = 0x1B, ["escape"] = 0x1B, ["tab"] = 0x09,
        ["up"] = 0x26, ["arriba"] = 0x26, ["down"] = 0x28, ["abajo"] = 0x28, ["left"] = 0x25, ["izquierda"] = 0x25, ["right"] = 0x27, ["derecha"] = 0x27,
        ["home"] = 0x24, ["inicio"] = 0x24, ["end"] = 0x23, ["fin"] = 0x23, ["pageup"] = 0x21, ["repag"] = 0x21, ["pagedown"] = 0x22, ["avpag"] = 0x22,
        ["insert"] = 0x2D, ["delete"] = 0x2E, ["supr"] = 0x2E, ["backspace"] = 0x08, ["retroceso"] = 0x08,
        ["plus"] = 0xBB, ["mas"] = 0xBB, ["más"] = 0xBB, ["minus"] = 0xBD, ["menos"] = 0xBD, ["pause"] = 0x13, ["pausa"] = 0x13,
        ["mediaplay"] = 0xB3, ["medianext"] = 0xB0, ["mediaprev"] = 0xB1, ["volumeup"] = 0xAF, ["volumedown"] = 0xAE, ["mute"] = 0xAD,
    };

    /// <summary>«Ctrl+Alt+1», «win + shift + f5», «Ctrl+Espacio»… Las letras y números necesitan al menos un modificador.</summary>
    public static bool TryParse(string? texto, out Atajo atajo, out string error)
    {
        atajo = default;
        error = "";
        var partes = (texto ?? "").Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (partes.Length == 0)
        {
            error = "El atajo está vacío.";
            return false;
        }
        uint modificadores = 0;
        for (int i = 0; i < partes.Length - 1; i++)
        {
            if (!Modificadores.TryGetValue(partes[i], out var modificador))
            {
                error = $"«{partes[i]}» no es un modificador (Ctrl, Alt, Shift, Win).";
                return false;
            }
            modificadores |= modificador;
        }
        var ultima = partes[^1];
        uint tecla;
        if (ultima.Length == 1 && char.IsAsciiLetterOrDigit(ultima[0]))
            tecla = char.ToUpperInvariant(ultima[0]);
        else if (ultima.Length >= 2 && (ultima[0] is 'f' or 'F') && int.TryParse(ultima[1..], out var funcion) && funcion is >= 1 and <= 24)
            tecla = 0x70u + (uint)funcion - 1;
        else if (ultima.Length == 7 && ultima.StartsWith("numpad", StringComparison.OrdinalIgnoreCase) && char.IsAsciiDigit(ultima[6]))
            tecla = 0x60u + (uint)(ultima[6] - '0');
        else if (Especiales.TryGetValue(ultima, out var especial))
            tecla = especial;
        else
        {
            error = $"No se reconoce la tecla «{ultima}».";
            return false;
        }
        bool esFuncion = tecla is >= 0x70 and <= 0x87;
        bool esMultimedia = tecla is >= 0xAD and <= 0xB3;
        if (modificadores == 0 && !esFuncion && !esMultimedia)
        {
            error = "Un atajo necesita Ctrl, Alt, Shift o Win (salvo las teclas F y las multimedia).";
            return false;
        }
        atajo = new Atajo(modificadores, tecla);
        return true;
    }
}
