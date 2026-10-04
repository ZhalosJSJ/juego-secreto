"""Escritorio: widgets propios, fijos en la pantalla que elija (por defecto, la segunda).

Cada widget es una ventana de WebView2 sin marco que muestra una página: una dirección
(como la interfaz del Gran Sabio) o un archivo HTML de la carpeta widgets/.
La configuración vive en config.local.json; las posiciones, en .datos/estado.json.
Ninguno de los dos se sube al repositorio.
"""
from __future__ import annotations

import html
import json
import logging
import os
import shutil
import socket
import subprocess
import threading
import time
from pathlib import Path
from urllib.parse import urlparse

import ventanas
from ventanas import Rect

RAIZ = Path(__file__).resolve().parent
CONFIG = RAIZ / "config.local.json"
CONFIG_EJEMPLO = RAIZ / "config.ejemplo.json"
CARPETA_WIDGETS = RAIZ / "widgets"
DATOS = RAIZ / ".datos"
ESTADO = DATOS / "estado.json"
NOMBRE = "Escritorio"
ESPERA_SERVIDOR = 90  # segundos que se espera a un widget cuyo servidor se acaba de iniciar
INTERVALO = 0.4       # cada cuánto se revisan el foco y las posiciones
TOLERANCIA = 2        # píxeles: menos que esto no cuenta como «el usuario lo movió»
SW_SHOWMINNOACTIVE = 7

log = logging.getLogger("escritorio")

# Franja delgada arriba de cada widget para arrastrarlo (pywebview mueve la ventana
# al arrastrar cualquier elemento con la clase pywebview-drag-region).
ASA = """(() => {
  if (document.getElementById('escritorio-asa')) return;
  const asa = document.createElement('div');
  asa.id = 'escritorio-asa';
  asa.className = 'pywebview-drag-region';
  asa.title = 'Arrastre para mover el widget';
  asa.style.cssText = 'position:fixed;top:0;left:0;right:0;height:12px;z-index:2147483647;cursor:move;transition:background .15s';
  asa.addEventListener('mouseenter', () => { asa.style.background = 'rgba(134,230,255,.28)'; });
  asa.addEventListener('mouseleave', () => { asa.style.background = 'transparent'; });
  (document.body || document.documentElement).appendChild(asa);
})();"""
QUITAR_ASA = "document.getElementById('escritorio-asa')?.remove();"


def pagina(mensaje: str, reintentar: bool = False) -> str:
    boton = '<button onclick="pywebview.api.reintentar()">Reintentar</button>' if reintentar else ""
    return f"""<!doctype html><html lang="es"><meta charset="utf-8"><style>
html,body{{margin:0;height:100%;background:#0b1220;color:#cfe3ff;font:15px/1.5 "Segoe UI Variable","Segoe UI",system-ui,sans-serif}}
body{{display:grid;place-items:center;text-align:center;padding:24px;box-sizing:border-box}}
button{{margin-top:14px;padding:8px 16px;border:1px solid #2b4a72;border-radius:8px;background:#12233d;color:inherit;font:inherit;cursor:pointer}}
</style><div><p>{html.escape(mensaje)}</p>{boton}</div></html>"""


def leer_json(ruta: Path, defecto: dict) -> dict:
    try:
        return json.loads(ruta.read_text(encoding="utf-8"))
    except FileNotFoundError:
        return defecto


