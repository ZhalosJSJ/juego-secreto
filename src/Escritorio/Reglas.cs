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

    /// <summary>Busca por capas: la primera capa que tenga una regla para la ventana gana (escritorio antes que generales).</summary>
    public static Regla? Buscar(IEnumerable<IEnumerable<Regla>> capas, string proceso, string titulo) =>
        capas.Select(capa => Buscar(capa, proceso, titulo)).FirstOrDefault(r => r is not null);

    /// <summary>
    /// Dónde va la ventana dentro del espacio disponible (el área de trabajo, o lo que dejan libre los widgets).
    /// Con «x» e «y», esa posición exacta. Sin ellas, se pone en su «lado», pegada al borde (por defecto, al centro).
    /// El tamaño se toma de «ancho» y «alto» (píxeles o porcentaje); lo que falte conserva el actual.
    /// </summary>
    public static Rect Destino(Regla regla, Rect espacio, Rect actual)
    {
        int ancho = regla.Ancho?.Resolver(espacio.Ancho) ?? actual.Ancho;
        int alto = regla.Alto?.Resolver(espacio.Alto) ?? actual.Alto;
        ancho = Math.Clamp(ancho, 1, espacio.Ancho);
        alto = Math.Clamp(alto, 1, espacio.Alto);
        if (regla.TienePosicion)
            return Geometria.Dentro(new Rect(espacio.X + regla.X!.Value, espacio.Y + regla.Y!.Value, ancho, alto), espacio);
        var lado = string.IsNullOrWhiteSpace(regla.Lado) ? "centro" : regla.Lado;
        return Geometria.Posicionar(espacio, ancho, alto, lado, margen: 0);
    }

    /// <summary>
    /// La regla que guarda «Recordar dónde está» para una ventana tal como está ahora.
    /// <paramref name="espacioDe"/> da el espacio disponible en cada pantalla (el mismo que usará <see cref="Destino"/>).
    /// </summary>
    public static Regla Recordar(string proceso, string titulo, Rect rect, bool maximizada, IReadOnlyList<Pantalla> pantallas, Func<Pantalla, Rect> espacioDe)
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
            var espacio = espacioDe(pantalla);
            regla.X = rect.X - espacio.X;
            regla.Y = rect.Y - espacio.Y;
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
