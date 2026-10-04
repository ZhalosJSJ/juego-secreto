# Escritorio

App nativa para Windows 11 que pone **widgets** en su escritorio, **en la segunda pantalla por defecto**, y **acomoda las ventanas de otros programas** donde usted quiera. Todo se agrupa en **escritorios**: distribuciones con nombre entre las que cambia con un clic. Incluye el **Gran Sabio**, un **reloj**, las **baterías** de sus aparatos Bluetooth y un **reproductor** con el volumen de cada programa.

Es un solo archivo, `Escritorio.exe`. No necesita instalar Python, .NET ni nada más: trae todo adentro y usa WebView2, que ya viene con Windows 11.

## Obtener el .exe

En GitHub, pestaña **Actions** → la última ejecución de **Compilar** → **Artifacts** → **Escritorio** (es un .zip con el .exe adentro). Cuando haya versiones publicadas, también estará en **Releases**.

La primera vez, Windows puede mostrar «Windows protegió su PC», porque el .exe no está firmado: **Más información → Ejecutar de todas formas**.

## Instalar

1. Abra `Escritorio.exe`. Aparece un ícono en la bandeja, junto al reloj.
2. Clic derecho en el ícono → **Instalar en este equipo**. Se copia a `%LOCALAPPDATA%\Programs\Escritorio`, aparece en el menú Inicio y en **Configuración → Aplicaciones** (desde ahí se desinstala). No pide permisos de administrador.
3. Si quiere que arranque solo: **Iniciar con Windows**.

**Actualizar:** abra el `Escritorio.exe` nuevo, nada más. Él cierra la copia que esté abierta, reemplaza la instalada, borra lo que sobre de la versión anterior y se vuelve a abrir desde la instalación (avisa con un globo). Si abre por error un `.exe` más viejo que el instalado, no lo degrada: abre el instalado. Para probar una compilación sin tocar la instalada: `Escritorio.exe --sin-actualizar`.

## El menú de la bandeja

| Opción | Qué hace |
|---|---|
| Escritorios | Cambia de escritorio (ver abajo), o vuelve al modo libre. |
| Pantalla de los widgets | Elige en qué pantalla van los widgets. |
| Bloquear posición | Oculta la franja de arrastre para no moverlos sin querer. |
| Restablecer posiciones | Los devuelve a su lugar inicial. |
| Recargar | Vuelve a leer `config.json` y a abrir los widgets. |
| Organizar ventanas | Ver abajo. |
| Iniciar con Windows | Arranca solo al iniciar sesión. |
| Abrir configuración / carpeta de widgets | Abre `config.json` o la carpeta de sus widgets propios. |
| Salir | Cierra todo. |

Para mover un widget, arrástrelo desde la franja delgada de su borde superior. Los widgets no aparecen en la barra de tareas ni en Alt+Tab, y vuelven detrás de las demás ventanas cuando deja de usarlos. Si conecta o desconecta una pantalla, se reacomodan solos.

## Escritorios

Un escritorio es una distribución con nombre: qué widgets se ven, cómo se acomodan, y dónde van las ventanas de otros programas. Se cambia desde la bandeja → **Escritorios**. La configuración por defecto trae dos:

- **Trabajo**: los widgets apilados en una columna a la derecha (reloj, reproductor, baterías y el Gran Sabio ocupando el resto), y Discord a la izquierda llenando todo el espacio que dejan.
- **Solo widgets**: todos los widgets, apilados a la derecha, sin reglas de ventanas.

**Libre (sin escritorio)** es el modo de siempre: cada widget en su `lado`, y solo las reglas generales.

Al cambiar a un escritorio se acomodan las ventanas que ya están abiertas y se abren los programas que tengan `abrir`. Al iniciar la app no se abre ni se mueve nada: solo se recuerda el último escritorio.

Así se escriben en `config.json`:

```json
"escritorios": {
  "trabajo": {
    "titulo": "Trabajo",
    "apilar": "derecha",
    "widgets": { "reloj": true, "reproductor": true, "baterias": true, "gran-sabio": true },
    "ventanas": [
      { "programa": "Discord.exe", "lado": "izquierda", "ancho": "100%", "alto": "100%", "abrir": "%APPDATA%\\Microsoft\\Windows\\Start Menu\\Programs\\Discord Inc\\Discord.lnk" }
    ]
  },
  "juego": {
    "titulo": "Juego",
    "pantalla": "principal",
    "widgets": { "baterias": { "lado": "abajo-derecha" }, "reloj": false }
  },
  "widgets": { "titulo": "Solo widgets", "apilar": "derecha" }
}
```

