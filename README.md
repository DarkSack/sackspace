# SackSpace

Tu escritorio de Windows, ordenado por espacios. Paneles de cristal (fondo difuminado, bordes redondeados, animaciones suaves, al estilo Android) que viven sobre el fondo de pantalla, con un espacio para cada momento: **Personal, Trabajo, Gaming y Desarrollo**, o los que quieras.

- **Espacios**: cada uno con sus propios paneles, accesos, notas, colores y distribución. Se cambia con las pestañas de arriba, la rueda del ratón sobre ellas, los atajos o el icono de la bandeja.
- **Widgets**: reloj, calendario, notas, tareas, monitor de CPU, RAM y gráfica, red (bajada y subida), discos, clima, temporizador (cuenta atrás, Pomodoro y cronómetro, con aviso de Windows aunque estés en otro espacio), horario de trabajo (tu turno de cada día con hoy resaltado, cuánto falta para entrar, comer o salir, y actividades de un día o semanales, con hora de fin opcional que se mueven arrastrándolas y se alargan o acortan desde su borde de abajo; extendido es una cuadrícula semanal con un color por día, filas por hora, turnos y actividades como bloques y hoy resaltado; retraído, solo hoy), reproductor (lo que suene en Spotify, el navegador o cualquier app) y grupos de accesos (apps del menú Inicio, archivos y carpetas; se arrastran desde el Explorador).
- **Espacios automáticos** (Ajustes › Espacios): cada espacio puede abrirse solo en un horario (p. ej. Trabajo de 09:00 a 18:00, de lunes a viernes) o al usar ciertos programas (Gaming con `steam`) o cualquier cosa dentro de una carpeta (`D:\SteamLibrary`); `!` deja algo fuera (`!wallpaper64`). Un programa puede estar en varios espacios: si ya estás en uno de ellos no cambia. Solo cambia al empezar el horario o al pasar a esa app, así que si cambias a mano se respeta.
- **Cada panel a tu gusto**: su propio color de cristal, opacidad y, si quieres, sin título (engranaje del panel en modo edición o clic derecho › Opciones).
- **Diseño libre**: mantén pulsado un panel (o el lápiz de la barra) para moverlo, cambiarle el tamaño, ocultarlo o quitarlo; se alinea solo a los bordes y a una rejilla. «Ordenar» los coloca sin encimarse.
- **Varios monitores**: cada pantalla tiene sus paneles; un panel se manda a otra pantalla con el clic derecho.
- **Temas**: cinco incluidos y los tuyos en JSON ([docs/TEMAS.md](docs/TEMAS.md)). Acento por espacio, tomado del fondo de pantalla, del tema o a tu gusto; opacidad, desenfoque y esquinas ajustables.
- **Iconos del escritorio por espacio** (Ajustes › General): cada espacio muestra solo los iconos que elijas, con el clic derecho sobre un icono o con el lápiz › Iconos. SackSpace oculta los de Windows mientras está abierto y los dibuja él; al salir vuelven. No mueve ni cambia ningún archivo.
- **Fondos animados**: con Wallpaper Engine o Lively, el cristal toma el fondo de cada pantalla (su vista previa). También puede usar una imagen propia, o ser transparente y dejar ver el fondo en movimiento.
- **Se guarda solo**: cada cambio se escribe al momento (con copia del anterior). Exportar e importar una copia desde Ajustes.
- **Privado**: sin cuentas ni estadísticas. Solo salen a internet el widget de Clima (a Open-Meteo, con la ciudad que escribas) y el de Uso de IA (a Anthropic, con tu sesión de Claude Code, para leer tu uso de Claude; el de ChatGPT se lee de las sesiones de Codex en tu PC). Todo queda en `%LOCALAPPDATA%\SackSpace`.
- **Ligero**: 0 % de CPU en reposo y unos 80 MB de memoria; relojes y medidores se pausan con juegos o vídeos a pantalla completa y con la sesión bloqueada, y una pantalla tapada por una ventana maximizada deja de dibujarse. «Bloquear el diseño» (Ajustes › General) evita mover paneles sin querer.

## Instalar

SackSpace es **gratis**. Funciona en Windows 10 2004 o posterior y no necesita tener .NET instalado.

**[Descarga el instalador](https://github.com/DarkSack/sackspace/releases/latest)** (`SackSpace-Setup-<versión>.exe`, en *Assets*) y ábrelo. No está firmado: si Windows muestra «Windows protegió su PC», pulsa **Más información › Ejecutar de todas formas**. El asistente muestra las condiciones de uso y privacidad, que hay que aceptar, y permite elegir la carpeta y marcar el acceso en el escritorio y el inicio con Windows. No pide permisos de administrador. Para actualizar, se instala la versión nueva encima: tu configuración se conserva. Para desinstalar, ve a Configuración › Aplicaciones.

## Uso

| Acción | Cómo |
|---|---|
| Ver los paneles encima de las ventanas | `Ctrl+Alt+W` o clic en el icono de la bandeja. `Esc` o un clic fuera los baja. |
| Editar el diseño | Mantén pulsado un panel, el lápiz de la barra o `Ctrl+Alt+Shift+E` |
| Cambiar de espacio | Pestañas, rueda del ratón sobre ellas, `Ctrl+Alt+←/→` o `Ctrl+Alt+Shift+1…9` |
| Ajustes | Engranaje de la barra, menú de la bandeja o `Ctrl+Alt+Shift+S` |

Los atajos se cambian en Ajustes › Atajos de teclado. Ninguno usa `Ctrl+Alt` con números, `E` o `Q`: en teclados en español eso es AltGr y robaría la `@`, el `#` o el `€`.

## Compilar

Requiere el SDK de .NET 10 en Windows 10 2004 o posterior.

```powershell
dotnet test SackSpace.slnx          # pruebas del núcleo
dotnet run --project src/SackSpace  # arranca la app
.\publish.ps1                       # dist\SackSpace.exe (un solo archivo autocontenido) y dist\SackSpace-Setup-<versión>.exe
```

El instalador se genera con [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`winget install JRSoftware.InnoSetup`); su script es [installer/SackSpace.iss](installer/SackSpace.iss). Sin Inno Setup, `publish.ps1` solo genera el exe.

Argumentos útiles para probar sin tocar tu configuración:

- `--data <carpeta>`: usa otra carpeta de datos (y otra instancia).
- `--snapshot <carpeta>`: dibuja cada espacio, el modo edición y la galería en PNG por pantalla, sin mostrar ventanas, y sale.

La arquitectura está en [docs/ARQUITECTURA.md](docs/ARQUITECTURA.md).

## Licencia

SackSpace es gratis: puedes usarlo en todos tus equipos, pero no venderlo ni distribuir versiones modificadas. Las condiciones completas y la privacidad están en [installer/LICENCIA.txt](installer/LICENCIA.txt), el mismo texto que muestra el instalador.
