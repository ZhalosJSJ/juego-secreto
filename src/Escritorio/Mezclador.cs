using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Escritorio;

/// <summary>
/// El volumen de cada programa y el general, como el mezclador de Windows, con la interfaz de
/// audio del sistema (Core Audio) llamada directamente por COM: sin paquetes externos.
/// Las sesiones se agrupan por programa (Chrome abre varias) y el volumen se aplica a todas.
/// </summary>
internal sealed class Mezclador : IDisposable
{
    const string General = "*";
    const string Sistema = "sistema";
    static readonly TimeSpan VidaDelDispositivo = TimeSpan.FromSeconds(5);

    sealed class Programa
    {
        public string Clave = "";
        public string Nombre = "";
        public string? Icono;
        public bool Activo;
        public float Nivel;
        public bool Silencio;
        public float Pico;
        public readonly List<CoreAudio.IAudioSessionControl> Sesiones = new();
    }

    readonly Aplicacion app;
    readonly Dictionary<string, string?> iconos = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<int, (string Nombre, string? Icono)> procesos = new();
    CoreAudio.IMMDevice? dispositivo;
    CoreAudio.IAudioSessionManager2? gestor;
    CoreAudio.IAudioEndpointVolume? general;
    CoreAudio.IAudioMeterInformation? medidorGeneral;
    DateTime dispositivoDesde;
    Guid sinContexto = Guid.Empty;
    Dictionary<string, float>? atenuados;  // niveles originales mientras se atenúa

    /// <summary>El nombre de proceso de este programa: la voz del Gran Sabio suena aquí y no se atenúa.</summary>
    static readonly string ClavePropia = ClaveDe(Process.GetCurrentProcess().ProcessName, Environment.ProcessId);

    public bool Atenuando => atenuados is not null;

    public Mezclador(Aplicacion app) => this.app = app;

    public void Dispose() => Soltar();

    /// <summary>Manda al widget el estado actual de todos los volúmenes.</summary>
    public void Enviar()
    {
        try
        {
            app.EnviarA("reproductor", JsonSerializer.Serialize(Leer()));
        }
        catch (Exception error)
        {
            Registro.Error("No se pudo leer el mezclador", error);
            Soltar();
        }
    }

    /// <summary>Cambia el volumen (0 a 1) de un programa, o del general con «*».</summary>
    public void Volumen(string clave, double nivel)
    {
        float valor = Medios.Nivel(nivel);
        try
        {
            if (clave == General)
                Preparar().general!.SetMasterVolumeLevelScalar(valor, ref sinContexto);
            else
                foreach (var sesion in SesionesDe(clave))
                    ((CoreAudio.ISimpleAudioVolume)sesion).SetMasterVolume(valor, ref sinContexto);
        }
        catch (Exception error)
        {
            Registro.Error($"No se pudo cambiar el volumen de «{clave}»", error);
            Soltar();
        }
    }

    public void Silencio(string clave, bool silencio)
    {
        try
        {
            if (clave == General)
                Preparar().general!.SetMute(silencio, ref sinContexto);
            else
                foreach (var sesion in SesionesDe(clave))
                    ((CoreAudio.ISimpleAudioVolume)sesion).SetMute(silencio, ref sinContexto);
        }
        catch (Exception error)
        {
            Registro.Error($"No se pudo silenciar «{clave}»", error);
            Soltar();
        }
    }

    /// <summary>Baja los demás programas a una fracción de su volumen (0.25 = a un cuarto), recordando el original.</summary>
    public void Atenuar(double fraccion)
    {
        if (atenuados is not null)
            return;
        try
        {
            var originales = new Dictionary<string, float>();
            foreach (var programa in Programas(Preparar().gestor!).Values)
            {
                if (programa.Clave == Sistema || programa.Clave == ClavePropia)
                    continue;
                originales[programa.Clave] = programa.Nivel;
                float nivel = Medios.Nivel(programa.Nivel * fraccion);
                foreach (var sesion in programa.Sesiones)
                    ((CoreAudio.ISimpleAudioVolume)sesion).SetMasterVolume(nivel, ref sinContexto);
            }
            atenuados = originales;
        }
        catch (Exception error)
        {
            Registro.Error("No se pudo atenuar el volumen", error);
            Soltar();
        }
    }

