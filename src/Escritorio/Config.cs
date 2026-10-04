using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Escritorio;

/// <summary>config.json: lo que el usuario elige. Se puede editar a mano.</summary>
public sealed class Config
{
    /// <summary>Dónde van los widgets: «secundaria», «principal» o un número desde 1.</summary>
    [JsonPropertyName("pantalla"), JsonConverter(typeof(TextoFlexible))]
    public string Pantalla { get; set; } = "secundaria";

    [JsonPropertyName("widgets")]
    public Dictionary<string, AjustesWidget> Widgets { get; set; } = new();

    [JsonPropertyName("organizar")]
    public AjustesOrganizar Organizar { get; set; } = new();

    public static Config PorDefecto() => new()
    {
        Widgets =
        {
            ["gran-sabio"] = new AjustesWidget
            {
                Titulo = "Gran Sabio",
                Url = "http://localhost:8765",
                Iniciar = "",
                Entorno = new() { ["NAVEGADOR"] = "no" },
                Ancho = 480,
                Alto = 0,
                Lado = "derecha",
                Fondo = "#040817",
            },
            ["reloj"] = new AjustesWidget
            {
                Titulo = "Reloj",
                Integrado = "reloj",
                Ancho = 280,
                Alto = 120,
                Lado = "arriba-izquierda",
            },
            ["baterias"] = new AjustesWidget
            {
                Titulo = "Baterías",
                Integrado = "baterias",
                Ancho = 300,
                Alto = 240,
                Lado = "abajo-izquierda",
            },
        },
    };
}

public sealed class AjustesWidget
{
    [JsonPropertyName("activo")] public bool Activo { get; set; } = true;
    [JsonPropertyName("titulo")] public string? Titulo { get; set; }

    /// <summary>Qué muestra: una dirección, un widget incluido en la app o un HTML de la carpeta widgets.</summary>
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("integrado")] public string? Integrado { get; set; }
    [JsonPropertyName("archivo")] public string? Archivo { get; set; }

    /// <summary>Programa que se ejecuta si la url no responde, con estas variables de entorno.</summary>
    [JsonPropertyName("iniciar")] public string? Iniciar { get; set; }
    [JsonPropertyName("entorno")] public Dictionary<string, string>? Entorno { get; set; }

    /// <summary>Tamaño en píxeles al 100 % de escala. alto = 0 ocupa todo el alto.</summary>
    [JsonPropertyName("ancho")] public int Ancho { get; set; } = 400;
    [JsonPropertyName("alto")] public int Alto { get; set; }
    [JsonPropertyName("lado")] public string Lado { get; set; } = "derecha";

    /// <summary>Sin marco, fuera de la barra de tareas y detrás de las demás ventanas.</summary>
    [JsonPropertyName("fijo")] public bool Fijo { get; set; } = true;
    [JsonPropertyName("fondo")] public string Fondo { get; set; } = "#0b1220";

    [JsonIgnore] public string Nombre => string.IsNullOrWhiteSpace(Titulo) ? "widget" : Titulo!;
}

public sealed class AjustesOrganizar
{
    /// <summary>Aplicar las reglas a cada ventana nueva que aparezca.</summary>
    [JsonPropertyName("activo")] public bool Activo { get; set; } = true;
    [JsonPropertyName("reglas")] public List<Regla> Reglas { get; set; } = new();
}

/// <summary>Dónde se pone una ventana de otro programa.</summary>
public sealed class Regla
{
    /// <summary>Nombre del programa, con o sin .exe (Discord.exe, spotify…).</summary>
    [JsonPropertyName("programa")] public string Programa { get; set; } = "";

    /// <summary>Opcional: solo si el título de la ventana contiene este texto.</summary>
    [JsonPropertyName("titulo")] public string? Titulo { get; set; }

    [JsonPropertyName("pantalla"), JsonConverter(typeof(TextoFlexible))]
    public string Pantalla { get; set; } = "secundaria";

    /// <summary>Posición exacta en píxeles físicos, relativa al área de trabajo (la graba «Recordar posición»).</summary>
    [JsonPropertyName("x")] public int? X { get; set; }
    [JsonPropertyName("y")] public int? Y { get; set; }
    [JsonPropertyName("ancho")] public int? Ancho { get; set; }
    [JsonPropertyName("alto")] public int? Alto { get; set; }

    /// <summary>Sin posición exacta: conserva su tamaño y se pone en este lado (por defecto, al centro).</summary>
    [JsonPropertyName("lado")] public string? Lado { get; set; }
    [JsonPropertyName("maximizar")] public bool Maximizar { get; set; }

    [JsonIgnore] public bool TieneRect => Ancho is > 0 && Alto is > 0;

    public string Describir() => string.IsNullOrWhiteSpace(Titulo) ? Programa : $"{Programa} («{Titulo}»)";
}

/// <summary>estado.json: lo que la app recuerda sola.</summary>
public sealed class Estado
{
    [JsonPropertyName("posiciones")] public Dictionary<string, Posicion> Posiciones { get; set; } = new();
    [JsonPropertyName("bloqueado")] public bool Bloqueado { get; set; }
}

/// <summary>Posición elegida por el usuario, relativa al área de trabajo de la pantalla de los widgets.</summary>
public sealed class Posicion
{
    [JsonPropertyName("dx")] public int Dx { get; set; }
    [JsonPropertyName("dy")] public int Dy { get; set; }
    [JsonPropertyName("ancho")] public int? Ancho { get; set; }
    [JsonPropertyName("alto")] public int? Alto { get; set; }
}

public static class Json
{
    public static readonly JsonSerializerOptions Opciones = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,  // tildes legibles al editar a mano
    };

    public static T Leer<T>(string ruta, Func<T> siNoExiste)
    {
        if (!File.Exists(ruta))
            return siNoExiste();
        return JsonSerializer.Deserialize<T>(File.ReadAllText(ruta), Opciones) ?? siNoExiste();
    }

    /// <summary>Escribe en un temporal y lo reemplaza: un corte a medias no deja el archivo roto.</summary>
    public static void Escribir<T>(string ruta, T datos)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        var temporal = ruta + ".tmp";
        File.WriteAllText(temporal, JsonSerializer.Serialize(datos, Opciones) + Environment.NewLine);
        File.Move(temporal, ruta, overwrite: true);
    }
}

/// <summary>Acepta "2" o 2 en el JSON: escribir la pantalla como número es lo natural.</summary>
public sealed class TextoFlexible : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader lector, Type tipo, JsonSerializerOptions opciones) =>
        lector.TokenType switch
        {
            JsonTokenType.Number => lector.GetInt32().ToString(),
            JsonTokenType.String => lector.GetString() ?? "",
            _ => throw new JsonException("Se esperaba un texto o un número."),
        };

    public override void Write(Utf8JsonWriter escritor, string valor, JsonSerializerOptions opciones)
    {
        if (int.TryParse(valor, out var numero))
            escritor.WriteNumberValue(numero);
        else
            escritor.WriteStringValue(valor);
    }
}
