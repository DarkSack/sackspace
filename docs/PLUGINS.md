# Widgets de terceros (plugins)

Un plugin añade widgets a la galería de SackSpace. Es una DLL de .NET que corre **dentro de SackSpace con los permisos del usuario**: por eso cada plugin viene apagado y el usuario lo activa a mano en Ajustes › Datos y privacidad › Widgets de terceros. Los cambios se aplican al reiniciar.

## Dónde va

```
%LOCALAPPDATA%\SackSpace\plugins\<Nombre>\<Nombre>.dll
```

La carpeta y la DLL se llaman igual. Solo se copia la DLL del plugin (y sus dependencias propias); nunca `SackSpace.dll` ni `SackSpace.Core.dll`.

## El contrato

Cualquier clase pública que implemente `SackSpace.Widgets.IWidgetFactory` y tenga constructor sin parámetros:

```csharp
public sealed class MiFactory : IWidgetFactory
{
    public WidgetDescriptor Descriptor { get; } = new(
        "autor.miwidget", "Mi widget", "Qué hace.", Glyphs.Calendar,
        DefaultWidth: 260, DefaultHeight: 150, MinWidth: 200, MinHeight: 120,
        Settings: [new WidgetSetting("ciudad", "Ciudad", WidgetSettingKind.Text, "Madrid")]);

    public FrameworkElement Create(WidgetContext c) => new MiVista(c);
}
```

- **Id**: único y con prefijo del autor (`autor.nombre`). Si choca con un widget integrado o de otro plugin, se ignora.
- **Opciones**: se leen de `c.Settings` (JSON). La hoja de opciones se dibuja sola a partir de `Settings`; al cambiarlas el panel se vuelve a crear.
- **Guardar datos propios**: escríbelos en `c.Settings` y llama a `c.Save()`.
- **Limpieza**: si la vista se suscribe a eventos o timers, implementa `IDisposable`; se llama cuando el panel sale de la pantalla.
- **Aspecto**: usa `Ui.Header`, `Ui.Text`, `Ui.Pill`… para que se vea como el resto y siga el tema.
- **Errores**: si `Create` lanza una excepción, el panel muestra «Este widget no se pudo cargar» y el error queda en el registro.

## El proyecto

Mira `samples/DaysUntil`: referencia `SackSpace` y `SackSpace.Core` con `Private="false" ExcludeAssets="runtime"` y `EnableDynamicLoading`. Compílalo con

```
dotnet build samples/DaysUntil -c Release
```

y copia `DaysUntil.dll` a `plugins\DaysUntil\`.

El contrato sigue a la versión de SackSpace: un plugin compilado contra una versión puede dejar de cargar en otra si cambian estas clases. En ese caso Ajustes dice «No es compatible con esta versión de SackSpace».