    /// <summary>Devuelve a cada programa el volumen que tenía antes de atenuar.</summary>
    public void Restaurar()
    {
        if (atenuados is null)
            return;
        var originales = atenuados;
        atenuados = null;
        try
        {
            var programas = Programas(Preparar().gestor!);
            foreach (var (clave, nivel) in originales)
                if (programas.TryGetValue(clave, out var programa))
                    foreach (var sesion in programa.Sesiones)
                        ((CoreAudio.ISimpleAudioVolume)sesion).SetMasterVolume(nivel, ref sinContexto);
        }
        catch (Exception error)
        {
            Registro.Error("No se pudo restaurar el volumen", error);
            Soltar();
        }
    }

    // --- Lectura ---

    object Leer()
    {
        var m = Preparar();
        m.general!.GetMasterVolumeLevelScalar(out float nivelGeneral);
        m.general.GetMute(out bool silencioGeneral);
        float picoGeneral = 0;
        m.medidorGeneral?.GetPeakValue(out picoGeneral);
        var programas = Programas(m.gestor!).Values
            .OrderByDescending(p => p.Activo)
            .ThenBy(p => p.Clave == Sistema)
            .ThenBy(p => p.Nombre, StringComparer.CurrentCultureIgnoreCase)
            .Select(p => new { clave = p.Clave, nombre = p.Nombre, icono = p.Icono, activo = p.Activo, nivel = p.Nivel, silencio = p.Silencio, pico = p.Pico })
            .ToList();
        return new
        {
            tipo = "mezclador",
            general = new { nivel = nivelGeneral, silencio = silencioGeneral, pico = picoGeneral },
            programas,
        };
    }

    IEnumerable<CoreAudio.IAudioSessionControl> SesionesDe(string clave) =>
        Programas(Preparar().gestor!).TryGetValue(clave, out var programa) ? programa.Sesiones : [];

    /// <summary>Las sesiones de audio vivas, agrupadas por programa.</summary>
    Dictionary<string, Programa> Programas(CoreAudio.IAudioSessionManager2 gestor)
    {
        var resultado = new Dictionary<string, Programa>(StringComparer.OrdinalIgnoreCase);
        gestor.GetSessionEnumerator(out var enumerador);
        enumerador.GetCount(out int cantidad);
        for (int i = 0; i < cantidad; i++)
        {
            CoreAudio.IAudioSessionControl control;
            try
            {
                enumerador.GetSession(i, out control);
            }
            catch (COMException)
            {
                continue;
            }
            control.GetState(out var estado);
            if (estado == CoreAudio.AudioSessionState.Expired)
                continue;
            var control2 = (CoreAudio.IAudioSessionControl2)control;
            control2.GetProcessId(out uint pid);
            bool esSistema = control2.IsSystemSoundsSession() == 0;
            var (clave, nombre, icono) = esSistema || pid == 0 ? (Sistema, Medios.NombreDeFuente(Sistema), null) : DescribirProceso((int)pid);

            if (!resultado.TryGetValue(clave, out var programa))
            {
                programa = new Programa { Clave = clave, Nombre = nombre, Icono = icono };
                resultado[clave] = programa;
            }
            var volumen = (CoreAudio.ISimpleAudioVolume)control;
            volumen.GetMasterVolume(out float nivel);
            volumen.GetMute(out bool silencio);
            float pico = 0;
            try
            {
                ((CoreAudio.IAudioMeterInformation)control).GetPeakValue(out pico);
            }
            catch (InvalidCastException)
            {
            }
            bool primera = programa.Sesiones.Count == 0;
            programa.Sesiones.Add(control);
            programa.Activo |= estado == CoreAudio.AudioSessionState.Active;
            programa.Nivel = primera ? nivel : Math.Max(programa.Nivel, nivel);
            programa.Silencio = primera ? silencio : programa.Silencio && silencio;
            programa.Pico = Math.Max(programa.Pico, pico);
        }
        return resultado;
    }

