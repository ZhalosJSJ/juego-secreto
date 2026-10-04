using System.Globalization;
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

    /// <summary>Distribuciones con nombre: qué widgets se ven y dónde van las ventanas de otros programas.</summary>
    [JsonPropertyName("escritorios")]
    public Dictionary<string, Escritorio> Escritorios { get; set; } = new();

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
            ["reproductor"] = new AjustesWidget
            {
                Titulo = "Reproductor",
                Integrado = "reproductor",
                Ancho = 340,
                Alto = 200,
                Lado = "abajo",
                Opciones = new() { ["diseno"] = "tarjeta" },
            },
        },
        Escritorios =
        {
            ["trabajo"] = new Escritorio
            {
                Titulo = "Trabajo",
                Apilar = "derecha",
                Widgets = new()
                {
                    ["reloj"] = new WidgetEnEscritorio(),
                    ["reproductor"] = new WidgetEnEscritorio(),
                    ["baterias"] = new WidgetEnEscritorio(),
                    ["gran-sabio"] = new WidgetEnEscritorio(),
                },
                Ventanas =
                {
                    new Regla { Programa = "Discord.exe", Pantalla = "secundaria", Lado = "izquierda", Ancho = Medida.Porcentaje(100), Alto = Medida.Porcentaje(100) },
                },
            },
            ["widgets"] = new Escritorio
            {
                Titulo = "Solo widgets",
                Apilar = "derecha",
            },
        },
    };

    /// <summary>
    /// Los widgets que se ven en un escritorio, en orden, con sus ajustes de posición ya aplicados.
    /// Sin escritorio (o si el escritorio no dice «widgets»), se ven todos los activos tal como están.
    /// Un escritorio con «widgets» muestra solo esos, en ese orden.
    /// </summary>
    public List<WidgetEfectivo> WidgetsDe(Escritorio? escritorio)
    {
        var lista = new List<WidgetEfectivo>();
        if (escritorio?.Widgets is null)
        {
            foreach (var (id, ajustes) in Widgets)
                if (ajustes.Activo)
                    lista.Add(new WidgetEfectivo(id, ajustes, ajustes.Ancho, ajustes.Alto, ajustes.Lado, ajustes.Pantalla));
            return lista;
        }
        foreach (var (id, enEscritorio) in escritorio.Widgets)
        {
            if (!enEscritorio.Visible || !Widgets.TryGetValue(id, out var ajustes) || !ajustes.Activo)
                continue;
            lista.Add(new WidgetEfectivo(
                id,
                ajustes,
                enEscritorio.Ancho ?? ajustes.Ancho,
                enEscritorio.Alto ?? ajustes.Alto,
                enEscritorio.Lado ?? ajustes.Lado,
                enEscritorio.Pantalla ?? ajustes.Pantalla));
        }
        return lista;
    }
}

/// <summary>Un widget tal como se muestra en el escritorio actual: sus ajustes y su posición efectiva.</summary>
public sealed record WidgetEfectivo(string Id, AjustesWidget Ajustes, int Ancho, int Alto, string Lado, string? Pantalla);

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

    /// <summary>Opciones para la página del widget: se le pasan en la dirección (?diseno=vinilo).</summary>
    [JsonPropertyName("opciones")] public Dictionary<string, string>? Opciones { get; set; }

    /// <summary>Tamaño en píxeles al 100 % de escala. alto = 0 ocupa todo el alto.</summary>
    [JsonPropertyName("ancho")] public int Ancho { get; set; } = 400;
    [JsonPropertyName("alto")] public int Alto { get; set; }
    [JsonPropertyName("lado")] public string Lado { get; set; } = "derecha";

    /// <summary>Pantalla propia de este widget; si falta, la pantalla de los widgets.</summary>
    [JsonPropertyName("pantalla"), JsonConverter(typeof(TextoFlexible))]
    public string? Pantalla { get; set; }

    /// <summary>Sin marco, fuera de la barra de tareas y detrás de las demás ventanas.</summary>
    [JsonPropertyName("fijo")] public bool Fijo { get; set; } = true;
    [JsonPropertyName("fondo")] public string Fondo { get; set; } = "#0b1220";

    [JsonIgnore] public string Nombre => string.IsNullOrWhiteSpace(Titulo) ? "widget" : Titulo!;
}

