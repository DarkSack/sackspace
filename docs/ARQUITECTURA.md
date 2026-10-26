# Arquitectura

Dos proyectos y las pruebas:

- **`src/SackSpace.Core`** (net10.0, sin WPF): el modelo y todo lo que se puede probar sin pantalla. Configuración y su guardado, matemáticas del diseño, temas y colores, atajos y descriptores de widgets.
- **`src/SackSpace`** (WPF, net10.0-windows10.0.19041.0): ventanas, widgets, servicios de Windows y ajustes.
- **`tests/SackSpace.Tests`**: xUnit sobre el núcleo.

## Cómo vive en el escritorio

Una ventana `DesktopHost` por monitor, transparente y del tamaño del área de trabajo:

- **Dueño**: su ventana dueña es la del escritorio (`Progman`). Así no sale en Alt+Tab ni en la barra de tareas, y sobrevive a Win+D.
- **Siempre al fondo**: `WM_WINDOWPOSCHANGING` la mantiene al fondo, salvo cuando se suben los paneles.
- **Clic a través**: los píxeles transparentes dejan pasar el clic a los iconos del escritorio. Solo los paneles reciben el ratón.
- **Subir los paneles** (`Ctrl+Alt+W`): la ventana pasa delante y muestra el fondo difuminado. Windows no deja marcar como "siempre encima" a una ventana con dueño, así que en cuanto otra app toma el foco los paneles se bajan solos, como los widgets de Windows.
- **Reinicio del Explorador**: el mensaje `TaskbarCreated` vuelve a enganchar las ventanas al escritorio nuevo.
- **Cambios de pantalla**: monitores, resolución o escala rehacen las ventanas, con 900 ms de espera.
- **Renderizado por software**: una ventana transparente de pantalla completa se copia a memoria en cada fotograma de todos modos. Medido: la GPU gasta la misma CPU y unas 4 veces más memoria (340 MB frente a 80 MB).

## Cristal sin desenfoque en vivo

Los paneles están sobre el fondo de pantalla, así que lo que hay detrás es el fondo:

1. `WallpaperService` dibuja el fondo de cada monitor a 480 px, con la ruta, la posición y el color de `IDesktopWallpaper`.
2. Lo difumina una sola vez y saca su color dominante, para el acento al estilo Material You.
3. Cada panel pinta su trozo de ese bitmap (`ImageBrush.Viewbox`) y encima el tinte, el brillo y el borde del tema.

Cuesta un bitmap pequeño por monitor, y se vuelve a calcular cuando cambia el fondo o el desenfoque.

## Configuración

- **Archivo**: `AppConfig` → `%LOCALAPPDATA%\SackSpace\config.json`.
  - Las coordenadas son DIP relativos al área de trabajo de cada monitor.
  - Cada panel guarda una clave estable del monitor (la ruta del dispositivo). Si ese monitor no está, el panel aparece en la principal.
- **Escritura**:
  - `ConfigSession` guarda 600 ms después del último cambio y al salir.
  - `ConfigStore` escribe de forma atómica (archivo temporal y `File.Replace`) y deja un `.bak`.
  - Un archivo dañado se aparta con fecha y se recupera la copia; nunca se borra.
- **Cambios**: `Session.Notify(ChangeKind)` avisa a `App.OnConfigChanged`, que reaplica solo lo necesario (tema, barra, paneles, atajos, inicio con Windows).

## Añadir un widget

1. **Descriptor** en `BuiltInWidgets` (Core): id, nombre, icono, tamaños y opciones. La hoja de opciones se genera sola a partir de las opciones (`Toggle`, `Choice`, `Number`, `Text`).
2. **Vista**: un `FrameworkElement` que recibe un `WidgetContext`. El contexto da:
   - el panel y sus `Settings` (JSON propio del widget);
   - los servicios;
   - `Save()` y `Toast()`.
3. **Registro**: `WidgetRegistry.Register(new WidgetFactory(descriptor, c => new MiWidget(c)))`.

Si la vista se suscribe a servicios (reloj, muestreador, multimedia), implementa `IDisposable`. Se desecha al cambiar de espacio, al quitar el panel o al rehacer las ventanas.

### Servicios compartidos

- **`ClockTicker`**: un solo temporizador alineado al reloj. Late cada minuto si nadie muestra segundos.
- **`SystemSampler`**: `GetSystemTimes` y `GlobalMemoryStatusEx`. Solo corre con suscriptores, al intervalo más corto que pidan.
- **`MediaService`**: los controles multimedia de Windows (SMTC), por eventos.
- **`ActivityGovernor`**: pausa todo lo anterior con pantalla completa (`SHQueryUserNotificationState`) o con la sesión bloqueada.

## Atajos

`HotkeyService` usa `RegisterHotKey` en una ventana solo de mensajes. Los atajos ocupados por otra app se marcan en Ajustes. Mientras se graba uno nuevo, los demás se desactivan.
