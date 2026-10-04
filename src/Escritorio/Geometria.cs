namespace Escritorio;

/// <summary>Rectángulo en píxeles físicos de la pantalla.</summary>
public readonly record struct Rect(int X, int Y, int Ancho, int Alto)
{
    public int Derecha => X + Ancho;
    public int Abajo => Y + Alto;

    public bool Contiene(int x, int y) => x >= X && x < Derecha && y >= Y && y < Abajo;

    public bool Cerca(Rect otro, int tolerancia = 2) =>
        Math.Abs(X - otro.X) <= tolerancia && Math.Abs(Y - otro.Y) <= tolerancia;
}

/// <param name="Nombre">Nombre del dispositivo, p. ej. \\.\DISPLAY2.</param>
/// <param name="Trabajo">El área sin la barra de tareas.</param>
/// <param name="Escala">1.0 = 100 %, 1.25 = 125 %…</param>
public sealed record Pantalla(string Nombre, Rect Area, Rect Trabajo, bool Principal, double Escala = 1.0);

/// <summary>Dónde va cada cosa. Sin dependencias de Windows: se prueba en cualquier sistema.</summary>
public static class Geometria
{
    public const int Margen = 12;

    public static readonly string[] Lados =
    [
        "derecha", "izquierda", "centro", "arriba", "abajo",
        "arriba-derecha", "arriba-izquierda", "abajo-derecha", "abajo-izquierda",
    ];

    /// <summary>De izquierda a derecha: «pantalla 1» es la de más a la izquierda.</summary>
    public static List<Pantalla> Ordenar(IEnumerable<Pantalla> pantallas) =>
        pantallas.OrderBy(p => p.Area.X).ThenBy(p => p.Area.Y).ToList();

    /// <summary>
    /// «secundaria» (la primera que no es la principal), «principal» o un número desde 1.
    /// Si la pedida no existe (por ejemplo, se desconectó la segunda pantalla), se usa la principal.
    /// </summary>
    public static Pantalla ElegirPantalla(IEnumerable<Pantalla> pantallas, string? preferida = "secundaria")
    {
        var ordenadas = Ordenar(pantallas);
        if (ordenadas.Count == 0)
            throw new InvalidOperationException("No se encontró ninguna pantalla.");
        var principal = ordenadas.FirstOrDefault(p => p.Principal) ?? ordenadas[0];
        var pedida = (preferida ?? "").Trim().ToLowerInvariant();
        if (pedida == "principal")
            return principal;
        if (int.TryParse(pedida, out var numero))
            return numero >= 1 && numero <= ordenadas.Count ? ordenadas[numero - 1] : principal;
        return ordenadas.FirstOrDefault(p => !p.Principal) ?? principal;
    }

    /// <summary>Cómo se guarda una pantalla en una regla: «principal», «secundaria» o su número.</summary>
    public static string Describir(Pantalla pantalla, IEnumerable<Pantalla> pantallas)
    {
        var ordenadas = Ordenar(pantallas);
        if (pantalla.Principal)
            return "principal";
        if (ordenadas.FirstOrDefault(p => !p.Principal)?.Nombre == pantalla.Nombre)
            return "secundaria";
        return (ordenadas.FindIndex(p => p.Nombre == pantalla.Nombre) + 1).ToString();
    }

    /// <summary>La pantalla que contiene el centro del rectángulo (o la más cercana).</summary>
    public static Pantalla PantallaDe(Rect rect, IEnumerable<Pantalla> pantallas)
    {
        var lista = Ordenar(pantallas);
        int cx = rect.X + rect.Ancho / 2, cy = rect.Y + rect.Alto / 2;
        return lista.FirstOrDefault(p => p.Area.Contiene(cx, cy))
            ?? lista.OrderBy(p => Distancia(p.Area, cx, cy)).First();
    }

    static long Distancia(Rect r, int x, int y)
    {
        long dx = Math.Max(Math.Max(r.X - x, 0), x - r.Derecha);
        long dy = Math.Max(Math.Max(r.Y - y, 0), y - r.Abajo);
        return dx * dx + dy * dy;
    }

    /// <summary>Tamaño y posición según el «lado». alto = 0 ocupa todo el alto disponible.</summary>
    public static Rect Posicionar(Rect trabajo, int ancho, int alto, string? lado, int margen)
    {
        int libreAncho = Math.Max(trabajo.Ancho - 2 * margen, 100);
        int libreAlto = Math.Max(trabajo.Alto - 2 * margen, 100);
        ancho = Math.Min(ancho > 0 ? ancho : 400, libreAncho);
        alto = alto > 0 ? Math.Min(alto, libreAlto) : libreAlto;
        int izquierda = trabajo.X + margen;
        int derecha = trabajo.Derecha - ancho - margen;
        int arriba = trabajo.Y + margen;
        int abajo = trabajo.Abajo - alto - margen;
        int centroX = trabajo.X + (trabajo.Ancho - ancho) / 2;
        int centroY = trabajo.Y + (trabajo.Alto - alto) / 2;
        var (x, y) = (lado ?? "").Trim().ToLowerInvariant() switch
        {
            "izquierda" => (izquierda, centroY),
            "centro" => (centroX, centroY),
            "arriba" => (centroX, arriba),
            "abajo" => (centroX, abajo),
            "arriba-derecha" => (derecha, arriba),
            "arriba-izquierda" => (izquierda, arriba),
            "abajo-derecha" => (derecha, abajo),
            "abajo-izquierda" => (izquierda, abajo),
            _ => (derecha, centroY),
        };
        return new Rect(x, y, ancho, alto);
    }

