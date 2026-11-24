# Shima

[繁體中文](../README.md) · [简体中文](README.zh-CN.md) · [English](README.en.md) · [日本語](README.ja.md) · [한국어](README.ko.md) · **Español** · [Français](README.fr.md) · [Deutsch](README.de.md) · [Português](README.pt.md) · [Italiano](README.it.md) · [Русский](README.ru.md)

Una pequeña isla en la parte superior de la pantalla de Windows: muestra la música que suena y la letra sincronizada, y se despliega en un panel de reproducción al pasar el ratón.

![Shima](screenshots/pill.png)

<img src="screenshots/music.png" width="440" alt="Shima">

## Funciones

- **Fuentes de música**: Roon (posición, volumen, zonas) y cualquier reproductor que aparezca en el panel de volumen de Windows: YouTube en el navegador, Spotify, Apple Music…
- **Letras**: tus `.lrc`, LRCLIB, NetEase, QQ Music y Kugou; distingue las versiones de una misma canción (`104期Ver.`, `SAKURA Ver.`)
- **Letras en movimiento**: la letra se desplaza con suavidad y una luz sigue el canto; haz clic en una línea para saltar a ella; puedes cambiar de fuente o ajustar el tiempo
- **Segunda línea**: traducción al chino o romaji bajo las canciones japonesas, o furigana sobre los kanji
- **Personaje**: vive en la cápsula y baila con el sonido real; por defecto, un Claude en píxeles en homenaje a la mascota de Claude Code
- **Estado de Discord**: canción, carátula y progreso
- **Herramientas**: pomodoro, bandeja de archivos, lista de tareas, calendario, recordatorio de agua
- **Estado del sistema**: cambios de volumen, brillo, batería, red y dispositivo de salida
- **Pregunta a Claude**: por escrito o por voz; también puede cambiar de canción
- **Claude Code**: permite o deniega sus solicitudes de permiso desde la isla; muestra el progreso y avisa al terminar
- **Chat**: las versiones web de Claude, Gemini y ChatGPT dentro de la isla
- **Temporizador de apagado**: pausa la música tras 15, 30, 45 o 60 minutos, o al terminar la canción; también se lo puedes pedir al asistente de voz
- **Dispositivo de reproducción**: cambia el dispositivo predeterminado de Windows desde la pestaña de música o el menú contextual
- **Resumen de escucha**: tiempo, canciones y artistas más escuchados por semana, mes o año en una tarjeta para compartir; el año es un mapa de calor con un cuadro por día
- **Recordatorios**: escribe `15:30 reunión`, `20m agua` o `daily 8:00 pastilla` y la isla te avisa a su hora; pueden repetirse a diario, entre semana o cada semana
- **Apariencia**: color de acento (muestras o un código propio) y línea de progreso en la cápsula
- **Guardar y traducir letras**: guarda la letra ajustada como `.lrc`; si una canción no tiene traducción, la IA la traduce verso a verso
- **Codex CLI y Gemini CLI**: como con Claude Code, la isla muestra el estado y avisa al terminar; las solicitudes de permiso de Codex se responden en la isla
- **Last.fm**: registra lo que escuchas en Last.fm (compatible con ListenBrainz a la vez)
- **Comandos rápidos**: escribe `/next`, `/vol 30`, `/sleep 30` o `/remind 15:30 reunión` en el cuadro de la IA; se ejecutan al instante, sin clave de API
- **Copia de seguridad**: exporta ajustes, historial, favoritos, recordatorios y letras a un zip y restáuralos en otro PC (sin claves)
- Arrástrala a cualquier sitio; se oculta ante aplicaciones a pantalla completa; 11 idiomas

## Instalación

Descarga `Shima.exe` desde [Releases](../../../releases) y ejecútalo. No hace falta instalar .NET.

El programa no está firmado, así que Windows mostrará un aviso de SmartScreen la primera vez: pulsa «Más información → Ejecutar de todas formas».

Si usas Roon, tras el primer inicio activa **Shima** en Roon, «Ajustes → Extensiones».

## Atajos

| | |
| --- | --- |
| Ctrl+Alt+Space | Desplegar / plegar |
| Ctrl+Alt+V | Hacer una pregunta al asistente de voz |

El icono de la bandeja del sistema permite ocultar la isla o salir. El engranaje de la derecha del panel abre los ajustes.

## Más

Para los ajustes, la compilación y cómo traducir, consulta la documentación completa en [English](README.en.md) o [繁體中文](../README.md).

Claude es una marca de Anthropic; el personaje de Claude es un homenaje y este proyecto no está afiliado a Anthropic.

MIT © 2026 Carinoasd
