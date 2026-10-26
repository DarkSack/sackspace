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

**De dónde sale el fondo**, por pantalla y en este orden (`WallpaperService.Pick`):

1. La imagen propia de Ajustes › Apariencia, si hay.
2. **Wallpaper Engine** (si corre `wallpaper64`/`wallpaper32`): su `config.json` está junto al exe que guarda el registro (`HKCU\Software\WallpaperEngine\installPath`). En `general.wallpaperconfig.selectedwallpapers` cada pantalla es `MonitorN`, y `general.user.monitormap` dice qué N le toca a cada ruta de dispositivo (la misma clave que usa SackSpace, con `/`). La imagen es el `preview` del `project.json` de ese fondo.
3. **Lively** (si corre `Lively`): `WallpaperLayout.json` en `%LOCALAPPDATA%\Lively Wallpaper` (o en el `LocalCache` del paquete de la Store) liga cada `DeviceId` con una carpeta; de su `LivelyInfo.json` se usa el propio archivo si es una imagen y, si no, la miniatura.
4. El fondo de Windows.

Ninguna de las dos apps avisa a Windows cuando cambia su fondo, así que cada 8 s se mira si corren y la fecha de sus archivos. La lectura está en `SackSpace.Core/Themes/LiveWallpapers.cs`, con pruebas.

**Cristal transparente**: los paneles y la barra no pintan el bitmap ni la sombra (que es una forma opaca), solo el tinte. Como la ventana es transparente por píxel, se ve el escritorio en vivo, también un fondo animado.

## Iconos del escritorio por espacio

Windows no deja ocultar iconos sueltos del escritorio sin tocar los archivos, y tocarlos no sirve: el escritorio suele estar en OneDrive (el cambio se sincronizaría), los iconos de «Acceso público» piden administrador, y Explorer puede estar configurado para enseñar los ocultos. Por eso, con la opción encendida:

1. `DesktopItems.ShowWindowsIcons(false)` usa el mismo interruptor que «Ver › Mostrar iconos del escritorio» (`WM_COMMAND 0x7402` a `SHELLDLL_DefView`). Explorer lo recuerda.
2. `DesktopItems.List()` lee el escritorio del usuario, el público y los iconos de sistema activos (Papelera, Este equipo…, según `HideDesktopIcons\NewStartPanel`).
3. `DesktopIconGrid` los dibuja en la capa del espacio, bajo los paneles, en columnas como «Organizar automáticamente» y con el tamaño de icono de Windows.
4. Cada `Workspace.DesktopIcons` es la lista de rutas que muestra; `null` = todas. Lo nuevo que aparece en el escritorio entra en el espacio activo, y un renombrado conserva sus espacios (`DesktopIconLists`, con pruebas).

Al apagar la opción o salir desde la bandeja vuelven los iconos de Windows. Si SackSpace se cierra a la fuerza, siguen ocultos hasta que vuelva a abrirse, o hasta usar «Ver › Mostrar iconos del escritorio».

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
