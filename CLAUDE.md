# Escritorio — contexto del proyecto

App nativa para Windows 11 (C# / .NET 10, WinForms + WebView2) que pone widgets en la segunda pantalla por defecto, organiza ventanas de otros programas, muestra baterías de aparatos Bluetooth y trae un reproductor (lo que suene en cualquier app) con mezclador de volúmenes por programa. Todo se agrupa en «escritorios»: distribuciones con nombre (qué widgets, apilados o no, y reglas de ventanas propias) entre las que se cambia desde la bandeja o con `Escritorio.exe --escritorio <id>`. Uno de los widgets es la interfaz web del Gran Sabio (otro proyecto del usuario, en `http://localhost:8765`). El usuario habla español; responder y escribir siempre en español.

**El repositorio es público.** Nunca subir datos personales, rutas del equipo, claves de API ni de producto. La app guarda todo lo del equipo en `%LOCALAPPDATA%\Escritorio`, fuera del repositorio. No copiar aquí nada del `CLAUDE.md` ni del `.env` del Gran Sabio.

**Sin dependencias para el usuario**: se publica como un único `Escritorio.exe` autocontenido (`PublishSingleFile` + `SelfContained`). No agregar nada que obligue a instalar algo aparte (Python, runtime de .NET, Node…). WebView2 viene con Windows 11.

## Compilar y probar

- `dotnet test pruebas/Escritorio.Pruebas`: lógica pura (geometría, reglas, configuración). Corre en Linux: el proyecto de pruebas enlaza `Geometria.cs`, `Reglas.cs` y `Config.cs`, que no deben depender de WinForms ni de Windows.
- `dotnet publish src/Escritorio -c Release -o publicado`: genera el .exe. Compila también desde Linux (`EnableWindowsTargeting`), pero solo se puede ejecutar en Windows.
- `.github/workflows/compilar.yml` compila en `windows-latest` y sube el .exe como artefacto. Con una etiqueta `v*`, publica una versión.
- En las sesiones en la nube, el SDK se instala con `apt-get install dotnet-sdk-10.0` (las descargas de builds.dotnet.microsoft.com están bloqueadas; NuGet sí funciona).

## Arquitectura (`src/Escritorio`)

- `Program.cs`: instancia única (mutex `Local\Escritorio-widgets`), `--desinstalar`, `--reemplazar` (la copia recién instalada espera a que se cierre la anterior), `--escritorio <id|libre>` (si ya hay una copia abierta, se lo manda por `WM_COPYDATA` a la ventana oculta `Comandos` y sale).
- `Aplicacion.cs` (`ApplicationContext`): carga `config.json` y `estado.json`, calcula la `Distribucion` del escritorio actual (`Config.WidgetsDe` + `Geometria.Apilar`/`UbicarWidget`), deja en pantalla exactamente esos widgets (`AplicarEscritorio`: cierra los que sobran, crea los que faltan, recoloca los demás), el ícono de la bandeja y su menú (se reconstruye al abrirse), el organizador y las baterías. `EnUI` vuelve al hilo de la interfaz. `EspacioVentanas(pantalla)` = área de trabajo menos la columna de widgets apilados; es la base de las reglas de ventanas. `ReglasActivas()` = capas: las del escritorio actual y luego las generales.
- `VentanaWidget.cs`: un `Form` con `WebView2`. Si es «fijo»: sin marco, `WS_EX_TOOLWINDOW` en `CreateParams` (fuera de la barra de tareas y de Alt+Tab), `ShowWithoutActivation`, y en `WM_WINDOWPOSCHANGING` se fuerza `HWND_BOTTOM` mientras no está activo. Se arrastra desde una franja nativa (`WM_NCLBUTTONDOWN` + `HTCAPTION`); `OnResizeEnd` guarda la posición. Para widgets con `url`: espera a que el puerto responda, lanza `iniciar` si hace falta y muestra un aviso nativo con «Reintentar» y «Buscar iniciar.bat…».
- `Organizador.cs`: `SetWinEventHook(EVENT_OBJECT_SHOW/DESTROY)` en el hilo de la interfaz, sin sondeo. Cada ventana se acomoda una sola vez (400 ms después de aparecer). `SetWindowPos` dos veces por el cambio de escala entre pantallas. `AlEntrarA(escritorio)`: abre los programas con `abrir` que falten y acomoda todas las abiertas. «Recordar dónde está» guarda en el escritorio actual o en las generales, según elija el usuario.
- `Comandos.cs`: `Form` oculto titulado `Escritorio.Comandos`; recibe órdenes por `WM_COPYDATA` y sirve de invocador para el hilo de la interfaz.
- `Baterias.cs`: cada minuto, solo si hay un widget `baterias`. Aparatos conectados por AEP (`System.Devices.Aep.IsConnected`). Nivel desde la propiedad `{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2` de los nodos del dispositivo (unidos por `ContainerId` o por la dirección dentro del `DeviceInstanceId`). Para LE sin ese dato, GATT 0x180F/0x2A19 con tiempo límite. Envía JSON con `PostWebMessageAsJson`.
- `Reproductor.cs`: `GlobalSystemMediaTransportControlsSessionManager` (Windows.Media.Control). Muestra la sesión «actual» de Windows o la que el usuario elija (`elegida`). Se suscribe a `MediaPropertiesChanged`, `PlaybackInfoChanged` y `TimelinePropertiesChanged`, agrupa ráfagas (`enviando`/`pendiente`) y manda `{tipo:"reproductor", …}`; la carátula va solo cuando cambia (`caratulaNueva`), reducida a 320 px JPEG con System.Drawing. Acciones: alternar, reproducir, pausar, siguiente, anterior, ir (segundos), elegir (id de sesión). Activo solo si hay un widget `reproductor`.
- `Mezclador.cs`: Core Audio por COM (`CoreAudio`: IMMDeviceEnumerator, IAudioSessionManager2, IAudioSessionControl2, ISimpleAudioVolume, IAudioMeterInformation, IAudioEndpointVolume), sin paquetes. Sesiones agrupadas por nombre de proceso; nombre desde `FileVersionInfo.FileDescription`, ícono con `Icon.ExtractAssociatedIcon` (en caché). El dispositivo predeterminado se renueva cada 5 s. El widget lo consulta cada 300 ms solo con el panel abierto. Mensajes: `{volumen:{app, nivel}}`, `{silencio:{app, valor}}`, `{pedir:"mezclador"}`; «*» es el general.
- `Medios.cs`: ayudas puras (nombres legibles de apps, clave de carátula, nivel 0-1), con pruebas.
- `Instalacion.cs`: copia a `%LOCALAPPDATA%\Programs\Escritorio`, acceso del menú Inicio (`IShellLinkW`), clave `Uninstall` en HKCU (aparece en Configuración → Aplicaciones), inicio con Windows en `HKCU\...\Run`.
- `Geometria.cs`, `Reglas.cs`, `Config.cs`: lógica pura, con pruebas. `Config.WidgetsDe(escritorio)` resuelve qué widgets se ven y con qué ajustes; `Geometria.Apilar` arma la columna y `ZonaLibre` lo que queda; `Reglas.Destino(regla, espacio, actual)` admite `Medida` (píxeles o `"60%"` del espacio).
- `integrados/<nombre>/index.html`: widgets incluidos (reloj, baterías, reproductor), incrustados en el .exe y copiados al iniciar a `%LOCALAPPDATA%\Escritorio\integrados`. Se sirven como `https://app.escritorio/`; los propios, como `https://widgets.escritorio/`. Las `opciones` del widget van en la dirección como parámetros (`?diseno=vinilo`). El reproductor tiene cinco diseños (`tarjeta`, `vinilo`, `minimo`, `portada`, `neon`) por `body[data-diseno]`, con el elegido en `localStorage`.
- Los mensajes del widget a la app pasan por `VentanaWidget.MensajeRecibido` → `Aplicacion.Mensaje(ventana, JsonElement)`: `pedir`, `accion`, `volumen`, `silencio`.

## Decisiones (no cambiar sin motivo)

- **PerMonitorV2** y coordenadas físicas en todo: `Screen` y `SetWindowPos` hablan el mismo idioma. Los tamaños de `config.json` están a escala 100 % y se multiplican por la escala de la pantalla (`GetDpiForMonitor`). Las posiciones guardadas (`dx`, `dy`) son físicas y relativas al área de trabajo, con clave `escritorio/widget` (o `widget` en modo libre). Un widget con posición guardada no se apila.
- **Las reglas de ventanas se miden sobre el espacio disponible** (`EspacioVentanas`), no sobre toda la pantalla: así «Discord a la izquierda, 100 %» llena justo lo que dejan los widgets. `Recordar` usa la misma base. El escritorio activo vive en `estado.json`, no en `config.json`.
- **No se cancela el cierre de ventanas**: bloquearía el apagado de Windows.
- **Solo los widgets integrados** (`https://app.escritorio/`) pueden mandarle mensajes a la app, y solo ellos reciben datos.
- **Micrófono**: se concede solo al origen de la `url` del propio widget, y solo si es local (loopback).
- El Gran Sabio se inicia con `cmd /c start "" /min "<iniciar>"` y `entorno: {"NAVEGADOR": "no"}`. Su `load_dotenv` no pisa variables que ya existen, así que no abre su propia ventana de navegador.
- El paquete WebView2 agrega su control de WPF; el target `SinWebView2Wpf` del `.csproj` lo quita para evitar el conflicto con `WindowsBase`.

## Pendiente / ideas acordadas con el usuario

- Probar en Windows real: dos pantallas con escalas distintas, arrastre, micrófono, baterías de sus aparatos, reglas de ventanas, escritorios (apilado, cambio, `--escritorio`), reproductor (Spotify y navegador, carátulas, mezclador con Chrome y Discord), instalación y desinstalación.
- Posible mejora: atajos de teclado globales propios para cambiar de escritorio (hoy se hace con un acceso directo con tecla asignada).
- Si existe, revisar el proyecto anterior de baterías del usuario (no se encontró en GitHub, Drive ni artifacts) para igualar su método.
- Widgets del panel de Windows 11 (Win+W): proveedor con Windows App SDK + Adaptive Cards, empaquetado MSIX. Es otra pieza.
- Firmar el .exe (evita el aviso de SmartScreen).
