# Shima

[繁體中文](../README.md) · [简体中文](README.zh-CN.md) · [English](README.en.md) · [日本語](README.ja.md) · [한국어](README.ko.md) · [Español](README.es.md) · [Français](README.fr.md) · **Deutsch** · [Português](README.pt.md) · [Italiano](README.it.md) · [Русский](README.ru.md)

Eine kleine Insel am oberen Rand des Windows-Bildschirms: Sie zeigt die laufende Musik und synchronisierte Songtexte und klappt beim Überfahren zu einem Wiedergabefeld auf.

![Shima](screenshots/pill.png)

<img src="screenshots/music.png" width="440" alt="Shima">

## Funktionen

- **Musikquellen**: Roon (Position, Lautstärke, Zonen) und jeder Player, der im Windows-Lautstärkefeld erscheint – YouTube im Browser, Spotify, Apple Music …
- **Songtexte**: eigene `.lrc`, LRCLIB, NetEase, QQ Music und Kugou; unterscheidet Versionen desselben Titels (`104期Ver.`, `SAKURA Ver.`)
- **Bewegte Songtexte**: Der Text scrollt weich, ein Licht folgt dem Gesang; Klick auf eine Zeile springt dorthin; Quelle wechseln oder Timing feinjustieren
- **Zweite Zeile**: chinesische Übersetzung oder Romaji unter japanischen Titeln
- **Figur**: wohnt in der Kapsel und bewegt sich zum tatsächlichen Ton; Standard ist ein Pixel-Claude als Hommage an das Claude-Code-Maskottchen
- **Discord-Status**: Titel, Cover und Fortschritt
- **Werkzeuge**: Pomodoro, Dateiablage, Aufgabenliste, Kalender, Trinkerinnerung
- **Systemstatus**: Änderungen von Lautstärke, Helligkeit, Akku, Netzwerk und Ausgabegerät
- **Claude fragen**: per Tastatur oder Sprache; kann auch den Titel wechseln
- **Claude Code**: Berechtigungsanfragen auf der Insel erlauben oder ablehnen; Fortschritt und Fertig-Meldung
- **Chat**: die Web-Versionen von Claude, Gemini und ChatGPT in der Insel
- **Einschlaf-Timer**: pausiert nach 15, 30, 45 oder 60 Minuten oder am Ende des Titels; geht auch per Sprachassistent
- **Wiedergabegerät**: das Standard-Wiedergabegerät von Windows direkt im Musik-Tab oder im Kontextmenü wechseln
- **Hörrückblick**: Hörzeit, meistgehörte Titel und Künstler pro Woche, Monat oder Jahr als teilbare Karte; das Jahr als Heatmap mit einem Feld pro Tag
- **Erinnerungen**: `15:30 Meeting`, `20m Wasser` oder `daily 8:00 Tablette` eintippen, die Insel meldet sich pünktlich; täglich, werktags oder wöchentlich wiederholbar
- **Darstellung**: Akzentfarbe (Farbfelder oder eigener Farbcode) und Fortschrittslinie auf der Kapsel
- **Songtexte speichern und übersetzen**: angepasste Songtexte als `.lrc` speichern; fehlt eine Übersetzung, übersetzt die KI Zeile für Zeile
- **Codex CLI und Gemini CLI**: wie bei Claude Code zeigt die Insel den Status und meldet das Ende; Berechtigungsanfragen von Codex lassen sich auf der Insel beantworten
- **Last.fm**: Gehörtes an Last.fm melden (auch zusammen mit ListenBrainz)
- Frei verschiebbar; blendet sich vor Vollbild-Apps aus; 11 Sprachen

## Installation

`Shima.exe` unter [Releases](../../../releases) herunterladen und starten. .NET muss nicht installiert sein.

Das Programm ist nicht signiert, daher zeigt Windows beim ersten Start eine SmartScreen-Warnung: „Weitere Informationen → Trotzdem ausführen“ wählen.

Mit Roon: nach dem ersten Start **Shima** in Roon unter „Einstellungen → Erweiterungen“ aktivieren.

## Tastenkürzel

| | |
| --- | --- |
| Ctrl+Alt+Space | Auf- / zuklappen |
| Ctrl+Alt+V | Dem Sprachassistenten eine Frage stellen |

Über das Symbol im Infobereich lässt sich die Insel ausblenden oder beenden. Das Zahnrad rechts im Feld öffnet die Einstellungen.

## Mehr

Einstellungen, Build und Übersetzung: siehe die vollständige Dokumentation auf [English](README.en.md) oder [繁體中文](../README.md).

Claude ist eine Marke von Anthropic; die Claude-Figur ist eine Hommage, und dieses Projekt steht in keiner Verbindung zu Anthropic.

MIT © 2026 Carinoasd
