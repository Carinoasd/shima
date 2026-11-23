# Shima

[繁體中文](../README.md) · [简体中文](README.zh-CN.md) · [English](README.en.md) · [日本語](README.ja.md) · [한국어](README.ko.md) · [Español](README.es.md) · [Français](README.fr.md) · [Deutsch](README.de.md) · [Português](README.pt.md) · **Italiano** · [Русский](README.ru.md)

Una piccola isola nella parte alta dello schermo di Windows: mostra la musica in riproduzione e il testo sincronizzato, e si apre in un pannello di riproduzione al passaggio del mouse.

![Shima](screenshots/pill.png)

<img src="screenshots/music.png" width="440" alt="Shima">

## Funzioni

- **Sorgenti musicali**: Roon (posizione, volume, zone) e qualsiasi lettore visibile nel pannello del volume di Windows: YouTube nel browser, Spotify, Apple Music…
- **Testi**: i tuoi `.lrc`, LRCLIB, NetEase, QQ Music e Kugou; distingue le versioni dello stesso brano (`104期Ver.`, `SAKURA Ver.`)
- **Testi animati**: il testo scorre in modo fluido e una luce segue il canto; clicca una riga per saltarci; cambia fonte o regola la sincronia
- **Seconda riga**: traduzione in cinese o romaji sotto i brani giapponesi
- **Personaggio**: vive nella capsula e si muove con il suono reale; predefinito un Claude in pixel, omaggio alla mascotte di Claude Code
- **Stato di Discord**: brano, copertina e avanzamento
- **Strumenti**: pomodoro, ripiano per i file, elenco attività, calendario, promemoria per bere
- **Stato del sistema**: variazioni di volume, luminosità, batteria, rete e dispositivo di uscita
- **Chiedi a Claude**: scrivendo o a voce; può anche cambiare brano
- **Claude Code**: consenti o nega le richieste di autorizzazione dall'isola; mostra l'avanzamento e avvisa al termine
- **Chat**: le versioni web di Claude, Gemini e ChatGPT dentro l'isola
- **Timer di spegnimento**: mette in pausa dopo 15, 30, 45 o 60 minuti, o alla fine del brano; puoi chiederlo anche all'assistente vocale
- **Dispositivo di riproduzione**: cambia il dispositivo predefinito di Windows dalla scheda musica o dal menu contestuale
- **Riepilogo d'ascolto**: tempo, brani e artisti più ascoltati per settimana, mese o anno in una scheda da condividere; l'anno è una mappa di calore con una casella al giorno
- **Promemoria**: scrivi `15:30 riunione`, `20m acqua` o `daily 8:00 medicina` e l'isola ti avvisa all'ora giusta; si possono ripetere ogni giorno, nei feriali o ogni settimana
- **Aspetto**: colore d'accento (campioni o codice colore personalizzato) e linea di avanzamento sulla capsula
- **Salva e traduci i testi**: salva il testo sincronizzato come `.lrc`; se manca la traduzione, l'IA traduce verso per verso
- **Codex CLI e Gemini CLI**: come per Claude Code, l'isola mostra lo stato e avvisa alla fine; le richieste di autorizzazione di Codex si gestiscono dall'isola
- **Last.fm**: registra gli ascolti su Last.fm (anche insieme a ListenBrainz)
- Trascinabile ovunque; si nasconde davanti alle app a schermo intero; 11 lingue

## Installazione

Scarica `Shima.exe` da [Releases](../../../releases) ed eseguilo. Non serve installare .NET.

Il programma non è firmato, quindi al primo avvio Windows mostra un avviso SmartScreen: scegli «Ulteriori informazioni → Esegui comunque».

Se usi Roon, dopo il primo avvio abilita **Shima** in Roon, «Impostazioni → Estensioni».

## Scorciatoie

| | |
| --- | --- |
| Ctrl+Alt+Space | Apri / chiudi |
| Ctrl+Alt+V | Fai una domanda all'assistente vocale |

L'icona nell'area di notifica permette di nascondere l'isola o uscire. L'ingranaggio a destra del pannello apre le impostazioni.

## Altro

Impostazioni, compilazione e traduzione: vedi la documentazione completa in [English](README.en.md) o [繁體中文](../README.md).

Claude è un marchio di Anthropic; il personaggio Claude è un omaggio e questo progetto non è affiliato ad Anthropic.

MIT © 2026 Carinoasd
