"""Pantallas y ventanas de Windows, y dónde va cada widget.

La geometría (elegir pantalla, calcular posiciones) es pura y se prueba en cualquier
sistema. Lo demás usa la API Win32 con ctypes y solo funciona en Windows.

Todas las coordenadas están en el espacio del proceso: pywebview lo declara consciente
del DPI del sistema, así que EnumDisplayMonitors y SetWindowPos hablan el mismo idioma.
"""
from __future__ import annotations

import ctypes
import sys
from dataclasses import dataclass

WINDOWS = sys.platform == "win32"
MARGEN = 12
LADOS = (
    "derecha", "izquierda", "centro", "arriba", "abajo",
    "arriba-derecha", "arriba-izquierda", "abajo-derecha", "abajo-izquierda",
)


@dataclass(frozen=True)
class Rect:
    x: int
    y: int
    ancho: int
    alto: int


@dataclass(frozen=True)
class Pantalla:
    nombre: str      # p. ej. \\.\DISPLAY2
    area: Rect       # la pantalla completa
    trabajo: Rect    # sin la barra de tareas
    principal: bool


def ordenar(pantallas: list[Pantalla]) -> list[Pantalla]:
    """De izquierda a derecha: «pantalla 1» es la de más a la izquierda."""
    return sorted(pantallas, key=lambda p: (p.area.x, p.area.y))


def elegir_pantalla(pantallas: list[Pantalla], preferida: str | int = "secundaria") -> Pantalla:
    """«secundaria» (la primera que no es la principal), «principal» o un número desde 1.

    Si la pedida no existe (por ejemplo, se desconectó la segunda pantalla), se usa la principal.
    """
    if not pantallas:
        raise ValueError("No se encontró ninguna pantalla.")
    ordenadas = ordenar(pantallas)
    principal = next((p for p in ordenadas if p.principal), ordenadas[0])
    pedida = str(preferida).strip().lower()
    if pedida == "principal":
        return principal
    if pedida.isdigit():
        indice = int(pedida) - 1
        return ordenadas[indice] if 0 <= indice < len(ordenadas) else principal
    return next((p for p in ordenadas if not p.principal), principal)


def rect_inicial(trabajo: Rect, ancho: int, alto: int, lado: str) -> Rect:
    """Tamaño y posición por defecto. alto=0 ocupa todo el alto disponible."""
    libre_ancho = max(trabajo.ancho - 2 * MARGEN, 100)
    libre_alto = max(trabajo.alto - 2 * MARGEN, 100)
    ancho = min(ancho or 400, libre_ancho)
    alto = min(alto, libre_alto) if alto else libre_alto
    izquierda = trabajo.x + MARGEN
    derecha = trabajo.x + trabajo.ancho - ancho - MARGEN
    arriba = trabajo.y + MARGEN
    abajo = trabajo.y + trabajo.alto - alto - MARGEN
    centro_x = trabajo.x + (trabajo.ancho - ancho) // 2
    centro_y = trabajo.y + (trabajo.alto - alto) // 2
    posiciones = {
        "derecha": (derecha, centro_y),
        "izquierda": (izquierda, centro_y),
        "centro": (centro_x, centro_y),
        "arriba": (centro_x, arriba),
        "abajo": (centro_x, abajo),
        "arriba-derecha": (derecha, arriba),
        "arriba-izquierda": (izquierda, arriba),
        "abajo-derecha": (derecha, abajo),
        "abajo-izquierda": (izquierda, abajo),
    }
    x, y = posiciones.get(lado, posiciones["derecha"])
    return Rect(x, y, ancho, alto)


def dentro(rect: Rect, trabajo: Rect) -> Rect:
    """Mueve (y si hace falta achica) el rectángulo para que quede entero en el área de trabajo."""
    ancho = min(rect.ancho, trabajo.ancho)
    alto = min(rect.alto, trabajo.alto)
    x = min(max(rect.x, trabajo.x), trabajo.x + trabajo.ancho - ancho)
    y = min(max(rect.y, trabajo.y), trabajo.y + trabajo.alto - alto)
    return Rect(x, y, ancho, alto)


def ubicar(trabajo: Rect, ajustes: dict, guardado: dict | None = None) -> Rect:
    """Dónde va un widget: su posición guardada (relativa a la pantalla) o la de su «lado»."""
    rect = rect_inicial(
        trabajo,
        int(ajustes.get("ancho", 400)),
        int(ajustes.get("alto", 0)),
        str(ajustes.get("lado", "derecha")),
    )
    if guardado and "dx" in guardado and "dy" in guardado:
        rect = Rect(trabajo.x + int(guardado["dx"]), trabajo.y + int(guardado["dy"]), rect.ancho, rect.alto)
    return dentro(rect, trabajo)


