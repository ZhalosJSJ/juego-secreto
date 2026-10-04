namespace Escritorio;

/// <summary>Qué regla corresponde a una ventana y dónde la deja. Sin dependencias de Windows.</summary>
public static class Reglas
{
    /// <summary>Las apps de la Tienda corren dentro de este proceso: para ellas cuenta el título.</summary>
    public const string ProcesoTienda = "ApplicationFrameHost";

    public static string NormalizarPrograma(string? programa)
    {
        var nombre = (programa ?? "").Trim();
        return nombre.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? nombre[..^4] : nombre;
    }

    public static bool Coincide(Regla regla, string proceso, string titulo)
    {
        var programa = NormalizarPrograma(regla.Programa);
        if (programa.Length == 0 || !string.Equals(programa, NormalizarPrograma(proceso), StringComparison.OrdinalIgnoreCase))
            return false;
        return string.IsNullOrEmpty(regla.Titulo) || titulo.Contains(regla.Titulo, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>La regla más específica: las que piden un título ganan a las que no.</summary>
    public static Regla? Buscar(IEnumerable<Regla> reglas, string proceso, string titulo) =>
        reglas.Where(r => Coincide(r, proceso, titulo))
              .OrderByDescending(r => !string.IsNullOrEmpty(r.Titulo))
              .FirstOrDefault();

    /// <summary>
    /// Dónde va la ventana. Con posición exacta, esa (ajustada a la pantalla).
    /// Sin ella, conserva su tamaño y se pone en su «lado», pegada al borde (por defecto, al centro).
    /// </summary>
    public static Rect Destino(Regla regla, Pantalla pantalla, Rect actual)
    {
        var trabajo = pantalla.Trabajo;
        if (regla.TieneRect)
        {
            var exacto = new Rect(trabajo.X + (regla.X ?? 0), trabajo.Y + (regla.Y ?? 0), regla.Ancho!.Value, regla.Alto!.Value);
            return Geometria.Dentro(exacto, trabajo);
        }
        var tamano = Geometria.Dentro(actual with { X = trabajo.X, Y = trabajo.Y }, trabajo);
        var lado = string.IsNullOrWhiteSpace(regla.Lado) ? "centro" : regla.Lado;
        return Geometria.Posicionar(trabajo, tamano.Ancho, tamano.Alto, lado, margen: 0);
    }

    /// <summary>La regla que guarda «Recordar posición» para una ventana tal como está ahora.</summary>
    public static Regla Recordar(string proceso, string titulo, Rect rect, bool maximizada, IReadOnlyList<Pantalla> pantallas)
    {
        var pantalla = Geometria.PantallaDe(rect, pantallas);
        var regla = new Regla
        {
            Programa = NormalizarPrograma(proceso) + ".exe",
            Titulo = string.Equals(proceso, ProcesoTienda, StringComparison.OrdinalIgnoreCase) ? titulo : null,
            Pantalla = Geometria.Describir(pantalla, pantallas),
        };
        if (maximizada)
        {
            regla.Maximizar = true;
        }
        else
        {
            regla.X = rect.X - pantalla.Trabajo.X;
            regla.Y = rect.Y - pantalla.Trabajo.Y;
            regla.Ancho = rect.Ancho;
            regla.Alto = rect.Alto;
        }
        return regla;
    }

    /// <summary>Agrega la regla, reemplazando la que hubiera para el mismo programa y título.</summary>
    public static void Guardar(List<Regla> reglas, Regla nueva)
    {
        reglas.RemoveAll(r =>
            string.Equals(NormalizarPrograma(r.Programa), NormalizarPrograma(nueva.Programa), StringComparison.OrdinalIgnoreCase)
            && string.Equals(r.Titulo ?? "", nueva.Titulo ?? "", StringComparison.OrdinalIgnoreCase));
        reglas.Add(nueva);
    }
}
