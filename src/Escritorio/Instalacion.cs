using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace Escritorio;

/// <summary>
/// Instalación sin instalador: copia el .exe a %LOCALAPPDATA%\Programs\Escritorio, crea el acceso
/// del menú Inicio y lo registra en Configuración → Aplicaciones (desde ahí se desinstala).
/// Todo es del usuario actual: no pide permisos de administrador.
///
/// Actualización: al abrir un .exe nuevo con una versión ya instalada, este cierra la copia que
/// esté corriendo, reemplaza la instalada, limpia lo que sobre y la vuelve a abrir. Un .exe más
/// viejo que el instalado no degrada: abre el instalado.
/// </summary>
internal static class Instalacion
{
    const string Nombre = "Escritorio";
    const string ClaveInicio = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ClaveDesinstalar = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Escritorio";
    static readonly TimeSpan EsperaCierreAmable = TimeSpan.FromSeconds(8);
    static readonly TimeSpan EsperaCierreForzado = TimeSpan.FromSeconds(5);

    public static readonly string Carpeta = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Escritorio");
    public static readonly string Exe = Path.Combine(Carpeta, "Escritorio.exe");
    static readonly string AccesoMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Escritorio.lnk");

    static string Actual => Environment.ProcessPath ?? Application.ExecutablePath;

