# Escritorio

Widgets propios para el escritorio de Windows 11, con control sobre **dónde** aparece cada uno. Si tiene dos pantallas, los widgets van a la **segunda** por defecto.

Cada widget es una ventanita sin marco que muestra una página web: una dirección local (como la interfaz del **Gran Sabio**) o un archivo HTML de la carpeta `widgets/`. Los widgets *fijos* no aparecen en la barra de tareas ni en Alt+Tab, y vuelven detrás de las demás ventanas cuando deja de usarlos.

## Requisitos

- Windows 11 (trae WebView2, el motor que dibuja los widgets).
- Python 3.11 o posterior, con el comando `py` disponible.

## Iniciar

Haga doble clic en **iniciar.bat**. La primera vez prepara el entorno (tarda un minuto) y crea `config.local.json` a partir de `config.ejemplo.json`.

Los widgets se controlan desde el ícono de la bandeja, junto al reloj:

| Opción | Qué hace |
|---|---|
| Pantalla de los widgets | Mueve todos los widgets a la pantalla elegida y la recuerda. |
| Bloquear posición | Quita la franja de arrastre para no moverlos sin querer. |
| Restablecer posiciones | Olvida dónde los dejó y los devuelve a su lugar inicial. |
| Recargar widgets | Vuelve a cargar cada widget (y reinicia el Gran Sabio si está apagado). |
| Abrir configuración | Abre `config.local.json`. Tras editarlo, use **Salir** y vuelva a iniciar. |
| Salir | Cierra todos los widgets. |

Para mover un widget, pase el mouse por su borde superior: aparece una franja; arrástrela.

## Configuración (`config.local.json`)

```json
{
  "pantalla": "secundaria",
  "widgets": {
    "reloj": { "archivo": "widgets/reloj/index.html", "ancho": 280, "alto": 120, "lado": "arriba-izquierda" }
  }
}
```

**`pantalla`**: `"secundaria"` (la primera que no es la principal; si solo hay una, usa esa), `"principal"`, o un número: `1` es la de más a la izquierda.

Cada widget acepta:

| Clave | Para qué sirve |
|---|---|
| `url` o `archivo` | Qué muestra: una dirección, o un HTML dentro de `widgets/`. |
| `titulo` | Nombre del widget (se ve en los avisos). |
| `ancho`, `alto` | Tamaño en píxeles. `alto: 0` ocupa todo el alto de la pantalla. |
| `lado` | Posición inicial: `derecha`, `izquierda`, `centro`, `arriba`, `abajo`, `arriba-derecha`, `arriba-izquierda`, `abajo-derecha`, `abajo-izquierda`. |
| `fijo` | `true` (por defecto): sin marco, fuera de la barra de tareas y al fondo. `false`: ventana normal. |
| `activo` | `false` lo desactiva sin borrarlo. |
| `iniciar` | Programa que se ejecuta si la `url` no responde (ruta relativa a esta carpeta o absoluta). |
| `entorno` | Variables que recibe ese programa. |
| `seleccionar_texto` | Permite seleccionar y copiar texto en el widget. |
| `fondo` | Color mientras carga, p. ej. `"#0b1220"`. |

Las posiciones que usted elige arrastrando se guardan en `.datos/estado.json`, relativas a la pantalla de los widgets: si cambia de pantalla, conservan su lugar.

## El Gran Sabio como widget

La configuración de ejemplo ya lo incluye: muestra `http://localhost:8765` a la derecha de la segunda pantalla, de arriba abajo.

- Si el Gran Sabio está apagado, lo inicia con `../gran-sabio/iniciar.bat` (es decir, espera que las dos carpetas estén una al lado de la otra; si no, cambie `iniciar`). Su consola queda minimizada, como siempre.
- Al iniciarlo le pasa `NAVEGADOR=no`, para que no abra además su ventana de navegador.
- Si a veces lo abre con su propio acceso directo (Ctrl+Alt+G) y no quiere que aparezca también en el navegador, ponga `NAVEGADOR=no` en el `.env` del Gran Sabio.
- La primera vez, el widget pide permiso para usar el micrófono: acéptelo. Queda guardado.
- Para hablar: haga clic en el widget y mantenga Espacio, igual que en la ventana de siempre.

## Crear un widget propio

1. Cree una carpeta en `widgets/`, por ejemplo `widgets/notas/`, con un `index.html`.
2. Agréguelo en `config.local.json`:

   ```json
   "notas": { "archivo": "widgets/notas/index.html", "ancho": 320, "alto": 240, "lado": "abajo-izquierda" }
   ```

3. **Salir** desde la bandeja y vuelva a iniciar.

`widgets/reloj/` sirve de ejemplo.

## Limitaciones conocidas

- **Win+D** (mostrar escritorio) también oculta los widgets.
- Los widgets de archivo no conservan `localStorage` entre reinicios (pywebview les asigna un puerto distinto cada vez). Los de `url`, como el Gran Sabio, sí.
- Los widgets viven en la pantalla elegida: si arrastra uno a otra pantalla, al reiniciar vuelve al borde de la suya.

## Privacidad

El repositorio es público. `config.local.json` (rutas de su equipo) y `.datos/` (posiciones, registro y datos del navegador interno) están en `.gitignore` y nunca se suben. No ponga claves ni datos personales en `config.ejemplo.json` ni en los widgets.

## Pruebas

```
python -m unittest discover -s pruebas
```
