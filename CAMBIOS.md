# Cambios

## 0.7.3

- Mezclador: en dispositivos de salida sin medidor de nivel o sin volumen general (auriculares Bluetooth, salidas virtuales), `Activate` devuelve `E_NOINTERFACE`, que .NET entrega como `InvalidCastException: Specified cast is not valid`, y la lectura entera fallaba. Ahora esas dos cosas son opcionales: el panel muestra los programas y, si lo hay, el general.

## 0.7.2

- Al arrancar por primera vez ofrece crear un acceso directo en el escritorio de Windows; también desde la bandeja y desde Configuración → General. Al instalar o actualizar, el acceso pasa a apuntar a la copia instalada; al desinstalar se quita.
- Reproductor con navegadores: Configuración → General muestra los navegadores Chromium del equipo y activa en ellos (con el navegador cerrado) la opción «Hardware Media Key Handling», que Brave trae apagada y sin la cual lo que suena en el navegador no llega a los controles multimedia de Windows ni al reproductor.

## 0.7.1

- Mezclador: las filas de los programas conservaban una clave en la primera lectura y otra en las siguientes, y además se reordenaban al sonar o callarse; el deslizador se «soltaba» al arrastrarlo. Ahora la clave es estable, el orden es por nombre y nada se mueve mientras se arrastra.
- Mezclador: si no se puede leer, el panel muestra el motivo (y queda en `registro.log`); una sesión que muere a mitad de la lectura ya no arruina las demás.
- Primera versión publicada en GitHub Releases: desde aquí, las copias instaladas se actualizan solas.

## 0.7.0

- Sistema de estilos común para los widgets (`comun/estilo.css` y `comun/puente.js`) y tema claro, oscuro o automático.
- Reloj con fecha, segundos y formato de 12 o 24 horas (`opciones`).
- Widget de notas que guarda en un archivo (por defecto en la carpeta de datos; se puede apuntar a OneDrive).
- Fondos de pantalla por escritorio (`fondos`), con restauración al salir.
- Atajo `activar:<widget>` para traer un widget al frente (por ejemplo, el Gran Sabio para hablarle).
- La app busca versiones nuevas en GitHub Releases, avisa y las instala; «Buscar actualizaciones» en la bandeja.
- «Ver registro» en la bandeja.
- Infraestructura: solución `Escritorio.slnx`, `global.json`, `Directory.Build.props`, `.editorconfig`, verificación de formato en CI, artefacto con la versión en el nombre y publicación automática de versiones al cambiar la versión en `main`.

## 0.6.0

- Las configuraciones de versiones anteriores se completan solas con los widgets incluidos que falten.
- Menú «Widgets» en la bandeja y ventana «Configuración…» con pestañas.
- Atajos de teclado globales, escritorio automático (`activarCon`), atenuar al hablar y widget de rendimiento.

## 0.5.0

- Un `.exe` nuevo reemplaza la versión instalada y quita la anterior.

## 0.4.0

- Reproductor con cinco diseños y mezclador de volúmenes por programa.

## 0.3.0

- Escritorios: distribuciones con nombre de widgets y ventanas; `--escritorio`.

## 0.2.0

- Reescritura como app nativa en C#: widgets fijos, organizar ventanas, baterías Bluetooth, instalación sin instalador.
