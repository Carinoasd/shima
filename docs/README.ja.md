# Shima

[繁體中文](../README.md) · [简体中文](README.zh-CN.md) · [English](README.en.md) · **日本語** · [한국어](README.ko.md) · [Español](README.es.md) · [Français](README.fr.md) · [Deutsch](README.de.md) · [Português](README.pt.md) · [Italiano](README.it.md) · [Русский](README.ru.md)

Windows の画面上部に浮かぶ小さな島。再生中の曲と同期歌詞を表示し、マウスを乗せると再生パネルに広がります。

![Shima](screenshots/pill.png)

<img src="screenshots/music.png" width="440" alt="Shima">

## 機能

- **音楽ソース**：Roon（再生位置・音量・ゾーン）と、Windows の音量パネルに表示されるすべてのプレーヤー（ブラウザーの YouTube、Spotify、Apple Music など）
- **歌詞**：自分の `.lrc`、LRCLIB、NetEase、QQ Music、Kugou。同じ曲のバージョン違い（`104期Ver.`、`SAKURA Ver.`）を見分けます
- **動く歌詞**：歌詞全体がなめらかにスクロールし、歌の進行に合わせて光が走ります。行をクリックするとその位置へ移動。取得元の切り替えやタイミングの微調整も可能
- **歌詞の2行目**：日本語の曲に中国語訳またはローマ字を表示
- **キャラクター**：カプセルの中で実際の音に合わせて動きます。既定は Claude Code のマスコットへのオマージュのドット絵 Claude
- **Discord ステータス**：再生中の曲、ジャケット、進行状況を表示
- **ツール**：ポモドーロ、ファイル置き場、やることリスト、カレンダー、水分補給リマインダー
- **システム状態**：音量・明るさ・バッテリー・ネットワーク・出力デバイスの変化を島に表示
- **Claude に質問**：文字でも音声でも。曲送りも頼めます
- **Claude Code**：権限リクエストを島に表示して許可・拒否。タスクの進行状況と完了を通知
- **チャット**：Claude、Gemini、ChatGPT の Web 版を島の中に
- 好きな位置へドラッグ可能／全画面アプリの前では自動で隠れる／11 言語対応

## インストール

[Releases](../../../releases) から `Shima.exe` をダウンロードして実行します。.NET の別途インストールは不要です。

コード署名がないため、初回は Windows の SmartScreen 警告が出ます。「詳細情報 → 実行」を選んでください。

Roon を使う場合は、初回起動後に Roon の「設定 → 拡張機能」で **Shima** を有効にしてください。

## ショートカット

| | |
| --- | --- |
| Ctrl+Alt+Space | 開く／閉じる |
| Ctrl+Alt+V | 音声アシスタントに一言質問 |

通知領域のアイコンから島を隠したり終了したりできます。開いたパネルの右端の歯車が設定です。

## 詳細

設定項目、ビルド方法、翻訳への参加は [English](README.en.md) または [繁體中文](../README.md) の完全版をご覧ください。

Claude は Anthropic の商標です。島の Claude キャラクターはオマージュであり、本プロジェクトは Anthropic とは関係ありません。

MIT © 2026 Carinoasd