    /// <summary>Píxeles al 100 % → píxeles físicos de esa pantalla.</summary>
    public static int Escalado(int valor, Pantalla pantalla) => (int)Math.Round(valor * pantalla.Escala);

    /// <summary>Mueve (y si hace falta achica) el rectángulo para que quede entero en el área de trabajo.</summary>
    public static Rect Dentro(Rect rect, Rect trabajo)
    {
        int ancho = Math.Min(rect.Ancho, trabajo.Ancho);
        int alto = Math.Min(rect.Alto, trabajo.Alto);
        int x = Math.Min(Math.Max(rect.X, trabajo.X), trabajo.Derecha - ancho);
        int y = Math.Min(Math.Max(rect.Y, trabajo.Y), trabajo.Abajo - alto);
        return new Rect(x, y, ancho, alto);
    }

    /// <summary>Un widget para apilar: tamaño ya escalado a la pantalla. alto = 0 reparte el espacio que sobre.</summary>
    public readonly record struct Apilable(string Id, int Ancho, int Alto);

    /// <summary>El resultado de apilar: dónde va cada widget y la franja vertical que ocupan entre todos.</summary>
    public sealed record Pila(Dictionary<string, Rect> Posiciones, Rect? Columna);

    /// <summary>
    /// Apila los widgets en una columna pegada al lado pedido («derecha» o «izquierda»), en orden,
    /// de arriba abajo y separados por el margen. Los de alto 0 se reparten el alto que sobre.
    /// La columna es tan ancha como el widget más ancho; los angostos se alinean al borde exterior.
    /// </summary>
    public static Pila Apilar(Rect trabajo, IReadOnlyList<Apilable> widgets, string? lado, int margen)
    {
        var posiciones = new Dictionary<string, Rect>();
        if (widgets.Count == 0)
            return new Pila(posiciones, null);
        bool izquierda = string.Equals((lado ?? "").Trim(), "izquierda", StringComparison.OrdinalIgnoreCase);
        int libreAlto = Math.Max(trabajo.Alto - 2 * margen, 100);
        int anchoColumna = Math.Clamp(widgets.Max(w => w.Ancho), 100, Math.Max(trabajo.Ancho - 2 * margen, 100));
        int fijos = widgets.Where(w => w.Alto > 0).Sum(w => Math.Min(w.Alto, libreAlto));
        int flexibles = widgets.Count(w => w.Alto <= 0);
        int sobrante = libreAlto - fijos - (widgets.Count - 1) * margen;
        int altoFlexible = flexibles > 0 ? Math.Max(sobrante / flexibles, 100) : 0;
        int columnaX = izquierda ? trabajo.X + margen : trabajo.Derecha - margen - anchoColumna;
        int y = trabajo.Y + margen;
        foreach (var widget in widgets)
        {
            int ancho = Math.Min(widget.Ancho > 0 ? widget.Ancho : anchoColumna, anchoColumna);
            int alto = widget.Alto > 0 ? Math.Min(widget.Alto, libreAlto) : altoFlexible;
            int x = izquierda ? columnaX : columnaX + anchoColumna - ancho;
            posiciones[widget.Id] = Dentro(new Rect(x, y, ancho, alto), trabajo);
            y += alto + margen;
        }
        return new Pila(posiciones, new Rect(columnaX, trabajo.Y, anchoColumna, trabajo.Alto));
    }

    /// <summary>El área de trabajo sin la columna de widgets (ni su margen): el espacio para las demás ventanas.</summary>
    public static Rect ZonaLibre(Rect trabajo, Rect? columna, string? lado, int margen)
    {
        if (columna is not { } franja)
            return trabajo;
        if (string.Equals((lado ?? "").Trim(), "izquierda", StringComparison.OrdinalIgnoreCase))
        {
            int x = franja.Derecha + margen;
            return new Rect(x, trabajo.Y, Math.Max(trabajo.Derecha - x, 100), trabajo.Alto);
        }
        return new Rect(trabajo.X, trabajo.Y, Math.Max(franja.X - margen - trabajo.X, 100), trabajo.Alto);
    }

    /// <summary>
    /// Dónde va un widget: su posición guardada (relativa al área de trabajo) o la de su «lado».
    /// El tamaño del archivo de configuración está en píxeles al 100 % y se escala a la pantalla.
    /// </summary>
    public static Rect UbicarWidget(Pantalla pantalla, int ancho, int alto, string? lado, Posicion? guardada)
    {
        int Escalar(int valor) => Escalado(valor, pantalla);
        var rect = Posicionar(pantalla.Trabajo, Escalar(ancho), Escalar(alto), lado, Escalar(Margen));
        if (guardada is not null)
        {
            rect = new Rect(
                pantalla.Trabajo.X + guardada.Dx,
                pantalla.Trabajo.Y + guardada.Dy,
                guardada.Ancho is > 0 ? guardada.Ancho.Value : rect.Ancho,
                guardada.Alto is > 0 ? guardada.Alto.Value : rect.Alto);
        }
        return Dentro(rect, pantalla.Trabajo);
    }
}