| Clave | Para qué sirve |
|---|---|
| `titulo` | Cómo aparece en el menú. |
| `pantalla` | Pantalla de los widgets en este escritorio. Si falta, la general. |
| `apilar` | `"derecha"` o `"izquierda"`: los widgets van en una columna de ese lado, en el orden en que están escritos, uno debajo del otro. Los de `alto: 0` se reparten lo que sobra. Sin `apilar`, cada widget va a su `lado`. |
| `widgets` | Cuáles se ven y en qué orden: `true`, `false`, o un objeto con `lado`, `ancho`, `alto` o `pantalla` propios. Si falta, se ven todos. |
| `ventanas` | Reglas de ventanas propias de este escritorio. Mandan sobre las generales. |

Dentro de un escritorio con `apilar`, las ventanas se reparten **el espacio que dejan los widgets**: `"ancho": "100%"` es «todo lo que queda al lado de la columna». Y `abrir` abre el programa (o un acceso directo del menú Inicio) si no está abierto al entrar al escritorio.

Las posiciones que arrastra a mano se recuerdan por escritorio. Un widget que haya movido deja de apilarse hasta que use **Restablecer posiciones**.

**Para cambiar con una tecla:** cree un acceso directo a `Escritorio.exe --escritorio trabajo` (o `--escritorio libre`), abra sus propiedades y asígnele una tecla de método abreviado.

## Organizar ventanas de otros programas

Para que, por ejemplo, Discord se abra siempre en la segunda pantalla:

1. Abra Discord y déjelo donde y del tamaño que lo quiere (o maximizado).
2. Bandeja → **Organizar ventanas → Recordar dónde está… → Discord**.

Desde entonces, cada vez que se abra, va a ese lugar. Solo se acomoda al aparecer: después puede moverlo libremente. Si está en un escritorio, el menú pregunta si la regla vale **solo en ese escritorio** o **en todos**.

- **Acomodar las abiertas ahora** aplica las reglas a lo que ya está abierto.
- **Olvidar regla** quita una.
- **Acomodar las nuevas automáticamente** las activa o desactiva todas.

Las reglas quedan en `config.json` y se pueden escribir a mano:

```json
"organizar": {
  "activo": true,
  "reglas": [
    { "programa": "Discord.exe", "pantalla": "secundaria", "x": 0, "y": 0, "ancho": 1280, "alto": 1392 },
    { "programa": "Spotify.exe", "pantalla": "secundaria", "maximizar": true },
    { "programa": "chrome.exe", "titulo": "YouTube Music", "pantalla": 2, "lado": "abajo-derecha" }
  ]
}
```

- `x` e `y` se miden en píxeles desde la esquina del espacio disponible de esa pantalla (el área de trabajo o, en un escritorio con widgets apilados, lo que queda junto a la columna).
- `ancho` y `alto` van en píxeles (`800`) o como porcentaje de ese espacio (`"60%"`). Lo que falte conserva el tamaño actual de la ventana.
- Sin `x` e `y`, la ventana se pone en su `lado`. Si no se indica, va al centro.
- `maximizar` ocupa toda la pantalla, también sobre los widgets.
- Las apps de la Tienda de Microsoft se distinguen por `titulo`.
- Windows no deja mover ventanas de programas abiertos como administrador (por ejemplo, el Administrador de tareas).

## Baterías

El widget muestra la batería de este equipo (si es portátil) y la de los aparatos Bluetooth conectados: audífonos, mouse, teclado, controles… Se actualiza cada minuto, o al hacerle clic.

Usa el mismo dato que muestra **Configuración → Bluetooth y dispositivos**. Para aparatos Bluetooth LE que no lo publican ahí, lo lee directamente del aparato. Si uno aparece con «sin dato», el aparato no informa su batería a Windows.

## Reproductor y volúmenes

El widget muestra **lo que se esté escuchando**, venga de donde venga: Spotify, YouTube en el navegador, el Reproductor multimedia de Windows… Usa el mismo control que las teclas de reproducción del teclado, así que no hay que configurar nada por app. Trae carátula, progreso (clic en la barra para saltar), anterior/pausa/siguiente, y las teclas **Espacio**, **←** y **→** cuando el widget tiene el foco. Si varias apps suenan a la vez, aparece un selector para elegir cuál mostrar.

Tiene **cinco diseños**; el botón ◐ del widget los va rotando y recuerda el último. También se fija en `config.json` con `"opciones": { "diseno": "vinilo" }`.

