# Shima

[繁體中文](../README.md) · [简体中文](README.zh-CN.md) · [English](README.en.md) · [日本語](README.ja.md) · [한국어](README.ko.md) · [Español](README.es.md) · **Français** · [Deutsch](README.de.md) · [Português](README.pt.md) · [Italiano](README.it.md) · [Русский](README.ru.md)

Une petite île en haut de l'écran Windows : elle affiche la musique en cours et les paroles synchronisées, puis se déploie en panneau de lecture au survol.

![Shima](screenshots/pill.png)

<img src="screenshots/music.png" width="440" alt="Shima">

## Fonctionnalités

- **Sources musicales** : Roon (position, volume, zones) et tout lecteur visible dans le panneau de volume de Windows — YouTube dans le navigateur, Spotify, Apple Music…
- **Paroles** : vos `.lrc`, LRCLIB, NetEase, QQ Music et Kugou ; distingue les versions d'un même titre (`104期Ver.`, `SAKURA Ver.`)
- **Paroles animées** : le texte défile en douceur et une lumière suit le chant ; cliquez sur une ligne pour y aller ; changez de source ou ajustez le décalage
- **Deuxième ligne** : traduction chinoise ou romaji sous les titres japonais, ou furigana au-dessus des kanji
- **Personnage** : il vit dans la capsule et bouge au rythme du son réel ; par défaut, un Claude en pixels en hommage à la mascotte de Claude Code
- **Statut Discord** : titre, pochette et progression
- **Outils** : pomodoro, étagère de fichiers, liste de tâches, calendrier, rappel d'hydratation
- **État du système** : changements de volume, luminosité, batterie, réseau et périphérique de sortie
- **Demander à Claude** : au clavier ou à la voix ; il peut aussi changer de titre
- **Claude Code** : autorisez ou refusez ses demandes depuis l'île ; progression affichée et notification à la fin
- **Chat** : les versions web de Claude, Gemini et ChatGPT dans l'île
- **Minuteur de veille** : pause après 15, 30, 45 ou 60 minutes, ou à la fin du titre ; on peut aussi le demander à l'assistant vocal
- **Périphérique de lecture** : changez le périphérique par défaut de Windows depuis l'onglet musique ou le menu contextuel
- **Récap d'écoute** : temps, titres et artistes les plus écoutés par semaine, mois ou année, sur une carte à partager ; l'année est une carte de chaleur avec une case par jour
- **Rappels** : tapez `15:30 réunion`, `20m eau` ou `daily 8:00 médicament` et l'île vous prévient à l'heure ; répétition quotidienne, en semaine ou hebdomadaire
- **Apparence** : couleur d'accent (pastilles ou code couleur libre) et ligne de progression sur la capsule
- **Paroles enregistrées et traduites** : enregistrez les paroles calées en `.lrc` ; sans traduction, l'IA traduit ligne par ligne
- **Codex CLI et Gemini CLI** : comme pour Claude Code, l'île affiche l'état et signale la fin ; les demandes d'autorisation de Codex se règlent sur l'île
- **Last.fm** : envoie vos écoutes à Last.fm (en même temps que ListenBrainz si vous voulez)
- **Commandes rapides** : tapez `/next`, `/vol 30`, `/sleep 30` ou `/remind 15:30 réunion` dans le champ de l'IA ; elles s'exécutent aussitôt, sans clé d'API
- **Sauvegarde** : exportez réglages, historique, favoris, rappels et paroles dans un zip et restaurez-les sur un autre PC (sans les clés)
- Déplaçable où vous voulez ; masquée devant les applications en plein écran ; 11 langues

## Installation

Téléchargez `Shima.exe` depuis [Releases](../../../releases) et lancez-le. Aucune installation de .NET n'est nécessaire.

Le programme n'est pas signé : Windows affichera un avertissement SmartScreen la première fois. Choisissez « Informations complémentaires → Exécuter quand même ».

Avec Roon, après le premier lancement, activez **Shima** dans Roon, « Paramètres → Extensions ».

## Raccourcis

| | |
| --- | --- |
| Ctrl+Alt+Space | Déployer / replier |
| Ctrl+Alt+V | Poser une question à l'assistant vocal |

L'icône de la zone de notification permet de masquer l'île ou de quitter. L'engrenage à droite du panneau ouvre les réglages.

## En savoir plus

Réglages, compilation et traduction : voir la documentation complète en [English](README.en.md) ou [繁體中文](../README.md).

Claude est une marque d'Anthropic ; le personnage Claude est un hommage et ce projet n'est pas affilié à Anthropic.

MIT © 2026 Carinoasd