if WINDOWS:
    from ctypes import wintypes

    _user32 = ctypes.WinDLL("user32", use_last_error=True)
    _kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)

    GWL_EXSTYLE = -20
    WS_EX_TOOLWINDOW = 0x00000080
    WS_EX_APPWINDOW = 0x00040000
    SWP_NOSIZE = 0x0001
    SWP_NOMOVE = 0x0002
    SWP_NOZORDER = 0x0004
    SWP_NOACTIVATE = 0x0010
    HWND_BOTTOM = 1
    MONITORINFOF_PRIMARY = 1
    ERROR_ALREADY_EXISTS = 183
    MB_ICONWARNING = 0x30

    class _MONITORINFOEXW(ctypes.Structure):
        _fields_ = [
            ("cbSize", wintypes.DWORD),
            ("rcMonitor", wintypes.RECT),
            ("rcWork", wintypes.RECT),
            ("dwFlags", wintypes.DWORD),
            ("szDevice", wintypes.WCHAR * 32),
        ]

    _MONITORENUMPROC = ctypes.WINFUNCTYPE(
        wintypes.BOOL, wintypes.HMONITOR, wintypes.HDC, ctypes.POINTER(wintypes.RECT), wintypes.LPARAM
    )
    _user32.EnumDisplayMonitors.argtypes = [wintypes.HDC, ctypes.c_void_p, _MONITORENUMPROC, wintypes.LPARAM]
    _user32.GetMonitorInfoW.argtypes = [wintypes.HMONITOR, ctypes.POINTER(_MONITORINFOEXW)]
    _user32.SetWindowPos.argtypes = [
        wintypes.HWND, wintypes.HWND, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int, wintypes.UINT
    ]
    _user32.GetWindowRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
    _user32.GetForegroundWindow.restype = wintypes.HWND
    _user32.IsIconic.argtypes = [wintypes.HWND]
    _user32.IsWindow.argtypes = [wintypes.HWND]
    _user32.MessageBoxW.argtypes = [wintypes.HWND, wintypes.LPCWSTR, wintypes.LPCWSTR, wintypes.UINT]
    _kernel32.CreateMutexW.argtypes = [ctypes.c_void_p, wintypes.BOOL, wintypes.LPCWSTR]
    _kernel32.CreateMutexW.restype = wintypes.HANDLE

    # En Python de 32 bits no existen las variantes «Ptr».
    _leer_estilo = getattr(_user32, "GetWindowLongPtrW", _user32.GetWindowLongW)
    _escribir_estilo = getattr(_user32, "SetWindowLongPtrW", _user32.SetWindowLongW)
    _leer_estilo.argtypes = [wintypes.HWND, ctypes.c_int]
    _leer_estilo.restype = ctypes.c_ssize_t
    _escribir_estilo.argtypes = [wintypes.HWND, ctypes.c_int, ctypes.c_ssize_t]
    _escribir_estilo.restype = ctypes.c_ssize_t

    _mutex = None  # se conserva mientras viva el proceso

    def _rect(r: wintypes.RECT) -> Rect:
        return Rect(r.left, r.top, r.right - r.left, r.bottom - r.top)

    def listar_pantallas() -> list[Pantalla]:
        pantallas: list[Pantalla] = []

        def visitar(monitor, _hdc, _rect_ptr, _dato):
            info = _MONITORINFOEXW()
            info.cbSize = ctypes.sizeof(info)
            if _user32.GetMonitorInfoW(monitor, ctypes.byref(info)):
                pantallas.append(Pantalla(
                    info.szDevice,
                    _rect(info.rcMonitor),
                    _rect(info.rcWork),
                    bool(info.dwFlags & MONITORINFOF_PRIMARY),
                ))
            return True

        _user32.EnumDisplayMonitors(None, None, _MONITORENUMPROC(visitar), 0)
        return pantallas

    def fijar(hwnd: int) -> None:
        """Sin botón en la barra de tareas ni entrada en Alt+Tab (ventana de herramientas).

        Debe hacerse antes de mostrar la ventana: así la barra de tareas nunca la registra.
        """
        estilo = _leer_estilo(hwnd, GWL_EXSTYLE)
        _escribir_estilo(hwnd, GWL_EXSTYLE, (estilo | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW)

    def colocar(hwnd: int, rect: Rect) -> None:
        _user32.SetWindowPos(hwnd, None, rect.x, rect.y, rect.ancho, rect.alto, SWP_NOZORDER | SWP_NOACTIVATE)

    def al_fondo(hwnd: int) -> None:
        """Detrás de todas las demás ventanas, sin robar el foco."""
        _user32.SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE)

    def ventana_activa() -> int:
        return _user32.GetForegroundWindow() or 0

    def rect_ventana(hwnd: int) -> Rect | None:
        """Posición actual, o None si la ventana ya no existe o está minimizada."""
        if not _user32.IsWindow(hwnd) or _user32.IsIconic(hwnd):
            return None
        r = wintypes.RECT()
        if not _user32.GetWindowRect(hwnd, ctypes.byref(r)):
            return None
        return _rect(r)

    def instancia_unica(nombre: str) -> bool:
        """False si ya hay otra copia de la app abierta en esta sesión de Windows."""
        global _mutex
        _mutex = _kernel32.CreateMutexW(None, False, nombre)
        return ctypes.get_last_error() != ERROR_ALREADY_EXISTS

    def avisar(titulo: str, mensaje: str) -> None:
        """Cuadro de aviso: la app corre con pythonw y no tiene consola donde imprimir."""
        _user32.MessageBoxW(None, mensaje, titulo, MB_ICONWARNING)