/// <summary>Una distribución con nombre.</summary>
public sealed class Escritorio
{
    [JsonPropertyName("titulo")] public string? Titulo { get; set; }

    /// <summary>Pantalla de los widgets en este escritorio; si falta, la general.</summary>
    [JsonPropertyName("pantalla"), JsonConverter(typeof(TextoFlexible))]
    public string? Pantalla { get; set; }

    /// <summary>«derecha» o «izquierda»: los widgets se apilan en una columna de ese lado, en orden.</summary>
    [JsonPropertyName("apilar")] public string? Apilar { get; set; }

    /// <summary>Qué widgets se ven (true, false o ajustes de posición). Si falta, todos.</summary>
    [JsonPropertyName("widgets")] public Dictionary<string, WidgetEnEscritorio>? Widgets { get; set; }

    /// <summary>Reglas de ventanas propias de este escritorio; mandan sobre las generales.</summary>
    [JsonPropertyName("ventanas")] public List<Regla> Ventanas { get; set; } = new();

    public string Nombre(string id) => string.IsNullOrWhiteSpace(Titulo) ? id : Titulo!;
}

/// <summary>Cómo se ve un widget dentro de un escritorio. En el JSON puede ser true, false o un objeto.</summary>
[JsonConverter(typeof(WidgetEnEscritorioConverter))]
public sealed class WidgetEnEscritorio
{
    public bool Visible { get; set; } = true;
    public int? Ancho { get; set; }
    public int? Alto { get; set; }
    public string? Lado { get; set; }
    public string? Pantalla { get; set; }

    internal bool SoloVisibilidad => Ancho is null && Alto is null && Lado is null && Pantalla is null;
}

public sealed class AjustesOrganizar
{
    /// <summary>Aplicar las reglas a cada ventana nueva que aparezca.</summary>
    [JsonPropertyName("activo")] public bool Activo { get; set; } = true;

    /// <summary>Reglas generales: valen en todos los escritorios.</summary>
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

    /// <summary>Posición exacta en píxeles, relativa a la esquina del espacio disponible (la graba «Recordar dónde está»).</summary>
    [JsonPropertyName("x")] public int? X { get; set; }
    [JsonPropertyName("y")] public int? Y { get; set; }

    /// <summary>Tamaño: píxeles (800) o porcentaje del espacio disponible ("60%"). Si falta, conserva el actual.</summary>
    [JsonPropertyName("ancho")] public Medida? Ancho { get; set; }
    [JsonPropertyName("alto")] public Medida? Alto { get; set; }

    /// <summary>Sin posición exacta: se pone en este lado (por defecto, al centro).</summary>
    [JsonPropertyName("lado")] public string? Lado { get; set; }
    [JsonPropertyName("maximizar")] public bool Maximizar { get; set; }

    /// <summary>Opcional: programa o acceso directo que se abre al cambiar a un escritorio, si no está abierto.</summary>
    [JsonPropertyName("abrir")] public string? Abrir { get; set; }

    [JsonIgnore] public bool TienePosicion => X.HasValue && Y.HasValue;

    public string Describir() => string.IsNullOrWhiteSpace(Titulo) ? Programa : $"{Programa} («{Titulo}»)";
}

