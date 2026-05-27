# Interactive Wallpaper para Windows

Ejecutable nativo para usar como wallpaper interactivo con widgets:

- Reloj y fecha.
- Calendario sincronizado por URL iCal de Google Calendar.
- Ventana web flotante, movible y redimensionable.
- Menu en la bandeja del sistema para ajustes, mostrar/ocultar widgets y salir.

## Compilar

Ejecuta:

```powershell
.\build.ps1
```

El ejecutable queda en:

```text
dist\InteractiveWallpaper.exe
```

## Uso

Abre `InteractiveWallpaper.exe`. Por defecto intenta colocarse como wallpaper detras del escritorio de Windows. Para probarlo como ventana normal:

```powershell
.\dist\InteractiveWallpaper.exe --windowed
```

Para sincronizar Google Calendar, abre `Ajustes` desde la barra superior o el icono de bandeja y pega la URL secreta iCal:

Google Calendar > Configuracion > Integrar calendario > Direccion secreta en formato iCal.

Los ajustes y posiciones se guardan en:

```text
%APPDATA%\InteractiveWallpaper\settings.ini
```

## Version HTML

Tambien hay una version HTML lista para cargar en Lively Wallpaper o abrir en el navegador:

```text
wallpaper.html
```

Usa el video de YouTube indicado como fondo, con reloj, calendario y ventana web colocados a la derecha.

Si abres el HTML directamente como archivo, YouTube puede bloquear el reproductor. En ese caso el fondo usa una version ambiental sin el error del iframe. Para intentar cargar el video real, abre:

```text
abrir_wallpaper_http.cmd
```

Ese script sirve el HTML en `http://127.0.0.1:8765/wallpaper.html`.
