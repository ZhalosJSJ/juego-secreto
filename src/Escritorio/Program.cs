namespace Escritorio;

internal static class Program
{
    public const string NombreMutex = @"Local\Escritorio-widgets";

    /// <param name="args">
    /// --desinstalar: lo usa Configuración → Aplicaciones.
    /// --reemplazar: espera a que se cierre la copia abierta (al instalarse) en lugar de avisar.
    /// --escritorio &lt;id&gt;: cambia de escritorio («libre» = ninguno). Si la app ya está abierta, se lo pide a esa copia.
    /// --sin-actualizar: abre este .exe tal cual, sin reemplazar la copia instalada (para probar una compilación).
    /// --actualizado &lt;versión&gt;: lo pasa la actualización a la copia instalada recién abierta, para que avise.
    /// </param>
    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Contains("--desinstalar"))
            return Instalacion.Desinstalar();
        // Un .exe nuevo reemplaza la versión instalada (y cierra la que esté abierta) antes de seguir.
        if (Instalacion.ActualizarSiHaceFalta(args) is { } codigo)
            return codigo;

        int indice = Array.IndexOf(args, "--escritorio");
        string? escritorioPedido = indice >= 0 && indice + 1 < args.Length ? args[indice + 1] : null;
        if (escritorioPedido is not null && Comandos.Enviar("escritorio " + escritorioPedido))
            return 0;

        using var mutex = new Mutex(false, NombreMutex);
        if (!Adquirir(mutex, esperar: args.Contains("--reemplazar")))
        {
            MessageBox.Show("Escritorio ya está abierto. Búsquelo en el ícono de la bandeja, junto al reloj.",
                "Escritorio", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }
        Application.ThreadException += (_, e) => Registro.Error("Error no controlado", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Registro.Error("Error fatal", e.ExceptionObject as Exception);
        int marca = Array.IndexOf(args, "--actualizado");
        string? versionAnterior = marca >= 0 && marca + 1 < args.Length ? args[marca + 1] : null;
        try
        {
            Application.Run(new Aplicacion(escritorioPedido, versionAnterior));
            return 0;
        }
        catch (Exception error)
        {
            Registro.Error("No se pudo iniciar", error);
            MessageBox.Show($"No se pudo iniciar:\n\n{error.Message}", "Escritorio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    static bool Adquirir(Mutex mutex, bool esperar)
    {
        try
        {
            return mutex.WaitOne(esperar ? TimeSpan.FromSeconds(15) : TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            return true;  // la copia anterior se cerró sin soltarlo: queda para esta
        }
    }
}
