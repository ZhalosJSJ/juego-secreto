# Escritorio

App nativa para Windows 11 que pone **widgets** en su escritorio, **en la segunda pantalla por defecto**, y **acomoda las ventanas de otros programas** donde usted quiera. Incluye el **Gran Sabio**, un **reloj** y las **baterías** de sus aparatos Bluetooth.

Es un solo archivo, `Escritorio.exe`. No necesita instalar Python, .NET ni nada más: trae todo adentro y usa WebView2, que ya viene con Windows 11.

## Obtener el .exe

En GitHub, pestaña **Actions** → la última ejecución de **Compilar** → **Artifacts** → **Escritorio** (es un .zip con el .exe adentro). Cuando haya versiones publicadas, también estará en **Releases**.

La primera vez, Windows puede mostrar «Windows protegió su PC», porque el .exe no está firmado: **Más información → Ejecutar de todas formas**.

## Instalar

1. Abra `Escritorio.exe`. Aparece un ícono en la bandeja, junto al reloj.
2. Clic derecho en el ícono → **Instalar en este equipo**. Se copia a `%LOCALAPPDATA%\Programs\Escritorio`, aparece en el menú Inicio y en **Configuración → Aplicaciones** (desde ahí se desinstala). No pide permisos de administrador.
3. Si quiere que arranque solo: **Iniciar con Windows**.

## El menú de la bandeja

| Opción | Qué hace |
|---|---|
| Pantalla de los widgets | Elige en qué pantalla van los widgets. |
| Bloquear posición | Oculta la franja de arrastre para no moverlos sin querer. |
| Restablecer posiciones | Los devuelve a su lugar inicial. |
| Recargar | Vuelve a leer `config.json` y a abrir los widgets. |
| Organizar ventanas | Ver abajo. |
| Iniciar con Windows | Arranca solo al iniciar sesión. |
| Abrir configuración / carpeta de widgets | Abre `config.json` o la carpeta de sus widgets propios. |
| Salir | Cierra todo. |

Para mover un widget, arrástrelo desde la franja delgada de su borde superior. Los widgets no aparecen en la barra de tareas ni en Alt+Tab, y vuelven detrás de las demás ventanas cuando deja de usarlos. Si conecta o desconecta una pantalla, se reacomodan solos.

## Organizar ventanas de otros programas

Para que, por ejemplo, Discord se abra siempre en la segunda pantalla:

1. Abra Discord y déjelo donde y del tamaño que lo quiere (o maximizado).
2. Bandeja → **Organizar ventanas → Recordar dónde está… → Discord**.

Desde entonces, cada vez que se abra, va a ese lugar. Solo se acomoda al aparecer: después puede moverlo libremente.

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

- `x`, `y`, `ancho` y `alto` se miden en píxeles desde la esquina del área de trabajo de esa pantalla.
- Sin posición exacta, la ventana conserva su tamaño y se pone en su `lado`. Si no se indica, va al centro.
- Las apps de la Tienda de Microsoft se distinguen por `titulo`.
- Windows no deja mover ventanas de programas abiertos como administrador (por ejemplo, el Administrador de tareas).

## Baterías

El widget muestra la batería de este equipo (si es portátil) y la de los aparatos Bluetooth conectados: audífonos, mouse, teclado, controles… Se actualiza cada minuto, o al hacerle clic.

Usa el mismo dato que muestra **Configuración → Bluetooth y dispositivos**. Para aparatos Bluetooth LE que no lo publican ahí, lo lee directamente del aparato. Si uno aparece con «sin dato», el aparato no informa su batería a Windows.

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
| `integrado`, `archivo` o `url` | Qué muestra: un widget incluido (`reloj`, `baterias`), un HTML de la carpeta de widgets, o una dirección. |
| `titulo` | Su nombre. |
| `ancho`, `alto` | Tamaño en píxeles a escala 100 % (se ajusta solo a la escala de la pantalla). `alto: 0` ocupa todo el alto. |
| `lado` | Posición inicial: `derecha`, `izquierda`, `centro`, `arriba`, `abajo`, `arriba-derecha`, `arriba-izquierda`, `abajo-derecha`, `abajo-izquierda`. |
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
- `estado.json`: posiciones de los widgets.
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
