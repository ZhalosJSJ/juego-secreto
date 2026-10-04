namespace Escritorio;

internal static class Program
{
    public const string NombreMutex = @"Local\Escritorio-widgets";

    /// <param name="args">
    /// --desinstalar: lo usa Configuración → Aplicaciones.
    /// --reemplazar: espera a que se cierre la copia abierta (al instalarse) en lugar de avisar.
    /// </param>
    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Contains("--desinstalar"))
            return Instalacion.Desinstalar();

        using var mutex = new Mutex(false, NombreMutex);
        if (!Adquirir(mutex, esperar: args.Contains("--reemplazar")))
        {
            MessageBox.Show("Escritorio ya está abierto. Búsquelo en el ícono de la bandeja, junto al reloj.",
                "Escritorio", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }
        Application.ThreadException += (_, e) => Registro.Error("Error no controlado", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Registro.Error("Error fatal", e.ExceptionObject as Exception);
        try
        {
            Application.Run(new Aplicacion());
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
