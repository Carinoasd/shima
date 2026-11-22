# Shima

[繁體中文](../README.md) · [简体中文](README.zh-CN.md) · [English](README.en.md) · [日本語](README.ja.md) · **한국어** · [Español](README.es.md) · [Français](README.fr.md) · [Deutsch](README.de.md) · [Português](README.pt.md) · [Italiano](README.it.md) · [Русский](README.ru.md)

Windows 화면 맨 위에 떠 있는 작은 섬. 재생 중인 음악과 싱크 가사를 보여 주고, 마우스를 올리면 재생 패널로 펼쳐집니다.

![Shima](screenshots/pill.png)

<img src="screenshots/music.png" width="440" alt="Shima">

## 기능

- **음악 소스**: Roon(재생 위치·음량·영역)과 Windows 음량 패널에 나타나는 모든 플레이어(브라우저의 YouTube, Spotify, Apple Music 등)
- **가사**: 내 `.lrc`, LRCLIB, NetEase, QQ Music, Kugou. 같은 곡의 다른 버전(`104期Ver.`, `SAKURA Ver.`)을 구분합니다
- **움직이는 가사**: 가사 전체가 부드럽게 스크롤되고 노래 진행에 맞춰 빛이 지나갑니다. 줄을 클릭하면 그 위치로 이동. 출처 변경과 타이밍 미세 조정 가능
- **가사 둘째 줄**: 일본어 곡 아래에 중국어 번역 또는 로마자 표시
- **캐릭터**: 캡슐 안에서 실제 소리에 맞춰 움직입니다. 기본은 Claude Code 마스코트를 오마주한 픽셀 Claude
- **Discord 상태**: 듣고 있는 곡, 커버, 진행 상황 표시
- **도구**: 뽀모도로, 파일 보관함, 할 일 목록, 달력, 물 마시기 알림
- **시스템 상태**: 음량·밝기·배터리·네트워크·출력 장치가 바뀌면 섬에 표시
- **Claude에게 묻기**: 글이나 음성으로. 곡 넘기기도 부탁할 수 있습니다
- **Claude Code**: 권한 요청을 섬에서 허용·거부. 작업 진행 상황과 완료 알림
- **채팅**: Claude, Gemini, ChatGPT 웹 버전을 섬 안에서
- 원하는 위치로 드래그 / 전체 화면 앱 앞에서는 자동 숨김 / 11개 언어

## 설치

[Releases](../../../releases)에서 `Shima.exe`를 내려받아 실행합니다. .NET을 따로 설치할 필요가 없습니다.

코드 서명이 없어 처음 실행할 때 Windows SmartScreen 경고가 나옵니다. 「추가 정보 → 실행」을 누르세요.

Roon을 쓴다면 처음 실행한 뒤 Roon의 「설정 → 확장」에서 **Shima**를 사용하도록 설정하세요.

## 단축키

| | |
| --- | --- |
| Ctrl+Alt+Space | 펼치기／접기 |
| Ctrl+Alt+V | 음성 비서에게 한마디 묻기 |

알림 영역 아이콘으로 섬을 숨기거나 종료할 수 있습니다. 펼친 패널의 맨 오른쪽 톱니바퀴가 설정입니다.

## 더 보기

설정 항목, 빌드 방법, 번역 참여는 [English](README.en.md) 또는 [繁體中文](../README.md) 전체 문서를 참고하세요.

Claude는 Anthropic의 상표입니다. 섬의 Claude 캐릭터는 오마주이며, 이 프로젝트는 Anthropic과 관련이 없습니다.

MIT © 2026 Carinoasd
