# SackSpace

Tu escritorio de Windows, ordenado por espacios. Paneles de cristal (fondo difuminado, bordes redondeados, animaciones suaves, al estilo Android) que viven sobre el fondo de pantalla, con un espacio para cada momento: **Personal, Trabajo, Gaming y Desarrollo**, o los que quieras.

- **Espacios**: cada uno con sus propios paneles, accesos, notas, colores y distribución. Se cambia con las pestañas de arriba, la rueda del ratón sobre ellas, los atajos o el icono de la bandeja.
- **Widgets**: reloj, calendario, notas, tareas, monitor de CPU, RAM y gráfica, red (bajada y subida), discos, almacenamiento (en qué se va el espacio de cada disco: juegos de Steam, Epic y Xbox, sistema, programas, datos de apps, desarrollo, fotos y videos, archivos, papelera y carpetas tuyas; al tocar una categoría lista sus carpetas más grandes y las abre en el Explorador; cuánto creció cada disco desde la medición anterior, aviso de poco espacio, vaciar la papelera y Sensor de almacenamiento; recorre todo el disco en tu PC, en pausa mientras juegas a pantalla completa; juegos de Steam sin abrir hace meses con su botón de desinstalar y limpieza de temporales y cachés), OBS (si estás en directo o grabando, subida y cuadros perdidos, y botones para el directo, la grabación, la repetición y las escenas; usa el servidor WebSocket de OBS en tu PC), clima, temporizador (cuenta atrás, Pomodoro y cronómetro, con aviso de Windows aunque estés en otro espacio), horario de trabajo (tu turno de cada día con hoy resaltado, cuánto falta para entrar, comer o salir, y actividades de un día o semanales, con hora de fin opcional que se mueven arrastrándolas y se alargan o acortan desde su borde de abajo; extendido es una cuadrícula semanal con un color por día, filas por hora, turnos y actividades como bloques y hoy resaltado; retraído, solo hoy), reproductor (lo que suene en Spotify, el navegador o cualquier app) y grupos de accesos (apps del menú Inicio, archivos y carpetas; se arrastran desde el Explorador; lo que desinstalas o borras se quita solo, o a mano con + › «Quitar los que ya no están»; los juegos que instalas en Steam o Epic se pueden añadir solos, y + › «Un juego instalado» lista los que faltan).
- **Buscar y abrir** (`Ctrl+Alt+Shift+F` o el icono de la bandeja): escribe y abre cualquier acceso de tus espacios, una app del menú Inicio, un juego instalado o **cualquier archivo tuyo**, por su nombre exacto o parecido (letras cambiadas o que faltan). Entiende filtros: `ext:pdf,docx`, `*.psd`, `tipo:imagen` (video, audio, documento, código, comprimido, programa), `en:Descargas`, `tam:>100mb`, `fecha:semana` (hoy, ayer, mes, año o `2025`) y `"frase exacta"`. La lista de nombres se guarda en tu PC y se rehace cada 12 h; no lee el contenido de los archivos. Mayús + clic abre la carpeta. Se apaga en Ajustes › General.
- **Más widgets**: tiempo de juego (horas por juego hoy, en la semana, el mes y en total, y el más jugado del mes; cuenta solo mientras el juego está delante), Twitch (si estás en directo, espectadores, seguidores nuevos, y título y categoría editables; se conecta con un código en twitch.tv/activate), historial del portapapeles (solo en memoria, sin lo que los gestores de contraseñas marcan como privado) y botón «Clip» en el de OBS, que guarda un clip con SackClips o, sin él, la repetición de OBS.
- **SackMix**: al cambiar de espacio, [SackMix](https://github.com/DarkSack/sackmix) pone la mezcla de audio que guardaste para ese espacio (desde su menú de la bandeja).
- **Idiomas**: español, inglés y portugués (Brasil). Sigue al de Windows o se elige en Ajustes › General.
- **Widgets de terceros**: plugins en `%LOCALAPPDATA%\SackSpace\plugins`, apagados hasta que los actives en Ajustes › Datos y privacidad ([docs/PLUGINS.md](docs/PLUGINS.md)).
- **Espacios automáticos** (Ajustes › Espacios): cada espacio puede abrirse solo en un horario (p. ej. Trabajo de 09:00 a 18:00, de lunes a viernes) o al usar ciertos programas (Gaming con `steam`) o cualquier cosa dentro de una carpeta (`D:\SteamLibrary`); `!` deja algo fuera (`!wallpaper64`). Un programa puede estar en varios espacios: si ya estás en uno de ellos no cambia. Solo cambia al empezar el horario o al pasar a esa app, así que si cambias a mano se respeta.
- **Cada panel a tu gusto**: su propio color de cristal, opacidad y, si quieres, sin título (engranaje del panel en modo edición o clic derecho › Opciones).
- **Diseño libre**: mantén pulsado un panel (o el lápiz de la barra) para moverlo, cambiarle el tamaño, ocultarlo o quitarlo; se alinea solo a los bordes y a una rejilla. «Ordenar» los coloca sin encimarse.
- **Varios monitores**: cada pantalla tiene sus paneles; un panel se manda a otra pantalla con el clic derecho.
- **Temas**: cinco incluidos y los tuyos en JSON ([docs/TEMAS.md](docs/TEMAS.md)). Acento por espacio, tomado del fondo de pantalla, del tema o a tu gusto; opacidad, desenfoque y esquinas ajustables.
- **Iconos del escritorio por espacio** (Ajustes › General): cada espacio muestra solo los iconos que elijas, con el clic derecho sobre un icono o con el lápiz › Iconos. SackSpace oculta los de Windows mientras está abierto y los dibuja él; al salir vuelven. No mueve ni cambia ningún archivo.
- **Fondos animados**: con Wallpaper Engine o Lively, el cristal toma el fondo de cada pantalla (su vista previa). También puede usar una imagen propia, o ser transparente y dejar ver el fondo en movimiento.
- **Se guarda solo**: cada cambio se escribe al momento (con copia del anterior). Exportar e importar una copia desde Ajustes.
- **Privado**: sin cuentas ni estadísticas. Solo salen a internet el widget de Clima (a Open-Meteo, con la ciudad que escribas) y el de Uso de IA (a Anthropic, con tu sesión de Claude Code, para leer tu uso de Claude; el de ChatGPT se lee de las sesiones de Codex en tu PC), y una vez al día la pregunta a GitHub de si hay versión nueva (se apaga en Ajustes › General). Todo queda en `%LOCALAPPDATA%\SackSpace`.
- **Se actualiza**: una vez al día pregunta a GitHub si hay versión nueva y te avisa; «Instalar» descarga el asistente, comprueba su huella SHA-256 y lo abre. Se apaga en Ajustes › General.
- **Ligero**: 0 % de CPU en reposo y unos 80 MB de memoria; relojes y medidores se pausan con juegos o vídeos a pantalla completa y con la sesión bloqueada, y una pantalla tapada por una ventana maximizada deja de dibujarse. «Bloquear el diseño» (Ajustes › General) evita mover paneles sin querer.

## Instalar

SackSpace es **gratis**. Funciona en Windows 10 2004 o posterior y no necesita tener .NET instalado.

Con winget (cuando el paquete esté publicado): `winget install DarkSack.SackSpace`. O bien:

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

SackSpace es gratis y de código abierto, con [licencia MIT](LICENSE). Si publicas una versión modificada, ponle otro nombre. Las condiciones de uso y la privacidad están en [installer/LICENCIA.txt](installer/LICENCIA.txt), el mismo texto que muestra el instalador.

## Política de firma de código

Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

- Los instaladores oficiales se compilan en GitHub Actions ([.github/workflows/build.yml](.github/workflows/build.yml)) a partir del código de este repositorio; primero se firma `SackSpace.exe` y luego el instalador que lo contiene. Lo que no salga de ese workflow no está firmado por el proyecto.
- Autores y revisores (committers and reviewers): [DarkSack](https://github.com/DarkSack).
- Aprobación de cada firma (approvers): [DarkSack](https://github.com/DarkSack).
- Todos usan verificación en dos pasos en GitHub y en SignPath.

**Privacidad.** SackSpace no envía nada a otros sistemas salvo lo que pides tú: el widget de Clima (la ciudad que escribas, a Open-Meteo), el de Uso de IA (tu sesión de Claude Code, a Anthropic) y la búsqueda diaria de actualizaciones (pregunta a api.github.com por la última versión; se apaga en Ajustes › General). Detalles en [installer/LICENCIA.txt](installer/LICENCIA.txt).