    (string Clave, string Nombre, string? Icono) DescribirProceso(int pid)
    {
        if (procesos.TryGetValue(pid, out var conocido))
            return (ClaveDe(conocido.Nombre, pid), conocido.Nombre, conocido.Icono);
        string proceso = "";
        string? descripcion = null, ruta = null;
        try
        {
            using var p = Process.GetProcessById(pid);
            proceso = p.ProcessName;
            try
            {
                ruta = p.MainModule?.FileName;
                descripcion = p.MainModule?.FileVersionInfo.FileDescription;
            }
            catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // Programas de otro nivel de permisos: solo se conoce su nombre de proceso.
            }
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            proceso = "proceso " + pid;
        }
        var nombre = Medios.NombreDePrograma(proceso, descripcion);
        var icono = ruta is null ? null : IconoDe(ruta);
        procesos[pid] = (nombre, icono);
        return (ClaveDe(proceso.Length > 0 ? proceso : nombre, pid), nombre, icono);
    }

    /// <summary>Clave estable por programa: su nombre de proceso en minúsculas.</summary>
    static string ClaveDe(string nombre, int pid)
    {
        var clave = new string(nombre.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or ' ').ToArray()).Trim();
        return clave.Length > 0 ? clave : "pid" + pid;
    }

    string? IconoDe(string ruta)
    {
        if (iconos.TryGetValue(ruta, out var icono))
            return icono;
        try
        {
            using var extraido = Icon.ExtractAssociatedIcon(ruta);
            if (extraido is null)
                return iconos[ruta] = null;
            using var mapa = extraido.ToBitmap();
            using var salida = new MemoryStream();
            mapa.Save(salida, ImageFormat.Png);
            return iconos[ruta] = "data:image/png;base64," + Convert.ToBase64String(salida.ToArray());
        }
        catch (Exception)
        {
            return iconos[ruta] = null;
        }
    }

    // --- Dispositivo de salida ---

    /// <summary>El dispositivo de salida predeterminado; se renueva cada pocos segundos por si cambia (auriculares, etc.).</summary>
    Mezclador Preparar()
    {
        if (gestor is not null && DateTime.UtcNow - dispositivoDesde < VidaDelDispositivo)
            return this;
        Soltar();
        var enumerador = (CoreAudio.IMMDeviceEnumerator)new CoreAudio.MMDeviceEnumerator();
        enumerador.GetDefaultAudioEndpoint(CoreAudio.EDataFlow.Render, CoreAudio.ERole.Multimedia, out dispositivo);
        gestor = (CoreAudio.IAudioSessionManager2)Activar(dispositivo, typeof(CoreAudio.IAudioSessionManager2).GUID);
        general = (CoreAudio.IAudioEndpointVolume)Activar(dispositivo, typeof(CoreAudio.IAudioEndpointVolume).GUID);
        try
        {
            medidorGeneral = (CoreAudio.IAudioMeterInformation)Activar(dispositivo, typeof(CoreAudio.IAudioMeterInformation).GUID);
        }
        catch (COMException)
        {
            medidorGeneral = null;
        }
        dispositivoDesde = DateTime.UtcNow;
        procesos.Clear();  // un proceso puede haber terminado y otro reutilizado su número
        return this;
    }

    static object Activar(CoreAudio.IMMDevice dispositivo, Guid interfaz)
    {
        dispositivo.Activate(ref interfaz, CoreAudio.CLSCTX_ALL, IntPtr.Zero, out var objeto);
        return objeto;
    }

    void Soltar()
    {
        gestor = null;
        general = null;
        medidorGeneral = null;
        dispositivo = null;
    }
}

/// <summary>Las interfaces de Core Audio que usa el mezclador, tal como las define Windows.</summary>
internal static class CoreAudio
{
    public const int CLSCTX_ALL = 0x17;

