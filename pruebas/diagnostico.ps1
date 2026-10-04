# Diagnóstico de Escritorio: reúne en un archivo lo que hace falta para revisar una prueba.
#   Clic derecho → Ejecutar con PowerShell, o:  powershell -ExecutionPolicy Bypass -File diagnostico.ps1
# Escribe %LOCALAPPDATA%\Escritorio\diagnostico.txt. Incluye nombres de aparatos Bluetooth y rutas del
# equipo: revíselo antes de compartirlo.

$ErrorActionPreference = 'Continue'
$carpeta = Join-Path $env:LOCALAPPDATA 'Escritorio'
$salida = Join-Path $carpeta 'diagnostico.txt'
New-Item -ItemType Directory -Force -Path $carpeta | Out-Null

$lineas = New-Object System.Collections.Generic.List[string]
function Titulo($texto) { $lineas.Add(''); $lineas.Add("== $texto =="); }
function Linea($texto) { $lineas.Add([string]$texto) }

if (-not ('Diag' -as [type])) {
Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class Diag
{
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice; }
    delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);
    delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFOEX info);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr hMonitor, int type, out uint dpiX, out uint dpiY);
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    public static bool DpiPorMonitor()
    {
        // -4 = PER_MONITOR_AWARE_V2: así las coordenadas son físicas, como en la app.
        return SetProcessDpiAwarenessContext(new IntPtr(-4));
    }

    public static List<string> Pantallas()
    {
        var lista = new List<string>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr m, IntPtr h, ref RECT r, IntPtr d) =>
        {
            var info = new MONITORINFOEX(); info.cbSize = Marshal.SizeOf(info);
            GetMonitorInfoW(m, ref info);
            uint dx, dy; GetDpiForMonitor(m, 0, out dx, out dy);
            lista.Add(string.Format("{0} principal={1} area=({2},{3}) {4}x{5} trabajo=({6},{7}) {8}x{9} escala={10:P0}",
                info.szDevice, (info.dwFlags & 1) != 0,
                info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Right - info.rcMonitor.Left, info.rcMonitor.Bottom - info.rcMonitor.Top,
                info.rcWork.Left, info.rcWork.Top, info.rcWork.Right - info.rcWork.Left, info.rcWork.Bottom - info.rcWork.Top,
                dx / 96.0));
            return true;
        }, IntPtr.Zero);
        return lista;
    }

    public static List<string> Ventanas(uint pid)
    {
        var lista = new List<string>();
        EnumWindows((hwnd, l) =>
        {
            uint p; GetWindowThreadProcessId(hwnd, out p);
            if (p != pid || GetAncestor(hwnd, 2) != hwnd) return true;
            var titulo = new StringBuilder(256); GetWindowTextW(hwnd, titulo, 256);
            RECT r; GetWindowRect(hwnd, out r);
            long ex = (long)GetWindowLongPtr(hwnd, -20);
            lista.Add(string.Format("'{0}' visible={1} pos=({2},{3}) {4}x{5} toolwindow={6} appwindow={7} dueño={8}",
                titulo, IsWindowVisible(hwnd), r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top,
                (ex & 0x80) != 0, (ex & 0x40000) != 0, GetWindow(hwnd, 4) != IntPtr.Zero));
            return true;
        }, IntPtr.Zero);
        return lista;
    }
}
'@
}

$dpiFisico = [Diag]::DpiPorMonitor()

Titulo 'Sistema'
Linea ("Fecha: " + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
$os = Get-CimInstance Win32_OperatingSystem
Linea ("Windows: $($os.Caption) $($os.Version) (compilación $($os.BuildNumber))")
$webview = foreach ($clave in 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
                               'HKCU:\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}') {
    if (Test-Path $clave) { (Get-ItemProperty $clave).pv }
}
Linea ("WebView2: " + $(if ($webview) { $webview -join ', ' } else { 'NO ENCONTRADO' }))

Titulo 'Pantallas'
Linea ("Coordenadas físicas: $dpiFisico (si es False, están escaladas al DPI del sistema)")
[Diag]::Pantallas() | ForEach-Object { Linea $_ }

Titulo 'Escritorio.exe'
$procesos = @(Get-Process -Name Escritorio -ErrorAction SilentlyContinue)
if ($procesos.Count -eq 0) { Linea 'No está en ejecución.' }
foreach ($p in $procesos) {
    Linea ("PID $($p.Id)  ruta: $($p.Path)  memoria: $([math]::Round($p.WorkingSet64 / 1MB)) MB")
    [Diag]::Ventanas([uint32]$p.Id) | ForEach-Object { Linea "  $_" }
}
$instalado = Join-Path $env:LOCALAPPDATA 'Programs\Escritorio\Escritorio.exe'
Linea ("Instalado en Programs: " + (Test-Path $instalado))
$desinstalar = 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Escritorio'
Linea ("Registrado en Configuración → Aplicaciones: " + (Test-Path $desinstalar))
$run = Get-ItemProperty 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run' -ErrorAction SilentlyContinue
Linea ("Inicio con Windows: " + $(if ($run -and $run.Escritorio) { $run.Escritorio } else { 'no' }))

foreach ($archivo in 'config.json', 'estado.json') {
    Titulo $archivo
    $ruta = Join-Path $carpeta $archivo
    if (Test-Path $ruta) { Get-Content $ruta -Encoding UTF8 | ForEach-Object { Linea $_ } } else { Linea '(no existe)' }
}

Titulo 'registro.log (últimas 60 líneas)'
$registro = Join-Path $carpeta 'registro.log'
if (Test-Path $registro) { Get-Content $registro -Encoding UTF8 -Tail 60 | ForEach-Object { Linea $_ } } else { Linea '(no existe)' }

Titulo 'Widgets integrados extraídos'
$integrados = Join-Path $carpeta 'integrados'
if (Test-Path $integrados) { Get-ChildItem $integrados -Recurse -File | ForEach-Object { Linea $_.FullName.Substring($carpeta.Length + 1) } } else { Linea '(no existe)' }

Titulo 'Bluetooth: aparatos y nivel de batería que publica Windows'
# La misma propiedad que lee la app: {104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2.
$nivelClave = '{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2'
$nodos = Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object { $_.InstanceId -like 'BTH*' }
if (-not $nodos) { Linea 'No hay dispositivos Bluetooth presentes.' }
foreach ($nodo in $nodos) {
    $nivel = (Get-PnpDeviceProperty -InstanceId $nodo.InstanceId -KeyName $nivelClave -ErrorAction SilentlyContinue).Data
    $contenedor = (Get-PnpDeviceProperty -InstanceId $nodo.InstanceId -KeyName 'DEVPKEY_Device_ContainerId' -ErrorAction SilentlyContinue).Data
    Linea ("{0}  [{1}]  estado={2}  bateria={3}  contenedor={4}" -f $nodo.FriendlyName, $nodo.InstanceId, $nodo.Status,
        $(if ($null -ne $nivel) { "$nivel%" } else { '-' }), $contenedor)
}

$lineas | Set-Content -Path $salida -Encoding UTF8
$lineas | ForEach-Object { Write-Host $_ }
Write-Host ''
Write-Host "Guardado en $salida" -ForegroundColor Cyan
if ($Host.Name -eq 'ConsoleHost') { Read-Host 'Pulse Enter para cerrar' | Out-Null }
