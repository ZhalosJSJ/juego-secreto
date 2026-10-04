# Prueba manual en Windows

Guion para probar `Escritorio.exe` en un equipo real. Cada punto dice qué hacer y qué debería pasar. Anote lo que no coincida.

Al terminar (o cuando algo falle), ejecute `pruebas\diagnostico.ps1` (clic derecho → **Ejecutar con PowerShell**) y envíe el archivo que genera: `%LOCALAPPDATA%\Escritorio\diagnostico.txt`. Trae las pantallas, las ventanas de la app, la configuración, el registro de errores y los datos de batería que Windows publica. Revíselo antes de enviarlo: incluye nombres de sus aparatos Bluetooth y rutas de su equipo.

## 1. Primer arranque

1. Descargue **Escritorio** desde Actions → Artifacts, descomprima y abra `Escritorio.exe`. Si sale «Windows protegió su PC»: **Más información → Ejecutar de todas formas**.
2. Debe aparecer un ícono (rombo celeste) en la bandeja, junto al reloj.
3. En la **segunda pantalla** deben verse: el reloj arriba a la izquierda, las baterías abajo a la izquierda y, a la derecha de arriba abajo, un panel oscuro del Gran Sabio con el botón **Buscar iniciar.bat…**.
4. Si solo tiene una pantalla conectada, todo va a la principal.

Qué mirar: ¿aparecieron en la pantalla correcta? ¿Tienen el tamaño esperado (reloj ≈ 280×120 a escala 100 %)? ¿Las esquinas son redondeadas?

## 2. Comportamiento de widget fijo

1. Los widgets **no** deben aparecer en la barra de tareas ni al pulsar **Alt+Tab**.
2. Abra cualquier ventana (Explorador) encima de un widget: la ventana debe taparlo.
3. Haga clic en un widget y después en otra ventana: el widget debe volver **detrás** de las demás.
4. Pulse **Win+D**: los widgets se ocultan (limitación conocida). Vuelva a pulsar Win+D: reaparecen.

## 3. Arrastrar y recordar

1. Pase el mouse por el borde superior del reloj: debe iluminarse una franja delgada.
2. Arrástrelo desde ahí a otro lugar.
3. Bandeja → **Salir**, y vuelva a abrir `Escritorio.exe`: el reloj debe estar donde lo dejó.
4. Bandeja → **Bloquear posición**: la franja desaparece. Vuelva a pulsarlo: reaparece.
5. Bandeja → **Restablecer posiciones**: el reloj vuelve arriba a la izquierda.

## 4. Baterías

1. Con audífonos, mouse o control Bluetooth conectados, el widget debe listarlos con su porcentaje. Si es portátil, también «Este equipo».
2. Haga clic en el widget: se actualiza al momento (cambia la hora de la esquina).
3. Anote qué aparatos muestran porcentaje, cuáles dicen **sin dato** y cuáles no aparecen. Compare con Configuración → Bluetooth y dispositivos.

## 5. Gran Sabio

1. En el panel del Gran Sabio pulse **Buscar iniciar.bat…** y elija el `iniciar.bat` del Gran Sabio.
2. Debe iniciarse (su consola queda minimizada) y en unos segundos el widget muestra su interfaz. **No** debe abrirse además su ventana de navegador.
3. La primera vez pide permiso de micrófono: acéptelo. Haga clic en el widget, mantenga **Espacio** y hable: debe transcribir.
4. Cierre la consola del Gran Sabio: el widget debe mostrar «Iniciando Gran Sabio…» y volver a levantarlo solo.

## 6. Escritorios

1. Bandeja → **Escritorios → Trabajo**: los tres widgets deben quedar **en columna a la derecha** (reloj, baterías, Gran Sabio ocupando el resto), sin pisarse. Globo: «Escritorio «Trabajo»: N ventanas acomodadas».
2. Abra Discord (si lo usa): debe irse a la **izquierda**, ocupando todo el espacio que dejan los widgets, sin tapar la columna.
3. **Escritorios → Solo widgets**: misma columna, pero Discord ya no se mueve al abrirse.
4. **Escritorios → Libre**: cada widget vuelve a su lado (reloj arriba a la izquierda, etc.).
5. En **Trabajo**, arrastre el reloj fuera de la columna: baterías y Gran Sabio deben cerrar el hueco. **Restablecer posiciones** lo devuelve a la columna.
6. Abra `cmd` y ejecute `Escritorio.exe --escritorio widgets` (con la ruta completa del .exe): debe cambiar de escritorio sin abrir una segunda copia.

