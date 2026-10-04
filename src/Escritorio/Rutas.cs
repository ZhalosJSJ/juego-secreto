namespace Escritorio;

/// <summary>
/// Todo lo de cada equipo vive en %LOCALAPPDATA%\Escritorio: configuración, posiciones,
/// registro, el perfil del navegador interno y los widgets propios. Nada de eso va al repositorio.
/// </summary>
internal static class Rutas
{
    public static readonly string Datos = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Escritorio");

    public static readonly string Config = Path.Combine(Datos, "config.json");
    public static readonly string Estado = Path.Combine(Datos, "estado.json");
    public static readonly string Registro = Path.Combine(Datos, "registro.log");
    public static readonly string WebView = Path.Combine(Datos, "webview");
    public static readonly string Integrados = Path.Combine(Datos, "integrados");
    public static readonly string WidgetsPropios = Path.Combine(Datos, "widgets");

    /// <summary>Direcciones virtuales con las que el navegador interno ve esas carpetas.</summary>
    public const string HostIntegrados = "app.escritorio";
    public const string HostPropios = "widgets.escritorio";

    public static void Crear()
    {
        Directory.CreateDirectory(Datos);
        Directory.CreateDirectory(WidgetsPropios);
    }

    /// <summary>Copia los widgets que vienen dentro del .exe (reloj, baterías) a su carpeta.</summary>
    public static void ExtraerIntegrados()
    {
        var ensamblado = typeof(Rutas).Assembly;
        foreach (var recurso in ensamblado.GetManifestResourceNames())
        {
            // Compilado en Windows, el nombre trae «\» en las subcarpetas; en Linux, «/».
            var nombre = recurso.Replace('\\', '/');
            if (!nombre.StartsWith("integrados/", StringComparison.Ordinal))
                continue;
            var destino = Path.Combine(Integrados, nombre["integrados/".Length..].Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
            using var origen = ensamblado.GetManifestResourceStream(recurso)!;
            using var archivo = File.Create(destino);
            origen.CopyTo(archivo);
        }
    }

    /// <summary>La ruta de un widget propio, solo si está dentro de la carpeta widgets.</summary>
    public static string? WidgetPropio(string? archivo)
    {
        if (string.IsNullOrWhiteSpace(archivo))
            return null;
        var carpeta = Path.GetFullPath(WidgetsPropios) + Path.DirectorySeparatorChar;
        var ruta = Path.GetFullPath(Path.Combine(carpeta, archivo));
        return ruta.StartsWith(carpeta, StringComparison.OrdinalIgnoreCase) && File.Exists(ruta) ? ruta : null;
    }
}

/// <summary>Registro en un archivo: la app no tiene consola.</summary>
internal static class Registro
{
    static readonly object Cerrojo = new();
    const long TamanoMaximo = 1_000_000;

    public static void Info(string mensaje) => Escribir("INFO", mensaje);

    public static void Error(string mensaje, Exception? error = null) =>
        Escribir("ERROR", error is null ? mensaje : $"{mensaje}: {error}");

    static void Escribir(string nivel, string mensaje)
    {
        try
        {
            lock (Cerrojo)
            {
                Directory.CreateDirectory(Rutas.Datos);
                var info = new FileInfo(Rutas.Registro);
                if (info.Exists && info.Length > TamanoMaximo)
                    File.Move(Rutas.Registro, Rutas.Registro + ".anterior", overwrite: true);
                File.AppendAllText(Rutas.Registro, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {nivel} {mensaje}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
            // Si el registro no se puede escribir, la app sigue igual.
        }
    }
}
