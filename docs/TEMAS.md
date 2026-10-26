# Temas

Un tema es un archivo `.json` en la carpeta de temas (`%LOCALAPPDATA%\SackSpace\themes`). Se abre desde Ajustes › Apariencia › «Abrir la carpeta de temas». Cópialo ahí y pulsa «Recargar».

```json
{
  // Letras, números, "-" o "_". No puede repetir el de otro tema.
  "id": "atardecer",
  "name": "Atardecer",
  "isDark": true,
  "accent": "#FF8A5C",
  "glassTint": "#2A1620",
  "glassOpacity": 0.5,
  "blurRadius": 44,
  "cornerRadius": 24,
  "foreground": "#FFF4EE",
  "secondaryForeground": "#E0B9A8",
  "border": "#33FFFFFF",
  "scrim": "#73000000",
  "fontFamily": "Segoe UI Variable Display, Segoe UI"
}
```

| Campo | Qué es |
|---|---|
| `isDark` | Decide el modo de la ventana de Ajustes y el brillo del cristal. |
| `accent` | Color de acento cuando en Apariencia se elige «El del tema». |
| `glassTint` / `glassOpacity` | Color que cubre el fondo difuminado dentro de cada panel, y cuánto lo cubre (0 a 1). |
| `blurRadius` | Desenfoque del fondo, de 0 a 120. |
| `cornerRadius` | Radio de las esquinas, de 0 a 48. |
| `foreground` / `secondaryForeground` | Texto principal y secundario. |
| `border` | Contorno de los paneles. Suele ser blanco o negro casi transparente. |
| `scrim` | Sombra sobre el fondo cuando los paneles se suben. |
| `fontFamily` | Fuentes de la interfaz, en orden de preferencia. |

- **Colores**: `#RRGGBB` o `#AARRGGBB` (el primer par es la opacidad).
- **Campos que faltan**: toman el valor de «Cristal oscuro».
- **Comentarios y comas finales**: se permiten.
- **Un tema con errores**: no se carga, y el motivo sale en rojo en Apariencia.
- **Ajustes del usuario**: en Apariencia, el usuario puede cambiar la opacidad, el desenfoque y las esquinas de cualquier tema sin editar el archivo.
