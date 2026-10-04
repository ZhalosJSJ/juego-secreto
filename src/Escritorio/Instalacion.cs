using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Microsoft.Win32;

namespace Escritorio;

/// <summary>
/// Instalación sin instalador: copia el .exe a %LOCALAPPDATA%\Programs\Escritorio, crea el acceso
/// del menú Inicio y lo registra en Configuración → Aplicaciones (desde ahí se desinstala).
/// Todo es del usuario actual: no pide permisos de administrador.
/// </summary>
internal static class Instalacion
{
    const string Nombre = "Escritorio";
    const string ClaveInicio = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ClaveDesinstalar = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Escritorio";

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

    public static void Instalar()
    {
        Directory.CreateDirectory(Carpeta);
        File.Copy(Actual, Exe, overwrite: true);
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
        // Si ya arrancaba con Windows, que arranque la copia instalada.
        if (InicioAutomatico)
        {
            using var clave = Registry.CurrentUser.CreateSubKey(ClaveInicio);
            clave.SetValue(Nombre, $"\"{Exe}\"");
        }
        Registro.Info($"Instalado en {Carpeta}");
    }

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