def escribir_json(ruta: Path, datos: dict) -> None:
    ruta.parent.mkdir(parents=True, exist_ok=True)
    temporal = ruta.with_name(ruta.name + ".tmp")
    temporal.write_text(json.dumps(datos, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    os.replace(temporal, ruta)


def cargar_config() -> dict:
    if not CONFIG.exists():
        shutil.copyfile(CONFIG_EJEMPLO, CONFIG)
        log.info("Se creó config.local.json a partir de config.ejemplo.json")
    config = leer_json(CONFIG, {})
    if not isinstance(config.get("widgets"), dict):
        raise ValueError("config.local.json no tiene la sección «widgets».")
    return config


def responde(url: str) -> bool:
    """¿Hay algo escuchando en el puerto de esa dirección?"""
    partes = urlparse(url)
    puerto = partes.port or (443 if partes.scheme == "https" else 80)
    try:
        with socket.create_connection((partes.hostname or "127.0.0.1", puerto), timeout=0.5):
            return True
    except OSError:
        return False


def cerca(a: Rect, b: Rect) -> bool:
    return abs(a.x - b.x) <= TOLERANCIA and abs(a.y - b.y) <= TOLERANCIA


def icono():
    from PIL import Image, ImageDraw

    imagen = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
    dibujo = ImageDraw.Draw(imagen)
    dibujo.polygon([(32, 4), (60, 32), (32, 60), (4, 32)], fill=(134, 230, 255, 255))
    dibujo.polygon([(32, 20), (44, 32), (32, 44), (20, 32)], fill=(11, 18, 32, 255))
    return imagen


class Widget:
    def __init__(self, ident: str, ajustes: dict) -> None:
        self.id = ident
        self.ajustes = ajustes
        self.titulo = str(ajustes.get("titulo") or ident)
        self.url = str(ajustes.get("url") or "")
        self.fijo = bool(ajustes.get("fijo", True))
        self.archivo: Path | None = None
        if not self.url:
            ruta = (RAIZ / str(ajustes.get("archivo", ""))).resolve()
            if CARPETA_WIDGETS.resolve() not in ruta.parents or not ruta.is_file():
                raise ValueError(f"El widget «{ident}» necesita una «url» o un «archivo» dentro de la carpeta widgets.")
            self.archivo = ruta
        self.ventana = None                 # webview.Window
        self.hwnd = 0                       # se conoce justo antes de mostrarla
        self.colocado: Rect | None = None   # la última posición que puso la app, no el usuario
        self.visto: Rect | None = None      # la posición en la revisión anterior
        self.era_activa = False


class ApiWidget:
    """Lo que la página de un widget puede pedirle a la app (window.pywebview.api)."""

    def __init__(self, app: Escritorio, widget: Widget) -> None:
        self._app = app
        self._widget = widget

    def reintentar(self) -> None:
        threading.Thread(target=self._app.conectar, args=(self._widget,), daemon=True).start()


class Escritorio:
    def __init__(self, config: dict) -> None:
        self.config = config
        self.estado = leer_json(ESTADO, {})
        self.estado.setdefault("posiciones", {})
        self.widgets = [
            Widget(ident, ajustes)
            for ident, ajustes in config["widgets"].items()
            if isinstance(ajustes, dict) and ajustes.get("activo", True)
        ]
        self.cerrando = False
        self.bandeja = None
        # Lo usan solo la bandeja y la revisión periódica. El hilo de las ventanas nunca lo
        # toma: SetWindowPos desde otro hilo espera a ese hilo, y así no hay bloqueo mutuo.
        self.cerrojo = threading.RLock()

    # --- Pantalla y posiciones ---

    @property
    def bloqueado(self) -> bool:
        return bool(self.estado.get("bloqueado", False))

    def pantalla(self) -> ventanas.Pantalla:
        return ventanas.elegir_pantalla(ventanas.listar_pantallas(), self.config.get("pantalla", "secundaria"))

    def rect_para(self, widget: Widget, pantalla: ventanas.Pantalla | None = None) -> Rect:
        pantalla = pantalla or self.pantalla()
        return ventanas.ubicar(pantalla.trabajo, widget.ajustes, self.estado["posiciones"].get(widget.id))

    def recolocar(self) -> None:
        with self.cerrojo:
            pantalla = self.pantalla()
            for widget in self.widgets:
                if widget.hwnd:
                    widget.colocado = widget.visto = self.rect_para(widget, pantalla)
                    ventanas.colocar(widget.hwnd, widget.colocado)

    def guardar_estado(self) -> None:
        escribir_json(ESTADO, self.estado)

    # --- Ventanas ---

    def crear_ventanas(self) -> None:
        import webview

        for widget in self.widgets:
            ventana = webview.create_window(
                widget.titulo,
                url=str(widget.archivo) if widget.archivo else None,
                html=None if widget.archivo else pagina(f"Esperando a {widget.titulo}…"),
                js_api=ApiWidget(self, widget),
                width=int(widget.ajustes.get("ancho", 400)),
                height=int(widget.ajustes.get("alto") or 600),
                x=0, y=0,  # posición manual; la real se pone con Win32 antes de mostrarla
                frameless=widget.fijo,
                easy_drag=False,
                text_select=bool(widget.ajustes.get("seleccionar_texto", False)),
                background_color=str(widget.ajustes.get("fondo", "#0b1220")),
            )
            widget.ventana = ventana
            ventana.events.before_show += self._antes_de_mostrar(widget)
            ventana.events.shown += self._al_mostrar(widget)
            ventana.events.loaded += self._al_cargar(widget)
            ventana.events.closed += self._al_cerrar(widget)

    def _antes_de_mostrar(self, widget: Widget):
        # pywebview llama esto en el hilo de la ventana, ya creada y todavía invisible.
        def manejar(window):
            hwnd = int(window.native.Handle.ToInt64())
            if widget.fijo:
                ventanas.fijar(hwnd)
            widget.colocado = widget.visto = self.rect_para(widget)
            ventanas.colocar(hwnd, widget.colocado)
            widget.hwnd = hwnd
        return manejar

    def _al_mostrar(self, widget: Widget):
        def manejar():
            if widget.fijo and widget.hwnd:
                ventanas.al_fondo(widget.hwnd)
        return manejar

    def _al_cargar(self, widget: Widget):
        def manejar():
            if not self.bloqueado:
                widget.ventana.evaluate_js(ASA)
        return manejar

    def _al_cerrar(self, widget: Widget):
        # No se impide cerrar un widget con Alt+F4: pywebview no distingue ese cierre del
        # apagado de Windows, y cancelarlo bloquearía el apagado.
        def manejar():
            widget.hwnd = 0
        return manejar

    # --- Arranque y conexión ---

    def arrancar(self) -> None:
        """pywebview lo llama en un hilo aparte cuando ya arrancó la interfaz."""
        self.iniciar_bandeja()
        threading.Thread(target=self.vigilar, daemon=True).start()
        for widget in self.widgets:
            if widget.url:
                threading.Thread(target=self.conectar, args=(widget,), daemon=True).start()

    def conectar(self, widget: Widget) -> None:
        """Carga la dirección del widget; si no responde, lo inicia (si sabe cómo) y espera."""
        widget.ventana.events.shown.wait(20)
        if not responde(widget.url):
            if widget.ajustes.get("iniciar"):
                widget.ventana.load_html(pagina(f"Iniciando {widget.titulo}…"))
                self.lanzar(widget)
            limite = time.monotonic() + ESPERA_SERVIDOR
            while not responde(widget.url):
                if self.cerrando:
                    return
                if time.monotonic() > limite:
                    widget.ventana.load_html(pagina(f"{widget.titulo} no responde en {widget.url}.", reintentar=True))
                    return
                time.sleep(1)
        widget.ventana.load_url(widget.url)

    def lanzar(self, widget: Widget) -> None:
        ruta = Path(os.path.expandvars(str(widget.ajustes["iniciar"])))
        ruta = ruta if ruta.is_absolute() else (RAIZ / ruta).resolve()
        if not ruta.is_file():
            log.warning("No se encontró %s para iniciar «%s»", ruta, widget.id)
            return
        entorno = {**os.environ, **{str(k): str(v) for k, v in widget.ajustes.get("entorno", {}).items()}}
        orden = ["cmd.exe", "/c", str(ruta)] if ruta.suffix.lower() in {".bat", ".cmd"} else [str(ruta)]
        inicio = subprocess.STARTUPINFO()
        inicio.dwFlags |= subprocess.STARTF_USESHOWWINDOW
        inicio.wShowWindow = SW_SHOWMINNOACTIVE  # su consola queda minimizada y sin robar el foco
        try:
            subprocess.Popen(
                orden, cwd=ruta.parent, env=entorno, startupinfo=inicio, creationflags=subprocess.CREATE_NEW_CONSOLE
            )
        except OSError:
            # Se sigue esperando: si no arranca, el widget muestra el aviso con «Reintentar».
            log.exception("No se pudo iniciar «%s» con %s", widget.id, ruta)
            return
        log.info("Se inició «%s» con %s", widget.id, ruta.name)

    # --- Revisión periódica: mantener al fondo y recordar posiciones ---

    def vigilar(self) -> None:
        while not self.cerrando:
            time.sleep(INTERVALO)
            try:
                self.revisar()
            except Exception:
                log.exception("Error al revisar los widgets")

    def revisar(self) -> None:
        with self.cerrojo:
            activa = ventanas.ventana_activa()
            trabajo = None
            cambios = False
            for widget in self.widgets:
                if not widget.hwnd:
                    continue
                es_activa = activa == widget.hwnd
                # Al dejar de usar un widget fijo, vuelve detrás de las demás ventanas.
                if widget.fijo and widget.era_activa and not es_activa:
                    ventanas.al_fondo(widget.hwnd)
                widget.era_activa = es_activa

                rect = ventanas.rect_ventana(widget.hwnd)
                quieto = rect is not None and widget.visto is not None and cerca(rect, widget.visto)
                widget.visto = rect
                # Se guarda cuando el usuario lo movió y ya lo soltó (dos revisiones sin cambio).
                if not quieto or widget.colocado is None or cerca(rect, widget.colocado):
                    continue
                trabajo = trabajo or self.pantalla().trabajo
                self.estado["posiciones"][widget.id] = {"dx": rect.x - trabajo.x, "dy": rect.y - trabajo.y}
                widget.colocado = rect
                cambios = True
            if cambios:
                self.guardar_estado()

    # --- Ícono de la bandeja ---

    def iniciar_bandeja(self) -> None:
        try:
            import pystray
        except Exception:
            log.exception("No se pudo cargar pystray: no habrá ícono en la bandeja")
            return
        menu = pystray.Menu(
            pystray.MenuItem("Pantalla de los widgets", pystray.Menu(self._opciones_pantalla)),
            pystray.MenuItem("Bloquear posición", self.alternar_bloqueo, checked=lambda _item: self.bloqueado),
            pystray.MenuItem("Restablecer posiciones", self.restablecer),
            pystray.MenuItem("Recargar widgets", self.recargar),
            pystray.Menu.SEPARATOR,
            pystray.MenuItem("Abrir configuración", self.abrir_config),
            pystray.MenuItem("Salir", self.salir),
        )
        self.bandeja = pystray.Icon("escritorio", icono(), NOMBRE, menu)
        threading.Thread(target=self.bandeja.run, daemon=True).start()

    def _opciones_pantalla(self):
        import pystray

        actual = self.pantalla().nombre
        for numero, pantalla in enumerate(ventanas.ordenar(ventanas.listar_pantallas()), 1):
            etiqueta = f"Pantalla {numero}" + (" (principal)" if pantalla.principal else "")
            yield pystray.MenuItem(
                etiqueta,
                self._elegir_pantalla(numero),
                checked=lambda _item, nombre=pantalla.nombre: nombre == actual,
                radio=True,
            )

    def _elegir_pantalla(self, numero: int):
        def elegir():
            self.config["pantalla"] = numero
            escribir_json(CONFIG, self.config)
            self.recolocar()
            self.bandeja.update_menu()
        return elegir

    def alternar_bloqueo(self) -> None:
        self.estado["bloqueado"] = not self.bloqueado
        self.guardar_estado()
        script = QUITAR_ASA if self.bloqueado else ASA
        for widget in self.widgets:
            if widget.hwnd:
                threading.Thread(target=widget.ventana.evaluate_js, args=(script,), daemon=True).start()
        self.bandeja.update_menu()

    def restablecer(self) -> None:
        with self.cerrojo:
            self.estado["posiciones"] = {}
            self.guardar_estado()
            self.recolocar()

    def recargar(self) -> None:
        for widget in self.widgets:
            if not widget.hwnd:
                continue
            if widget.url:
                threading.Thread(target=self.conectar, args=(widget,), daemon=True).start()
            else:
                threading.Thread(target=widget.ventana.evaluate_js, args=("location.reload()",), daemon=True).start()

    def abrir_config(self) -> None:
        os.startfile(CONFIG)

    def salir(self) -> None:
        self.cerrando = True
        for widget in self.widgets:
            if widget.hwnd:
                widget.ventana.destroy()

    def quitar_bandeja(self) -> None:
        if self.bandeja:
            self.bandeja.stop()


def main() -> int:
    DATOS.mkdir(exist_ok=True)
    logging.basicConfig(
        filename=DATOS / "registro.log",
        level=logging.INFO,
        format="%(asctime)s %(levelname)s %(name)s: %(message)s",
        encoding="utf-8",
    )
    if not ventanas.WINDOWS:
        print(f"{NOMBRE} funciona solo en Windows.")
        return 1
    if not ventanas.instancia_unica("Local\\escritorio-widgets"):
        ventanas.avisar(NOMBRE, "Ya está abierto. Búsquelo en el ícono de la bandeja, junto al reloj.")
        return 0
    try:
        import webview

        app = Escritorio(cargar_config())
        if not app.widgets:
            ventanas.avisar(NOMBRE, "No hay widgets activos en config.local.json.")
            return 0
        app.crear_ventanas()
    except Exception as error:
        log.exception("No se pudo iniciar")
        ventanas.avisar(NOMBRE, f"No se pudo iniciar:\n\n{error}")
        return 1
    # private_mode=False con una carpeta propia: el navegador interno recuerda los datos de
    # cada página (por ejemplo, el registro del Gran Sabio) y el permiso del micrófono.
    webview.start(app.arrancar, gui="edgechromium", private_mode=False, storage_path=str(DATOS / "webview"))
    # Se cerraron todas las ventanas (desde «Salir» o una por una): el ícono se va con ellas.
    app.cerrando = True
    app.quitar_bandeja()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
