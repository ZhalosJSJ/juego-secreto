# Escritorio — contexto del proyecto

App de widgets propios para Windows 11, con la segunda pantalla como destino por defecto. Uno de los widgets es la interfaz web del Gran Sabio (otro proyecto del usuario, que corre en `http://localhost:8765`). El usuario habla español; responder y escribir siempre en español.

**El repositorio es público.** Nunca subir datos personales, rutas del equipo, claves de API ni de producto. Todo eso va en `config.local.json` o en `.datos/`, que están en `.gitignore`. No copiar aquí nada del `CLAUDE.md` ni del `.env` del Gran Sabio.

## Arquitectura

- `escritorio.py`: la app. Lee `config.local.json` (lo crea desde `config.ejemplo.json`), crea una ventana de pywebview (WebView2) por widget, pone el ícono de la bandeja (pystray) y revisa cada 0,4 s el foco y las posiciones.
- `ventanas.py`: geometría pura (elegir pantalla, `rect_inicial`, `dentro`, `ubicar`), con pruebas en `pruebas/`, y Win32 con ctypes (`listar_pantallas`, `fijar`, `colocar`, `al_fondo`, `instancia_unica`, `avisar`).
- `widgets/<nombre>/index.html`: widgets de archivo. Solo se aceptan archivos dentro de `widgets/`.
- `.datos/estado.json`: posiciones (`dx`, `dy` relativas al área de trabajo de la pantalla de los widgets) y `bloqueado`. `.datos/registro.log`: registro (la app corre con `pythonw` y no tiene consola). `.datos/webview/`: perfil de WebView2 (`private_mode=False`, para conservar `localStorage` y el permiso del micrófono).

## Decisiones (no cambiar sin motivo)

- **Coordenadas siempre con Win32**, no con las de pywebview: pywebview declara el proceso consciente del DPI del sistema, y `EnumDisplayMonitors` y `SetWindowPos` comparten ese espacio. Las conversiones de escala de pywebview no son confiables con pantallas mixtas.
- **Estilo y posición en `events.before_show`**: pywebview lo dispara en el hilo de la ventana, ya creada y todavía invisible. Ahí se aplica `WS_EX_TOOLWINDOW` (fuera de la barra de tareas y de Alt+Tab, sin parpadeo) y `SetWindowPos`. Se pasa `x=0, y=0` a `create_window` para que WinForms use `StartPosition.Manual` y no la recentre al mostrarla.
- **El cerrojo (`Escritorio.cerrojo`) nunca se toma en el hilo de la ventana**: `SetWindowPos` desde otro hilo espera a ese hilo, y se bloquearían mutuamente.
- **No se cancela el cierre de ventanas**: pywebview no informa el motivo del cierre, y cancelar también bloquearía el apagado de Windows.
- `easy_drag=False` y una franja `.pywebview-drag-region` inyectada en cada página (`ASA`), para que arrastrar no interfiera con la página.
- El Gran Sabio se inicia con `entorno: {"NAVEGADOR": "no"}`: su `load_dotenv` no pisa variables que ya existen, así no abre su propia ventana.
- `text_select` es `False` por defecto en pywebview: el widget del Gran Sabio usa `seleccionar_texto: true` para poder copiar del registro.

## Pendiente / ideas acordadas con el usuario

- Probar en Windows real: dos pantallas con escalas distintas, arrastre, micrófono, Win+D.
- Arranque automático con Windows.
- Widgets del panel de Windows 11 (Win+W): proveedor con Windows App SDK + Adaptive Cards, empaquetado MSIX. Es otra pieza (C#), no se hace con pywebview.
- Decidir si se agrega «organizar otras apps» (reglas para mandar ventanas de otros programas a la segunda pantalla).
- Fase 2: versión más integrada y minimalista (posible paso a Tauri; los widgets HTML se reutilizan).
- Archivos del juego anterior (`index.html`, `app.js`, `style.css`, `.DS_Store`): pendientes de borrar cuando el usuario lo confirme.