| Diseño | Cómo es | Tamaño sugerido |
|---|---|---|
| `tarjeta` | Carátula a la izquierda, datos a la derecha, barra y botones debajo. | 340 × 200 |
| `vinilo` | La carátula es un disco que gira mientras suena; tonos cálidos. | 340 × 200 |
| `minimo` | Una sola fila: carátula pequeña, título, pausa y siguiente. | 340 × 64 |
| `portada` | La carátula ocupa todo el fondo y los datos van encima. | 300 × 300 |
| `neon` | Negro con rosa y cian, fuente monoespaciada y ecualizador animado. | 340 × 200 |

El botón 🔊 abre el **mezclador**: volumen general y un deslizador por programa (con su ícono, silencio y un medidor de nivel), igual que el mezclador de Windows. Las apps que abren varias sesiones de sonido (Chrome) aparecen una sola vez y el cambio se aplica a todas.

## El Gran Sabio

Aparece a la derecha de la pantalla de los widgets, de arriba abajo.

- La primera vez muestra **Buscar iniciar.bat…**: indique el `iniciar.bat` del Gran Sabio. Desde entonces, si está apagado, lo inicia solo (con su consola minimizada) y sin abrir su ventana de navegador.
- El permiso del micrófono se concede solo, y solo a la dirección local del Gran Sabio.
- Para hablar, haga clic en el widget y mantenga Espacio, como siempre.
- Si lo abre con su propio acceso directo (Ctrl+Alt+G) y no quiere que se abra también en el navegador, ponga `NAVEGADOR=no` en el `.env` del Gran Sabio.

## Configuración (`config.json`)

Está en `%LOCALAPPDATA%\Escritorio\config.json` (bandeja → **Abrir configuración**). Después de editarlo, use **Recargar**.

```json
{
  "pantalla": "secundaria",
  "widgets": {
    "reloj": { "integrado": "reloj", "ancho": 280, "alto": 120, "lado": "arriba-izquierda" }
  }
}
```

**`pantalla`**: `"secundaria"` (la primera que no es la principal; si solo hay una, usa esa), `"principal"`, o un número: `1` es la de más a la izquierda.

Cada widget acepta:

| Clave | Para qué sirve |
|---|---|
| `integrado`, `archivo` o `url` | Qué muestra: un widget incluido (`reloj`, `baterias`, `reproductor`), un HTML de la carpeta de widgets, o una dirección. |
| `opciones` | Pares nombre-valor que la página del widget recibe en su dirección (`?diseno=vinilo`). Sirve también para widgets propios. |
| `titulo` | Su nombre. |
| `ancho`, `alto` | Tamaño en píxeles a escala 100 % (se ajusta solo a la escala de la pantalla). `alto: 0` ocupa todo el alto. |
| `lado` | Posición inicial: `derecha`, `izquierda`, `centro`, `arriba`, `abajo`, `arriba-derecha`, `arriba-izquierda`, `abajo-derecha`, `abajo-izquierda`. |
| `pantalla` | Pantalla propia de este widget. Si falta, la de los widgets. |
| `fijo` | `true` (por defecto): sin marco, fuera de la barra de tareas y al fondo. `false`: ventana normal. |
| `activo` | `false` lo desactiva sin borrarlo. |
| `iniciar` | Programa que se ejecuta si la `url` no responde. |
| `entorno` | Variables que recibe ese programa. |
| `fondo` | Color mientras carga, p. ej. `"#0b1220"`. |

## Crear un widget propio

1. Bandeja → **Abrir carpeta de widgets**. Cree una carpeta, por ejemplo `notas`, con un `index.html`.
2. En `config.json` agregue: `"notas": { "archivo": "notas/index.html", "ancho": 320, "alto": 240, "lado": "abajo-derecha" }`.
3. Bandeja → **Recargar**.

Su `localStorage` se conserva entre reinicios. Los widgets de `src/Escritorio/integrados/` sirven de ejemplo.

## Dónde guarda cada cosa

Todo queda en `%LOCALAPPDATA%\Escritorio`, nunca en el repositorio:

- `config.json`: su configuración.
- `estado.json`: posiciones de los widgets y el escritorio activo.
- `registro.log`: registro de errores; mírelo si algo no funciona.
- `widgets\`: sus widgets propios.
- `webview\`: datos del navegador interno.

## Limitaciones conocidas

- **Win+D** (mostrar escritorio) también oculta los widgets.
- Los widgets viven en la pantalla elegida: si arrastra uno a otra pantalla, al reiniciar vuelve al borde de la suya.

## Para desarrollar

Requiere el SDK de .NET 10. Se puede compilar desde Windows, Linux o macOS:

```
dotnet test pruebas/Escritorio.Pruebas                     # lógica: pantallas, posiciones, reglas, configuración
dotnet publish src/Escritorio -c Release -o publicado      # genera publicado/Escritorio.exe
```