    public enum EDataFlow { Render, Capture, All }
    public enum ERole { Console, Multimedia, Communications }
    public enum AudioSessionState { Inactive, Active, Expired }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    public class MMDeviceEnumerator;

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDeviceEnumerator
    {
        void EnumAudioEndpoints(EDataFlow flujo, int estados, out IntPtr coleccion);
        void GetDefaultAudioEndpoint(EDataFlow flujo, ERole rol, out IMMDevice dispositivo);
        void GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice dispositivo);
        void RegisterEndpointNotificationCallback(IntPtr cliente);
        void UnregisterEndpointNotificationCallback(IntPtr cliente);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDevice
    {
        void Activate(ref Guid interfaz, int contexto, IntPtr parametros, [MarshalAs(UnmanagedType.IUnknown)] out object objeto);
        void OpenPropertyStore(int acceso, out IntPtr propiedades);
        void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        void GetState(out int estado);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioSessionManager2
    {
        // IAudioSessionManager
        void GetAudioSessionControl(ref Guid sesion, int flags, out IAudioSessionControl control);
        void GetSimpleAudioVolume(ref Guid sesion, int flags, out ISimpleAudioVolume volumen);
        // IAudioSessionManager2
        void GetSessionEnumerator(out IAudioSessionEnumerator enumerador);
        void RegisterSessionNotification(IntPtr aviso);
        void UnregisterSessionNotification(IntPtr aviso);
        void RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sesion, IntPtr aviso);
        void UnregisterDuckNotification(IntPtr aviso);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioSessionEnumerator
    {
        void GetCount(out int cantidad);
        void GetSession(int indice, out IAudioSessionControl sesion);
    }

    [ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioSessionControl
    {
        void GetState(out AudioSessionState estado);
        void GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string nombre);
        void SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string nombre, ref Guid contexto);
        void GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string ruta);
        void SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string ruta, ref Guid contexto);
        void GetGroupingParam(out Guid grupo);
        void SetGroupingParam(ref Guid grupo, ref Guid contexto);
        void RegisterAudioSessionNotification(IntPtr aviso);
        void UnregisterAudioSessionNotification(IntPtr aviso);
    }

    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioSessionControl2
    {
        // IAudioSessionControl
        void GetState(out AudioSessionState estado);
        void GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string nombre);
        void SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string nombre, ref Guid contexto);
        void GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string ruta);
        void SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string ruta, ref Guid contexto);
        void GetGroupingParam(out Guid grupo);
        void SetGroupingParam(ref Guid grupo, ref Guid contexto);
        void RegisterAudioSessionNotification(IntPtr aviso);
        void UnregisterAudioSessionNotification(IntPtr aviso);
        // IAudioSessionControl2
        void GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        void GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        void GetProcessId(out uint pid);
        /// <summary>S_OK (0) si es la sesión de los sonidos del sistema; S_FALSE (1) si no.</summary>
        [PreserveSig] int IsSystemSoundsSession();
        void SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool sinAtenuar);
    }

    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ISimpleAudioVolume
    {
        void SetMasterVolume(float nivel, ref Guid contexto);
        void GetMasterVolume(out float nivel);
        void SetMute([MarshalAs(UnmanagedType.Bool)] bool silencio, ref Guid contexto);
        void GetMute([MarshalAs(UnmanagedType.Bool)] out bool silencio);
    }

    [ComImport, Guid("C02216F6-8C05-4D06-8D56-7A4F6BA7A5B6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioMeterInformation
    {
        void GetPeakValue(out float pico);
        void GetMeteringChannelCount(out int canales);
        void GetChannelsPeakValues(int canales, IntPtr picos);
        void QueryHardwareSupport(out int soporte);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioEndpointVolume
    {
        void RegisterControlChangeNotify(IntPtr aviso);
        void UnregisterControlChangeNotify(IntPtr aviso);
        void GetChannelCount(out uint canales);
        void SetMasterVolumeLevel(float decibeles, ref Guid contexto);
        void SetMasterVolumeLevelScalar(float nivel, ref Guid contexto);
        void GetMasterVolumeLevel(out float decibeles);
        void GetMasterVolumeLevelScalar(out float nivel);
        void SetChannelVolumeLevel(uint canal, float decibeles, ref Guid contexto);
        void SetChannelVolumeLevelScalar(uint canal, float nivel, ref Guid contexto);
        void GetChannelVolumeLevel(uint canal, out float decibeles);
        void GetChannelVolumeLevelScalar(uint canal, out float nivel);
        void SetMute([MarshalAs(UnmanagedType.Bool)] bool silencio, ref Guid contexto);
        void GetMute([MarshalAs(UnmanagedType.Bool)] out bool silencio);
        void GetVolumeStepInfo(out uint paso, out uint pasos);
        void VolumeStepUp(ref Guid contexto);
        void VolumeStepDown(ref Guid contexto);
        void QueryHardwareSupport(out uint soporte);
        void GetVolumeRange(out float minimo, out float maximo, out float incremento);
    }
}
