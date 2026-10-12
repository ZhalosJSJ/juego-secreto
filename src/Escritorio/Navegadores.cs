using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Escritorio;

/// <summary>
/// Los navegadores basados en Chromium publican lo que suena en los controles multimedia de Windows
/// (y así lo ve el reproductor) solo con la opción «Hardware Media Key Handling» activa; Brave la trae
/// apagada. Se activa igual que desde chrome://flags: anotándola en el archivo «Local State» del
/// navegador, cosa que solo se puede hacer con el navegador cerrado (al cerrarse lo reescribe).
/// </summary>
public static class Navegadores
{
    public const string Opcion = "hardware-media-key-handling";
    const string Habilitada = Opcion + "@1";   // en chrome://flags, @1 es «Enabled» y @2 «Disabled»
    const string Deshabilitada = Opcion + "@2";

    public sealed record Navegador(string Id, string Nombre, string Proceso, string LocalState);

    /// <summary>Estado: «habilitado», «deshabilitado» o «por-defecto».</summary>
    public sealed record EstadoNavegador(string Id, string Nombre, string Estado, bool Abierto);

    static readonly string Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    static readonly string Roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    public static readonly Navegador[] Conocidos =
    [
        new("brave", "Brave", "brave", Path.Combine(Local, "BraveSoftware", "Brave-Browser", "User Data", "Local State")),
        new("chrome", "Google Chrome", "chrome", Path.Combine(Local, "Google", "Chrome", "User Data", "Local State")),
        new("edge", "Microsoft Edge", "msedge", Path.Combine(Local, "Microsoft", "Edge", "User Data", "Local State")),
        new("vivaldi", "Vivaldi", "vivaldi", Path.Combine(Local, "Vivaldi", "User Data", "Local State")),
        new("operagx", "Opera GX", "opera", Path.Combine(Roaming, "Opera Software", "Opera GX Stable", "Local State")),
        new("opera", "Opera", "opera", Path.Combine(Roaming, "Opera Software", "Opera Stable", "Local State")),
    ];

    /// <summary>Los navegadores presentes en este equipo, con el estado de la opción.</summary>
    public static List<EstadoNavegador> Instalados() =>
        Conocidos.Where(n => File.Exists(n.LocalState))
                 .Select(n => new EstadoNavegador(n.Id, n.Nombre, Estado(LeerExperimentos(n.LocalState)), EstaAbierto(n)))
                 .ToList();

    public static string Estado(IEnumerable<string> experimentos)
    {
        var lista = experimentos.ToList();
        if (lista.Contains(Habilitada))
            return "habilitado";
        return lista.Contains(Deshabilitada) ? "deshabilitado" : "por-defecto";
    }

    /// <summary>La lista de experimentos con la opción en «Enabled» (o sin ella, para volver al valor por defecto).</summary>
    public static List<string> ConOpcion(IEnumerable<string> experimentos, bool habilitar)
    {
        var lista = experimentos.Where(e => e != Opcion && !e.StartsWith(Opcion + "@", StringComparison.Ordinal)).ToList();
        if (habilitar)
            lista.Add(Habilitada);
        return lista;
    }

    public static bool EstaAbierto(Navegador navegador)
    {
        var procesos = Process.GetProcessesByName(navegador.Proceso);
        bool abierto = procesos.Length > 0;
        foreach (var proceso in procesos)
            proceso.Dispose();
        return abierto;
    }

    /// <summary>Activa la opción en ese navegador. Lanza con un mensaje para el usuario si no se puede.</summary>
    public static Navegador Habilitar(string id)
    {
        var navegador = Conocidos.FirstOrDefault(n => n.Id == id) ?? throw new ArgumentException($"Navegador desconocido: «{id}».");
        if (!File.Exists(navegador.LocalState))
            throw new FileNotFoundException($"No se encontró la configuración de {navegador.Nombre}.", navegador.LocalState);
        if (EstaAbierto(navegador))
            throw new InvalidOperationException($"Cierre {navegador.Nombre} del todo (también si quedó en la bandeja) y vuelva a intentarlo.");

        var raiz = JsonNode.Parse(File.ReadAllText(navegador.LocalState)) as JsonObject
            ?? throw new InvalidDataException($"La configuración de {navegador.Nombre} no se pudo leer.");
        if (raiz["browser"] is not JsonObject browser)
            raiz["browser"] = browser = new JsonObject();
        var actuales = (browser["enabled_labs_experiments"] as JsonArray)?
            .Select(x => x?.GetValue<string>() ?? "").Where(x => x.Length > 0) ?? [];
        browser["enabled_labs_experiments"] = new JsonArray(ConOpcion(actuales, habilitar: true).Select(x => (JsonNode)x).ToArray());

        // Copia de respaldo y escritura segura: un corte a medias no deja el archivo roto.
        File.Copy(navegador.LocalState, navegador.LocalState + ".escritorio.bak", overwrite: true);
        var temporal = navegador.LocalState + ".escritorio.tmp";
        File.WriteAllText(temporal, raiz.ToJsonString());
        File.Move(temporal, navegador.LocalState, overwrite: true);
        return navegador;
    }

    static List<string> LeerExperimentos(string ruta)
    {
        try
        {
            var raiz = JsonNode.Parse(File.ReadAllText(ruta)) as JsonObject;
            return (raiz?["browser"]?["enabled_labs_experiments"] as JsonArray)?
                .Select(x => x?.GetValue<string>() ?? "").ToList() ?? [];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return [];
        }
    }
}
