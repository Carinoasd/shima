# Shima

[繁體中文](../README.md) · [简体中文](README.zh-CN.md) · [English](README.en.md) · [日本語](README.ja.md) · [한국어](README.ko.md) · [Español](README.es.md) · [Français](README.fr.md) · [Deutsch](README.de.md) · **Português** · [Italiano](README.it.md) · [Русский](README.ru.md)

Uma pequena ilha no topo do ecrã do Windows: mostra a música que está a tocar e a letra sincronizada, e abre-se num painel de reprodução ao passar o rato.

![Shima](screenshots/pill.png)

<img src="screenshots/music.png" width="440" alt="Shima">

## Funcionalidades

- **Fontes de música**: Roon (posição, volume, zonas) e qualquer leitor que apareça no painel de volume do Windows — YouTube no navegador, Spotify, Apple Music…
- **Letras**: os seus `.lrc`, LRCLIB, NetEase, QQ Music e Kugou; distingue as versões da mesma música (`104期Ver.`, `SAKURA Ver.`)
- **Letras animadas**: a letra desliza suavemente e uma luz acompanha o canto; clique numa linha para saltar para lá; troque de fonte ou ajuste o tempo
- **Segunda linha**: tradução em chinês ou romaji por baixo das músicas japonesas
- **Personagem**: vive na cápsula e mexe-se com o som real; por omissão, um Claude em píxeis em homenagem à mascote do Claude Code
- **Estado no Discord**: música, capa e progresso
- **Ferramentas**: pomodoro, prateleira de ficheiros, lista de tarefas, calendário, lembrete de água
- **Estado do sistema**: alterações de volume, brilho, bateria, rede e dispositivo de saída
- **Perguntar ao Claude**: por escrito ou por voz; também pode mudar de música
- **Claude Code**: permita ou recuse os pedidos de permissão na ilha; mostra o progresso e avisa ao terminar
- **Chat**: as versões web do Claude, Gemini e ChatGPT dentro da ilha
- **Temporizador de sono**: pausa após 15, 30, 45 ou 60 minutos, ou no fim da música; também pode pedir ao assistente de voz
- **Dispositivo de reprodução**: troque o dispositivo predefinido do Windows no separador de música ou no menu de contexto
- **Resumo de audição**: tempo, músicas e artistas mais ouvidos por semana, mês ou ano num cartão para partilhar; o ano é um mapa de calor com uma célula por dia
- **Lembretes**: escreva `15:30 reunião`, `20m água` ou `daily 8:00 remédio` e a ilha avisa à hora certa; podem repetir-se diariamente, nos dias úteis ou semanalmente
- **Aspeto**: cor de destaque (amostras ou código de cor próprio) e linha de progresso na cápsula
- **Guardar e traduzir letras**: guarde a letra acertada como `.lrc`; sem tradução, a IA traduz verso a verso
- **Codex CLI e Gemini CLI**: tal como no Claude Code, a ilha mostra o estado e avisa no fim; os pedidos de permissão do Codex respondem-se na ilha
- **Last.fm**: regista o que ouve no Last.fm (pode usar ao mesmo tempo que o ListenBrainz)
- Arraste-a para onde quiser; oculta-se perante aplicações em ecrã inteiro; 11 idiomas

## Instalação

Descarregue `Shima.exe` em [Releases](../../../releases) e execute-o. Não é preciso instalar o .NET.

O programa não está assinado, por isso o Windows mostra um aviso do SmartScreen na primeira vez: escolha «Mais informações → Executar mesmo assim».

Se usa o Roon, após o primeiro arranque ative **Shima** no Roon, em «Definições → Extensões».

## Atalhos

| | |
| --- | --- |
| Ctrl+Alt+Space | Abrir / fechar |
| Ctrl+Alt+V | Fazer uma pergunta ao assistente de voz |

O ícone na área de notificação permite ocultar a ilha ou sair. A roda dentada à direita do painel abre as definições.

## Mais

Definições, compilação e tradução: consulte a documentação completa em [English](README.en.md) ou [繁體中文](../README.md).

Claude é uma marca da Anthropic; a personagem Claude é uma homenagem e este projeto não tem ligação à Anthropic.

MIT © 2026 Carinoasd