## 7. Organizar ventanas

1. Abra el Bloc de notas, póngalo donde quiera en la segunda pantalla.
2. Bandeja → **Organizar ventanas → Recordar dónde está… → notepad — …** (en un escritorio pregunta: elija **En todos los escritorios**).
3. Cierre el Bloc de notas y vuelva a abrirlo: debe aparecer en ese mismo lugar.
4. **Organizar ventanas → Olvidar regla → Siempre: notepad.exe**. Al abrirlo de nuevo, ya no se mueve.
5. Pruebe lo mismo con una app de la Tienda (Calculadora): la regla debe incluir su título.

## 8. Pantallas

1. Bandeja → **Pantalla de los widgets → Pantalla 1**: los widgets deben pasar a esa pantalla.
2. Si sus pantallas tienen escalas distintas (100 % y 125 %, por ejemplo): al cambiar de pantalla el tamaño de los widgets debe verse proporcional, no diminuto ni gigante.
3. Desconecte la segunda pantalla (o apáguela desde Configuración): los widgets deben pasar a la principal. Al reconectarla, vuelven.

## 9. Instalación

1. Bandeja → **Instalar en este equipo**. La app se cierra y vuelve a abrirse sola (desde `%LOCALAPPDATA%\Programs\Escritorio`). El menú ya no muestra «Instalar en este equipo».
2. Debe existir **Escritorio** en el menú Inicio y en **Configuración → Aplicaciones → Aplicaciones instaladas**.
3. **Iniciar con Windows**: márquelo, cierre sesión y vuelva a entrar: la app debe arrancar sola.
4. **Actualizar:** con la app instalada y abierta, abra un `Escritorio.exe` nuevo (otra compilación) desde Descargas. La app abierta debe cerrarse, y en unos segundos volver a abrirse con el globo «Escritorio se actualizó a la versión …». En `%LOCALAPPDATA%\Programs\Escritorio` debe quedar solo el `.exe` nuevo, y Configuración → Aplicaciones mostrar la versión nueva.
5. Abra de nuevo el mismo `.exe` de Descargas: no debe pasar nada raro (si la app está abierta, sigue; si no, se abre la instalada).
6. Abra un `.exe` más viejo: debe avisar que ya hay una versión más nueva y abrir la instalada.
7. Para desinstalar: bandeja → Salir, y después Configuración → Aplicaciones → Escritorio → Desinstalar. Debe quitar el acceso del menú Inicio y la carpeta de programa; la configuración queda en `%LOCALAPPDATA%\Escritorio`.

## 10. Reproductor y volúmenes

1. Sin nada sonando, el widget dice «Nada en reproducción» con los botones apagados.
2. Reproduzca algo en Spotify: deben aparecer título, artista, álbum, carátula y la barra avanzando. Pausa, siguiente y anterior deben funcionar; clic en la barra salta a ese punto.
3. Reproduzca un video de YouTube en el navegador con Spotify en pausa: debe aparecer un selector con las dos apps; elija cada una.
4. Pulse ◐ cinco veces: tarjeta → vinilo (el disco gira) → mínimo → portada (carátula de fondo) → neón (ecualizador) → tarjeta. Cierre y abra la app: conserva el último.
5. Pulse 🔊: lista «General» y los programas con sonido, con ícono y medidor moviéndose. Baje el volumen de Chrome o Discord: debe cambiar solo ese programa (compárelo con el mezclador de Windows). Pruebe silenciar y volver a activar. Esc o ✕ cierra el panel.
6. En `config.json`, ponga `"opciones": { "diseno": "portada" }` al reproductor, con `"ancho": 300, "alto": 300`, y **Recargar**: debe abrir con ese diseño.

## Si algo falla

- Errores: `%LOCALAPPDATA%\Escritorio\registro.log`.
- Para volver a empezar de cero: Salir, borrar la carpeta `%LOCALAPPDATA%\Escritorio` y abrir de nuevo.