    public static string Version =>
        typeof(Instalacion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0";

    public static bool EstaInstalado => string.Equals(Path.GetFullPath(Actual), Path.GetFullPath(Exe), StringComparison.OrdinalIgnoreCase);

    public static bool InicioAutomatico
    {
        get
        {
            using var clave = Registry.CurrentUser.OpenSubKey(ClaveInicio);
            return clave?.GetValue(Nombre) is string;
        }
    }

    public static void AlternarInicioAutomatico()
    {
        using var clave = Registry.CurrentUser.CreateSubKey(ClaveInicio);
        if (clave.GetValue(Nombre) is string)
            clave.DeleteValue(Nombre, throwOnMissingValue: false);
        else
            clave.SetValue(Nombre, $"\"{Actual}\"");
    }

    // --- Instalar y actualizar ---

    /// <summary>Instalación a pedido (desde la bandeja), cuando no hay ninguna copia instalada.</summary>
    public static void Instalar()
    {
        Directory.CreateDirectory(Carpeta);
        File.Copy(Actual, Exe, overwrite: true);
        Registrar();
        Registro.Info($"Instalado en {Carpeta}");
    }

    /// <summary>
    /// Se llama al arrancar, antes de todo. Si este .exe no es el instalado pero hay uno instalado,
    /// decide qué hacer y lo hace. Devuelve el código con el que debe terminar este proceso,
    /// o null si debe seguir arrancando normalmente.
    /// </summary>
    public static int? ActualizarSiHaceFalta(string[] args)
    {
        if (EstaInstalado || args.Contains("--reemplazar") || args.Contains("--sin-actualizar") || !File.Exists(Exe))
            return null;
        var instalada = Versiones.Parsear(VersionDelArchivo(Exe));
        var actual = Versiones.Parsear(VersionDelArchivo(Actual)) ?? Versiones.Parsear(Version);
        switch (Versiones.Decidir(actual, instalada, MismoArchivo(Actual, Exe)))
        {
            case Versiones.Decision.AbrirInstalada:
                // Es la misma versión que ya está instalada: se usa esa (y solo esa).
                if (!HayOtraCopia())
                    Lanzar(Exe, "");
                return 0;
            case Versiones.Decision.NoDegradar:
                MessageBox.Show(
                    $"Ya está instalada una versión más nueva de Escritorio ({instalada}) que esta ({actual}). Se abre la instalada.",
                    Nombre, MessageBoxButtons.OK, MessageBoxIcon.Information);
                if (!HayOtraCopia())
                    Lanzar(Exe, "");
                return 0;
        }
        try
        {
            CerrarOtrasCopias();
            CopiarConReintentos(Actual, Exe);
            LimpiarCarpeta();
            Registrar();
            Registro.Info($"Actualizado de {instalada?.ToString() ?? "una versión anterior"} a {Version}");
        }
        catch (Exception error)
        {
            Registro.Error("No se pudo actualizar la copia instalada", error);
            MessageBox.Show(
                $"No se pudo reemplazar la versión instalada:\n\n{error.Message}\n\nCierre Escritorio desde la bandeja y vuelva a abrir este archivo.",
                Nombre, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }
        Lanzar(Exe, $"--reemplazar --actualizado \"{instalada?.ToString() ?? "anterior"}\"");
        return 0;
    }

    /// <summary>Acceso del menú Inicio, Configuración → Aplicaciones y, si ya estaba, el inicio con Windows.</summary>
    static void Registrar()
    {
        CrearAcceso(AccesoMenu, Exe);
        using (var clave = Registry.CurrentUser.CreateSubKey(ClaveDesinstalar))
        {
            clave.SetValue("DisplayName", Nombre);
            clave.SetValue("DisplayVersion", Version);
            clave.SetValue("DisplayIcon", Exe);
            clave.SetValue("InstallLocation", Carpeta);
            clave.SetValue("UninstallString", $"\"{Exe}\" --desinstalar");
            clave.SetValue("EstimatedSize", (int)(new FileInfo(Exe).Length / 1024), RegistryValueKind.DWord);
            clave.SetValue("NoModify", 1, RegistryValueKind.DWord);
            clave.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }
        // Si arrancaba con Windows (desde donde fuera), que arranque la copia instalada.
        if (InicioAutomatico)
        {
            using var clave = Registry.CurrentUser.CreateSubKey(ClaveInicio);
            clave.SetValue(Nombre, $"\"{Exe}\"");
        }
    }

    /// <summary>En la carpeta del programa solo vive el .exe: cualquier otra cosa es de una versión anterior.</summary>
    static void LimpiarCarpeta()
    {
        foreach (var entrada in Directory.EnumerateFileSystemEntries(Carpeta))
        {
            if (string.Equals(Path.GetFullPath(entrada), Path.GetFullPath(Exe), StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                if (Directory.Exists(entrada))
                    Directory.Delete(entrada, recursive: true);
                else
                    File.Delete(entrada);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Registro.Error($"No se pudo borrar {entrada}", error);
            }
        }
    }

    static string? VersionDelArchivo(string ruta)
    {
        try
        {
            return FileVersionInfo.GetVersionInfo(ruta).ProductVersion;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    static bool MismoArchivo(string a, string b)
    {
        try
        {
            if (new FileInfo(a).Length != new FileInfo(b).Length)
                return false;
            using var sha = SHA256.Create();
            using var fa = File.OpenRead(a);
            using var fb = File.OpenRead(b);
            return sha.ComputeHash(fa).AsSpan().SequenceEqual(SHA256.HashData(fb));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    static void CopiarConReintentos(string origen, string destino)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
        // Recién cerrada, la copia anterior puede seguir bloqueada un instante.
        for (int intento = 1; ; intento++)
        {
            try
            {
                File.Copy(origen, destino, overwrite: true);
                return;
            }
            catch (IOException) when (intento < 15)
            {
                Thread.Sleep(300);
            }
        }
    }

    // --- Otras copias en ejecución ---

    static IEnumerable<Process> OtrasCopias() =>
        Process.GetProcessesByName(Nombre).Where(p => p.Id != Environment.ProcessId);

    static bool HayOtraCopia()
    {
        var copias = OtrasCopias().ToList();
        bool hay = copias.Count > 0;
        copias.ForEach(p => p.Dispose());
        return hay;
    }

    /// <summary>Le pide a la copia abierta que cierre; si no entiende (versiones viejas) o no responde, la termina.</summary>
    static void CerrarOtrasCopias()
    {
        if (!HayOtraCopia())
            return;
        Comandos.Enviar("salir");
        if (EsperarCierre(EsperaCierreAmable))
            return;
        foreach (var copia in OtrasCopias())
        {
            using (copia)
            {
                try
                {
                    copia.Kill();
                    copia.WaitForExit((int)EsperaCierreForzado.TotalMilliseconds);
                }
                catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // Ya había terminado, o es de otro usuario: la copia no se podrá reemplazar y se avisará.
                }
            }
        }
        EsperarCierre(EsperaCierreForzado);
    }

    static bool EsperarCierre(TimeSpan limite)
    {
        var fin = DateTime.UtcNow + limite;
        while (DateTime.UtcNow < fin)
        {
            if (!HayOtraCopia())
                return true;
            Thread.Sleep(200);
        }
        return !HayOtraCopia();
    }

    static void Lanzar(string ruta, string argumentos) =>
        Process.Start(new ProcessStartInfo(ruta, argumentos) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(ruta)! });

    // --- Desinstalar ---

    /// <summary>Lo que ejecuta Configuración → Aplicaciones → Desinstalar.</summary>
    public static int Desinstalar()
    {
        using (var mutex = new Mutex(false, Program.NombreMutex))
        {
            bool libre;
            try
            {
                libre = mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                libre = true;
            }
            if (!libre)
            {
                MessageBox.Show("Primero cierre Escritorio: ícono de la bandeja → Salir. Después vuelva a desinstalarlo.",
                    Nombre, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 1;
            }
            mutex.ReleaseMutex();
        }
        if (MessageBox.Show("¿Desinstalar Escritorio?", Nombre, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return 0;

        using (var clave = Registry.CurrentUser.OpenSubKey(ClaveInicio, writable: true))
            clave?.DeleteValue(Nombre, throwOnMissingValue: false);
        Registry.CurrentUser.DeleteSubKeyTree(ClaveDesinstalar, throwOnMissingSubKey: false);
        if (File.Exists(AccesoMenu))
            File.Delete(AccesoMenu);
        MessageBox.Show($"Escritorio se desinstaló.\n\nSu configuración quedó en {Rutas.Datos}, por si lo vuelve a instalar. Puede borrar esa carpeta.",
            Nombre, MessageBoxButtons.OK, MessageBoxIcon.Information);
        // El .exe no se puede borrar mientras corre: un cmd invisible lo borra un par de segundos después de cerrarse.
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"{Carpeta}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        return 0;
    }

    // --- Acceso directo ---

    static void CrearAcceso(string ruta, string destino)
    {
        var acceso = (IShellLinkW)new ShellLink();
        try
        {
            acceso.SetPath(destino);
            acceso.SetWorkingDirectory(Path.GetDirectoryName(destino)!);
            acceso.SetDescription("Widgets para el escritorio");
            acceso.SetIconLocation(destino, 0);
            ((IPersistFile)acceso).Save(ruta, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(acceso);
        }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    class ShellLink;

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder archivo, int largo, IntPtr datos, uint flags);
        void GetIDList(out IntPtr lista);
        void SetIDList(IntPtr lista);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder nombre, int largo);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string nombre);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder carpeta, int largo);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string carpeta);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder argumentos, int largo);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string argumentos);
        void GetHotkey(out short tecla);
        void SetHotkey(short tecla);
        void GetShowCmd(out int comando);
        void SetShowCmd(int comando);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder ruta, int largo, out int indice);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string ruta, int indice);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string ruta, uint reservado);
        void Resolve(IntPtr ventana, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string archivo);
    }
}