/// <summary>Un tamaño en píxeles o como porcentaje del espacio disponible.</summary>
[JsonConverter(typeof(MedidaConverter))]
public readonly record struct Medida(int? Pixeles, double? Porciento)
{
    public static Medida Pixel(int pixeles) => new(pixeles, null);
    public static Medida Porcentaje(double porciento) => new(null, porciento);

    public static implicit operator Medida(int pixeles) => Pixel(pixeles);

    public int Resolver(int total) =>
        Pixeles ?? (int)Math.Round(total * (Porciento ?? 100) / 100);

    public static bool TryParse(string? texto, out Medida medida)
    {
        medida = default;
        var limpio = (texto ?? "").Trim();
        if (limpio.EndsWith('%') && double.TryParse(limpio[..^1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var porciento))
        {
            medida = Porcentaje(porciento);
            return true;
        }
        if (int.TryParse(limpio, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pixeles))
        {
            medida = Pixel(pixeles);
            return true;
        }
        return false;
    }

    public override string ToString() =>
        Pixeles is { } px ? px.ToString(CultureInfo.InvariantCulture) : $"{(Porciento ?? 0).ToString(CultureInfo.InvariantCulture)}%";
}

/// <summary>estado.json: lo que la app recuerda sola.</summary>
public sealed class Estado
{
    /// <summary>Posiciones elegidas por el usuario. Clave: «widget» o «escritorio/widget».</summary>
    [JsonPropertyName("posiciones")] public Dictionary<string, Posicion> Posiciones { get; set; } = new();
    [JsonPropertyName("bloqueado")] public bool Bloqueado { get; set; }

    /// <summary>El escritorio activo; null = libre.</summary>
    [JsonPropertyName("escritorio")] public string? Escritorio { get; set; }
}

/// <summary>Posición elegida por el usuario, relativa al área de trabajo de la pantalla del widget.</summary>
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
            JsonTokenType.Number => lector.GetInt32().ToString(CultureInfo.InvariantCulture),
            JsonTokenType.String => lector.GetString() ?? "",
            _ => throw new JsonException("Se esperaba un texto o un número."),
        };

    public override void Write(Utf8JsonWriter escritor, string valor, JsonSerializerOptions opciones)
    {
        if (int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numero))
            escritor.WriteNumberValue(numero);
        else
            escritor.WriteStringValue(valor);
    }
}

/// <summary>800 o "60%".</summary>
public sealed class MedidaConverter : JsonConverter<Medida>
{
    public override Medida Read(ref Utf8JsonReader lector, Type tipo, JsonSerializerOptions opciones)
    {
        if (lector.TokenType == JsonTokenType.Number)
            return Medida.Pixel(lector.GetInt32());
        if (lector.TokenType == JsonTokenType.String && Medida.TryParse(lector.GetString(), out var medida))
            return medida;
        throw new JsonException("Un tamaño es un número de píxeles (800) o un porcentaje (\"60%\").");
    }

    public override void Write(Utf8JsonWriter escritor, Medida valor, JsonSerializerOptions opciones)
    {
        if (valor.Pixeles is { } pixeles)
            escritor.WriteNumberValue(pixeles);
        else
            escritor.WriteStringValue(valor.ToString());
    }
}

/// <summary>true, false, o { "lado": "...", "ancho": ..., "alto": ..., "pantalla": ... }.</summary>
public sealed class WidgetEnEscritorioConverter : JsonConverter<WidgetEnEscritorio>
{
    // Sin el atributo del convertidor, para poder leer y escribir el objeto sin recursión.
    sealed class Datos
    {
        [JsonPropertyName("visible")] public bool Visible { get; set; } = true;
        [JsonPropertyName("ancho")] public int? Ancho { get; set; }
        [JsonPropertyName("alto")] public int? Alto { get; set; }
        [JsonPropertyName("lado")] public string? Lado { get; set; }
        [JsonPropertyName("pantalla"), JsonConverter(typeof(TextoFlexible))] public string? Pantalla { get; set; }
    }

    public override WidgetEnEscritorio Read(ref Utf8JsonReader lector, Type tipo, JsonSerializerOptions opciones)
    {
        switch (lector.TokenType)
        {
            case JsonTokenType.True:
                return new WidgetEnEscritorio();
            case JsonTokenType.False:
                return new WidgetEnEscritorio { Visible = false };
            case JsonTokenType.StartObject:
                var datos = JsonSerializer.Deserialize<Datos>(ref lector, opciones) ?? new Datos();
                return new WidgetEnEscritorio
                {
                    Visible = datos.Visible, Ancho = datos.Ancho, Alto = datos.Alto, Lado = datos.Lado, Pantalla = datos.Pantalla,
                };
            default:
                throw new JsonException("Un widget de un escritorio es true, false o un objeto con su posición.");
        }
    }

    public override void Write(Utf8JsonWriter escritor, WidgetEnEscritorio valor, JsonSerializerOptions opciones)
    {
        if (valor.SoloVisibilidad)
            escritor.WriteBooleanValue(valor.Visible);
        else
            JsonSerializer.Serialize(escritor, new Datos
            {
                Visible = valor.Visible, Ancho = valor.Ancho, Alto = valor.Alto, Lado = valor.Lado, Pantalla = valor.Pantalla,
            }, opciones);
    }
}
