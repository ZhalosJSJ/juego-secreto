namespace Escritorio;

/// <summary>Comparación de versiones del programa, sin dependencias de Windows (con pruebas).</summary>
public static class Versiones
{
    /// <summary>«0.4.0», «0.4.0+abc123» o «0.4.0.0» → 0.4.0. Texto raro → null.</summary>
    public static Version? Parsear(string? texto)
    {
        var limpio = (texto ?? "").Split('+')[0].Trim();
        if (limpio.StartsWith('v') || limpio.StartsWith('V'))
            limpio = limpio[1..];
        if (!Version.TryParse(limpio, out var version))
            return null;
        // Se normaliza a tres partes: 0.4.0.0 y 0.4.0 son la misma versión.
        return new Version(version.Major, Math.Max(version.Minor, 0), Math.Max(version.Build, 0));
    }

    /// <summary>
    /// Qué hacer cuando se abre un .exe que no es el instalado: reemplazar la instalación,
    /// abrir la instalada tal cual (misma versión y mismo archivo), o no degradar.
    /// Si alguna versión no se puede leer, se decide por el contenido del archivo.
    /// </summary>
    public static Decision Decidir(Version? actual, Version? instalada, bool mismoArchivo)
    {
        if (mismoArchivo)
            return Decision.AbrirInstalada;
        if (actual is not null && instalada is not null && actual < instalada)
            return Decision.NoDegradar;
        return Decision.Reemplazar;
    }

    public enum Decision { Reemplazar, AbrirInstalada, NoDegradar }
}
