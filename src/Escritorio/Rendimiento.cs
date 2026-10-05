using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.Json;

namespace Escritorio;

/// <summary>
/// CPU, memoria y tarjeta de video cada segundo, para el widget «rendimiento». La GPU se lee con
/// la biblioteca del controlador NVIDIA (nvml.dll); sin ella, el widget muestra solo CPU y memoria.
/// </summary>
internal sealed class Rendimiento : IDisposable
{
    readonly Aplicacion app;
    System.Threading.Timer? reloj;
    long ultimoOcioso, ultimoTotal;
    bool nvmlListo, nvmlNoDisponible;
    IntPtr gpu;
    string? nombreGpu;

    public Rendimiento(Aplicacion app) => this.app = app;

    /// <summary>Solo se mide mientras haya un widget de rendimiento abierto.</summary>
    public void Activar(bool activo)
    {
        if (activo && reloj is null)
            reloj = new System.Threading.Timer(_ => Medir(), null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        else if (!activo)
            Dispose();
    }

    public void Dispose()
    {
        reloj?.Dispose();
        reloj = null;
        if (nvmlListo)
        {
            try
            {
                nvmlShutdown();
            }
            catch (Exception)
            {
            }
            nvmlListo = false;
        }
    }

    public void Pedir() => Medir();

    void Medir()
    {
        try
        {
            var json = JsonSerializer.Serialize(new { tipo = "rendimiento", cpu = Cpu(), ram = Ram(), gpu = Gpu() });
            app.EnUI(() => app.EnviarA("rendimiento", json));
        }
        catch (Exception error)
        {
            Registro.Error("No se pudo medir el rendimiento", error);
        }
    }

    /// <summary>Porcentaje de uso del procesador desde la medición anterior (la primera vez no hay dato).</summary>
    double? Cpu()
    {
        if (!GetSystemTimes(out var ocioso, out var nucleo, out var usuario))
            return null;
        long o = Largo(ocioso);
        long total = Largo(nucleo) + Largo(usuario);  // el tiempo de núcleo incluye el ocioso
        double? uso = null;
        if (ultimoTotal != 0 && total > ultimoTotal)
            uso = Math.Round(Math.Clamp(100.0 * (1 - (double)(o - ultimoOcioso) / (total - ultimoTotal)), 0, 100));
        ultimoOcioso = o;
        ultimoTotal = total;
        return uso;
    }

    static long Largo(FILETIME tiempo) => ((long)tiempo.dwHighDateTime << 32) | (uint)tiempo.dwLowDateTime;

    static object? Ram()
    {
        var estado = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref estado))
            return null;
        return new { usada = estado.ullTotalPhys - estado.ullAvailPhys, total = estado.ullTotalPhys };
    }

    object? Gpu()
    {
        if (nvmlNoDisponible)
            return null;
        try
        {
            if (!nvmlListo)
            {
                if (nvmlInit_v2() != 0 || nvmlDeviceGetHandleByIndex_v2(0, out gpu) != 0)
                {
                    nvmlNoDisponible = true;
                    return null;
                }
                var nombre = new StringBuilder(96);
                nombreGpu = nvmlDeviceGetName(gpu, nombre, (uint)nombre.Capacity) == 0 ? nombre.ToString() : "GPU";
                nvmlListo = true;
            }
            nvmlDeviceGetUtilizationRates(gpu, out var uso);
            nvmlDeviceGetMemoryInfo(gpu, out var memoria);
            nvmlDeviceGetTemperature(gpu, 0, out uint temperatura);
            return new { nombre = nombreGpu, uso = uso.gpu, vram = new { usada = memoria.used, total = memoria.total }, temperatura };
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            // Sin controlador NVIDIA (o con otra tarjeta): no se vuelve a intentar.
            nvmlNoDisponible = true;
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct NvmlUtilizacion
    {
        public uint gpu, memoria;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct NvmlMemoria
    {
        public ulong total, libre, used;
    }

    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX estado);
    [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out FILETIME ocioso, out FILETIME nucleo, out FILETIME usuario);

    [DllImport("nvml.dll")] static extern int nvmlInit_v2();
    [DllImport("nvml.dll")] static extern int nvmlShutdown();
    [DllImport("nvml.dll")] static extern int nvmlDeviceGetHandleByIndex_v2(uint indice, out IntPtr dispositivo);
    [DllImport("nvml.dll", CharSet = CharSet.Ansi)] static extern int nvmlDeviceGetName(IntPtr dispositivo, StringBuilder nombre, uint largo);
    [DllImport("nvml.dll")] static extern int nvmlDeviceGetUtilizationRates(IntPtr dispositivo, out NvmlUtilizacion uso);
    [DllImport("nvml.dll")] static extern int nvmlDeviceGetMemoryInfo(IntPtr dispositivo, out NvmlMemoria memoria);
    [DllImport("nvml.dll")] static extern int nvmlDeviceGetTemperature(IntPtr dispositivo, int sensor, out uint temperatura);
}
