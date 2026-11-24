"""介面文字的對照表。每一筆：繁體中文 | 簡中 | 英 | 日 | 韓 | 西 | 法 | 德 | 葡 | 義 | 俄。

    python3 tools/strings.py     # 寫出 src/Shima/Assets/strings.tsv，並檢查程式裡用到的文字都有翻譯
"""
import glob, os, re, sys

LANGS = ["zh-TW", "zh-CN", "en", "ja", "ko", "es", "fr", "de", "pt", "it", "ru"]

ROWS = """
角色 | 角色 | Character | キャラクター | 캐릭터 | Personaje | Personnage | Figur | Personagem | Personaggio | Персонаж
歌詞第二行 | 歌词第二行 | Second lyrics line | 歌詞の2行目 | 가사 둘째 줄 | Segunda línea de la letra | Deuxième ligne des paroles | Zweite Textzeile | Segunda linha da letra | Seconda riga del testo | Вторая строка текста
這首歌的歌詞 | 这首歌的歌词 | Lyrics for this song | この曲の歌詞 | 이 곡의 가사 | Letra de esta canción | Paroles de ce titre | Text dieses Titels | Letra desta música | Testo di questo brano | Текст этой песни
勿擾（1 小時） | 勿扰（1 小时） | Do not disturb (1 hour) | 通知を止める（1時間） | 방해 금지 (1시간) | No molestar (1 hora) | Ne pas déranger (1 heure) | Nicht stören (1 Stunde) | Não incomodar (1 hora) | Non disturbare (1 ora) | Не беспокоить (1 час)
展開／收起 | 展开／收起 | Expand / collapse | 展開／折りたたみ | 펼치기/접기 | Expandir / contraer | Déployer / replier | Aufklappen / Zuklappen | Expandir / recolher | Espandi / comprimi | Развернуть / свернуть
問 AI | 问 AI | Ask AI | AI に質問 | AI에게 질문 | Preguntar a la IA | Demander à l'IA | KI fragen | Perguntar à IA | Chiedi all'IA | Спросить ИИ
按下新的組合 | 按下新的组合 | Press new keys | 新しいキーを押す | 새 키를 누르세요 | Pulsa las teclas | Appuyez sur les touches | Neue Tasten drücken | Prima as teclas | Premi i tasti | Нажмите сочетание
這組快速鍵已經給另一個功能用了 | 这组快捷键已经给另一个功能用了 | That shortcut is already used by the other action | そのショートカットはもう一方の操作で使用中です | 이 단축키는 다른 기능에서 이미 사용 중입니다 | Ese atajo ya lo usa la otra acción | Ce raccourci est déjà utilisé par l'autre action | Dieses Kürzel wird bereits von der anderen Aktion verwendet | Esse atalho já é usado pela outra ação | Questa scorciatoia è già usata dall'altra azione | Это сочетание уже занято другим действием
這組快速鍵被別的程式佔用 | 这组快捷键被其他程序占用 | That shortcut is used by another app | そのショートカットは他のアプリが使用中です | 다른 앱에서 사용 중인 단축키입니다 | Ese atajo lo usa otra aplicación | Ce raccourci est utilisé par une autre application | Dieses Kürzel wird von einer anderen App verwendet | Esse atalho está em uso por outra aplicação | Questa scorciatoia è usata da un'altra app | Это сочетание занято другим приложением
換一份 | 换一份 | Try another source | 別の歌詞にする | 다른 가사로 바꾸기 | Probar otra fuente | Essayer une autre source | Andere Quelle | Tentar outra fonte | Prova un'altra fonte | Другой источник
提早 0.1 秒 | 提前 0.1 秒 | 0.1 s earlier | 0.1秒早く | 0.1초 빠르게 | 0,1 s antes | 0,1 s plus tôt | 0,1 s früher | 0,1 s antes | 0,1 s prima | На 0,1 с раньше
延後 0.1 秒 | 延后 0.1 秒 | 0.1 s later | 0.1秒遅く | 0.1초 느리게 | 0,1 s después | 0,1 s plus tard | 0,1 s später | 0,1 s depois | 0,1 s dopo | На 0,1 с позже
還原 | 还原 | Reset | 元に戻す | 초기화 | Restablecer | Réinitialiser | Zurücksetzen | Repor | Ripristina | Сбросить
開啟歌詞資料夾 | 打开歌词文件夹 | Open lyrics folder | 歌詞フォルダーを開く | 가사 폴더 열기 | Abrir carpeta de letras | Ouvrir le dossier des paroles | Songtext-Ordner öffnen | Abrir pasta de letras | Apri cartella dei testi | Открыть папку с текстами
播放裝置 | 播放设备 | Playback device | 再生デバイス | 재생 장치 | Dispositivo de reproducción | Périphérique de lecture | Wiedergabegerät | Dispositivo de reprodução | Dispositivo di riproduzione | Устройство воспроизведения
無法切換播放裝置 | 无法切换播放设备 | Couldn't switch playback device | 再生デバイスを切り替えられません | 재생 장치를 전환할 수 없습니다 | No se pudo cambiar el dispositivo | Impossible de changer de périphérique | Gerät konnte nicht gewechselt werden | Não foi possível trocar o dispositivo | Impossibile cambiare dispositivo | Не удалось сменить устройство
回到螢幕正中間 | 回到屏幕正中间 | Back to top centre | 画面中央に戻す | 화면 가운데로 | Volver al centro | Revenir au centre | Zurück zur Mitte | Voltar ao centro | Torna al centro | Вернуть в центр
大小 | 大小 | Size | サイズ | 크기 | Tamaño | Taille | Größe | Tamanho | Dimensione | Размер
結束 | 退出 | Quit | 終了 | 종료 | Salir | Quitter | Beenden | Sair | Esci | Выход
音樂 | 音乐 | Music | 音楽 | 음악 | Música | Musique | Musik | Música | Musica | Музыка
聊天 | 聊天 | Chat | チャット | 채팅 | Chat | Chat | Chat | Chat | Chat | Чат
工具 | 工具 | Tools | ツール | 도구 | Herramientas | Outils | Werkzeuge | Ferramentas | Strumenti | Инструменты
設定 | 设置 | Settings | 設定 | 설정 | Ajustes | Réglages | Einstellungen | Definições | Impostazioni | Настройки
語音助手 | 语音助手 | Voice assistant | 音声アシスタント | 음성 비서 | Asistente de voz | Assistant vocal | Sprachassistent | Assistente de voz | Assistente vocale | Голосовой помощник
連接 | 连接 | Connect | 接続 | 연결 | Conectar | Connecter | Verbinden | Ligar | Collega | Подключить
讀取中… | 读取中… | Loading… | 読み込み中… | 불러오는 중… | Cargando… | Chargement… | Wird geladen… | A carregar… | Caricamento… | Загрузка…
近 {0} 天 {1} | 近 {0} 天 {1} | Last {0} days {1} | 直近 {0} 日 {1} | 최근 {0}일 {1} | Últimos {0} días {1} | {0} derniers jours {1} | Letzte {0} Tage {1} | Últimos {0} dias {1} | Ultimi {0} giorni {1} | За {0} дн. {1}
以後都允許 | 以后都允许 | Always allow | 常に許可 | 항상 허용 | Permitir siempre | Toujours autoriser | Immer erlauben | Permitir sempre | Consenti sempre | Всегда разрешать
所有專案 | 所有项目 | All projects | すべてのプロジェクト | 모든 프로젝트 | Todos los proyectos | Tous les projets | Alle Projekte | Todos os projetos | Tutti i progetti | Все проекты
刪除 | 删除 | Delete | 削除 | 삭제 | Eliminar | Supprimer | Löschen | Eliminar | Elimina | Удалить
全部清除 | 全部清除 | Clear all | すべて消去 | 모두 지우기 | Borrar todo | Tout effacer | Alle löschen | Limpar tudo | Cancella tutto | Очистить всё
{0} 完成了 · {1} | {0} 完成了 · {1} | {0} finished · {1} | {0} が完了しました · {1} | {0} 완료 · {1} | {0} terminó · {1} | {0} a terminé · {1} | {0} ist fertig · {1} | {0} terminou · {1} | {0} ha finito · {1} | {0} завершил работу · {1}
{0} 完成了 | {0} 完成了 | {0} finished | {0} が完了しました | {0} 완료 | {0} terminó | {0} a terminé | {0} ist fertig | {0} terminou | {0} ha finito | {0} завершил работу
在 Codex 輸入 /hooks，信任 Shima 的 hook | 在 Codex 输入 /hooks，信任 Shima 的 hook | In Codex, type /hooks and trust Shima's hooks | Codex で /hooks と入力し、Shima のフックを信頼してください | Codex에서 /hooks를 입력하고 Shima의 훅을 신뢰하세요 | En Codex, escribe /hooks y confía en los hooks de Shima | Dans Codex, tapez /hooks et faites confiance aux hooks de Shima | Gib in Codex /hooks ein und vertraue den Hooks von Shima | No Codex, escreva /hooks e confie nos hooks do Shima | In Codex digita /hooks e considera attendibili gli hook di Shima | В Codex введите /hooks и отметьте хуки Shima как доверенные
無法取得 Shima 的連線金鑰，沒有更動 {0} 的設定 | 无法获取 Shima 的连接密钥，没有改动 {0} 的设置 | Couldn't get Shima's connection key, so {0}'s settings were left untouched | Shima の接続キーを取得できなかったため、{0} の設定は変更していません | Shima 연결 키를 가져오지 못해 {0} 설정을 변경하지 않았습니다 | No se pudo obtener la clave de conexión de Shima; no se cambió la configuración de {0} | Impossible d'obtenir la clé de connexion de Shima ; les réglages de {0} n'ont pas été modifiés | Der Verbindungsschlüssel von Shima konnte nicht gelesen werden; die Einstellungen von {0} wurden nicht geändert | Não foi possível obter a chave de ligação do Shima; as definições do {0} não foram alteradas | Impossibile ottenere la chiave di connessione di Shima; le impostazioni di {0} non sono state modificate | Не удалось получить ключ подключения Shima, настройки {0} не изменены
開始／暫停 | 开始／暂停 | Start / pause | 開始／一時停止 | 시작／일시정지 | Iniciar / pausar | Démarrer / pause | Start / Pause | Iniciar / pausar | Avvia / pausa | Старт / пауза
重來 | 重来 | Restart | やり直す | 다시 시작 | Reiniciar | Recommencer | Neu starten | Recomeçar | Ricomincia | Заново
每天 | 每天 | Daily | 毎日 | 매일 | Cada día | Chaque jour | Täglich | Todos os dias | Ogni giorno | Каждый день
平日 | 工作日 | Weekdays | 平日 | 평일 | Entre semana | Jours ouvrés | Werktags | Dias úteis | Feriali | По будням
週日 | 周日 | Sun | 日曜 | 일 | Dom | Dim | So | Dom | Dom | Вс
週一 | 周一 | Mon | 月曜 | 월 | Lun | Lun | Mo | Seg | Lun | Пн
週二 | 周二 | Tue | 火曜 | 화 | Mar | Mar | Di | Ter | Mar | Вт
週三 | 周三 | Wed | 水曜 | 수 | Mié | Mer | Mi | Qua | Mer | Ср
週四 | 周四 | Thu | 木曜 | 목 | Jue | Jeu | Do | Qui | Gio | Чт
週五 | 周五 | Fri | 金曜 | 금 | Vie | Ven | Fr | Sex | Ven | Пт
週六 | 周六 | Sat | 土曜 | 토 | Sáb | Sam | Sa | Sáb | Sab | Сб
清掉做完的 | 清除已完成的 | Clear completed | 完了したものを消去 | 완료된 항목 지우기 | Borrar completadas | Effacer les terminées | Erledigte entfernen | Limpar concluídas | Cancella completate | Убрать выполненные
把檔案拖到島上暫放 | 把文件拖到岛上暂存 | Drop files on the island to keep them here | ファイルを島にドロップして一時保管 | 파일을 섬에 끌어다 놓아 보관 | Suelta archivos en la isla para guardarlos aquí | Déposez des fichiers sur l'île pour les garder ici | Dateien auf die Insel ziehen, um sie abzulegen | Largue ficheiros na ilha para os guardar aqui | Trascina i file sull'isola per tenerli qui | Перетащите файлы на остров, чтобы отложить их
新增待辦，按 Enter | 新增待办，按 Enter | Add a to-do, then press Enter | やることを追加して Enter | 할 일을 입력하고 Enter | Añade una tarea y pulsa Intro | Ajoutez une tâche, puis Entrée | Aufgabe hinzufügen, dann Enter | Adicione uma tarefa e prima Enter | Aggiungi un'attività e premi Invio | Добавьте задачу и нажмите Enter
沒在播歌時顯示 | 没在播歌时显示 | When nothing is playing | 再生していないとき | 재생 중이 아닐 때 | Sin música | Sans musique | Ohne Musik | Sem música | Senza musica | Когда музыка не играет
時間 | 时间 | Time | 時刻 | 시간 | Hora | Heure | Uhrzeit | Hora | Ora | Время
日期與時間 | 日期与时间 | Date and time | 日付と時刻 | 날짜와 시간 | Fecha y hora | Date et heure | Datum und Uhrzeit | Data e hora | Data e ora | Дата и время
下一個提醒 | 下一个提醒 | Next reminder | 次のリマインダー | 다음 알림 | Próximo aviso | Prochain rappel | Nächste Erinnerung | Próximo lembrete | Prossimo promemoria | Ближайшее напоминание
把檔案、文字或圖片拖到島上暫放 | 把文件、文字或图片拖到岛上暂存 | Drop files, text or images on the island to keep them here | ファイル・テキスト・画像を島にドロップして一時保管 | 파일, 텍스트, 이미지를 섬에 끌어다 놓아 보관 | Suelta archivos, texto o imágenes en la isla para guardarlos aquí | Déposez fichiers, texte ou images sur l'île pour les garder ici | Dateien, Text oder Bilder auf die Insel ziehen, um sie abzulegen | Largue ficheiros, texto ou imagens na ilha para os guardar aqui | Trascina file, testo o immagini sull'isola per tenerli qui | Перетащите файлы, текст или изображения на остров, чтобы отложить их
貼上剪貼簿的內容 | 粘贴剪贴板的内容 | Paste from clipboard | クリップボードの内容を貼り付け | 클립보드 내용 붙여넣기 | Pegar del portapapeles | Coller le presse-papiers | Aus der Zwischenablage einfügen | Colar da área de transferência | Incolla dagli appunti | Вставить из буфера обмена
新增提醒，如 15:30 開會、20m 喝水，按 Enter | 新增提醒，如 15:30 开会、20m 喝水，按 Enter | Add a reminder, e.g. 15:30 meeting or 20m water, then press Enter | リマインダーを追加（例：15:30 会議、20m 水）して Enter | 알림을 입력(예: 15:30 회의, 20m 물)하고 Enter | Añade un recordatorio, p. ej. 15:30 reunión o 20m agua, y pulsa Intro | Ajoutez un rappel, p. ex. 15:30 réunion ou 20m eau, puis Entrée | Erinnerung hinzufügen, z. B. 15:30 Meeting oder 20m Wasser, dann Enter | Adicione um lembrete, p. ex. 15:30 reunião ou 20m água, e prima Enter | Aggiungi un promemoria, ad es. 15:30 riunione o 20m acqua, e premi Invio | Добавьте напоминание, например 15:30 встреча или 20m вода, и нажмите Enter
暫存架滿了 | 暂存架满了 | The shelf is full | 一時保管がいっぱいです | 보관함이 가득 찼어요 | La repisa está llena | L'étagère est pleine | Die Ablage ist voll | A prateleira está cheia | Il ripiano è pieno | Полка заполнена
已複製 | 已复制 | Copied | コピーしました | 복사했어요 | Copiado | Copié | Kopiert | Copiado | Copiato | Скопировано
看不懂這個時間 | 看不懂这个时间 | Couldn't read that time | 時間を読み取れません | 시간을 이해하지 못했어요 | No entiendo esa hora | Heure non reconnue | Zeit nicht erkannt | Não entendi essa hora | Ora non riconosciuta | Не удалось понять время
明天 | 明天 | Tomorrow | 明日 | 내일 | Mañana | Demain | Morgen | Amanhã | Domani | Завтра
語言 | 语言 | Language | 言語 | 언어 | Idioma | Langue | Sprache | Idioma | Lingua | Язык
已更新到 {0} | 已更新到 {0} | Updated to {0} | {0} に更新しました | {0}(으)로 업데이트했습니다 | Actualizado a {0} | Mis à jour vers {0} | Auf {0} aktualisiert | Atualizado para {0} | Aggiornato alla {0} | Обновлено до {0}
這一版改了什麼 | 这一版改了什么 | What's new in this version | このバージョンの変更点 | 이번 버전의 변경 사항 | Novedades de esta versión | Nouveautés de cette version | Neuerungen in dieser Version | Novidades desta versão | Novità di questa versione | Что нового в этой версии
備份 | 备份 | Backup | バックアップ | 백업 | Copia de seguridad | Sauvegarde | Sicherung | Cópia de segurança | Backup | Резервная копия
匯出 | 导出 | Export | エクスポート | 내보내기 | Exportar | Exporter | Exportieren | Exportar | Esporta | Экспорт
還原備份 | 还原备份 | Restore | 復元 | 복원 | Restaurar | Restaurer | Wiederherstellen | Restaurar | Ripristina | Восстановить
已匯出備份 | 已导出备份 | Backup exported | バックアップをエクスポートしました | 백업을 내보냈습니다 | Copia exportada | Sauvegarde exportée | Sicherung exportiert | Cópia exportada | Backup esportato | Резервная копия экспортирована
匯出失敗 | 导出失败 | Export failed | エクスポートに失敗しました | 내보내기 실패 | Error al exportar | Échec de l'export | Export fehlgeschlagen | Falha ao exportar | Esportazione non riuscita | Не удалось экспортировать
還原失敗，資料沒有變動 | 还原失败，数据没有变动 | Restore failed, nothing was changed | 復元に失敗しました。データは変更されていません | 복원 실패, 데이터는 그대로입니다 | Error al restaurar, no se cambió nada | Échec de la restauration, rien n'a été modifié | Wiederherstellung fehlgeschlagen, nichts wurde geändert | Falha ao restaurar, nada foi alterado | Ripristino non riuscito, nulla è cambiato | Не удалось восстановить, данные не изменены
還原失敗，原本的資料留在 {0} | 还原失败，原来的数据留在 {0} | Restore failed, your original data is in {0} | 復元に失敗しました。元のデータは {0} にあります | 복원 실패, 원래 데이터는 {0}에 있습니다 | Error al restaurar, tus datos originales están en {0} | Échec de la restauration, vos données d'origine sont dans {0} | Wiederherstellung fehlgeschlagen, die ursprünglichen Daten liegen in {0} | Falha ao restaurar, os dados originais estão em {0} | Ripristino non riuscito, i dati originali sono in {0} | Не удалось восстановить, исходные данные находятся в {0}
這不是有效的備份檔 | 这不是有效的备份文件 | Not a valid backup file | 有効なバックアップファイルではありません | 올바른 백업 파일이 아닙니다 | No es un archivo de copia válido | Ce n'est pas un fichier de sauvegarde valide | Keine gültige Sicherungsdatei | Não é um ficheiro de cópia válido | Non è un file di backup valido | Это не файл резервной копии
備份檔含有不安全的路徑，已拒絕 | 备份文件含有不安全的路径，已拒绝 | Backup contains unsafe paths, rejected | バックアップに安全でないパスが含まれているため拒否しました | 백업에 안전하지 않은 경로가 있어 거부했습니다 | La copia contiene rutas no seguras, rechazada | La sauvegarde contient des chemins dangereux, refusée | Die Sicherung enthält unsichere Pfade und wurde abgelehnt | A cópia contém caminhos inseguros, rejeitada | Il backup contiene percorsi non sicuri, rifiutato | Копия содержит небезопасные пути, отклонена
備份檔太大，已拒絕 | 备份文件太大，已拒绝 | Backup is too large, rejected | バックアップが大きすぎるため拒否しました | 백업이 너무 커서 거부했습니다 | La copia es demasiado grande, rechazada | La sauvegarde est trop volumineuse, refusée | Die Sicherung ist zu groß und wurde abgelehnt | A cópia é demasiado grande, rejeitada | Il backup è troppo grande, rifiutato | Копия слишком велика, отклонена
備份裡的設定檔壞了，已拒絕 | 备份里的设置文件坏了，已拒绝 | Settings in the backup are damaged, rejected | バックアップ内の設定ファイルが壊れているため拒否しました | 백업의 설정 파일이 손상되어 거부했습니다 | Los ajustes de la copia están dañados, rechazada | Les réglages de la sauvegarde sont corrompus, refusée | Die Einstellungen in der Sicherung sind beschädigt und wurden abgelehnt | As definições da cópia estão danificadas, rejeitada | Le impostazioni nel backup sono danneggiate, rifiutato | Настройки в копии повреждены, отклонена
已還原，重新啟動中 | 已还原，正在重新启动 | Restored, restarting | 復元しました。再起動します | 복원했습니다. 다시 시작합니다 | Restaurado, reiniciando | Restauré, redémarrage | Wiederhergestellt, Neustart | Restaurado, a reiniciar | Ripristinato, riavvio | Восстановлено, перезапуск
開機時啟動 | 开机时启动 | Start with Windows | Windows 起動時に開始 | Windows 시작 시 실행 | Iniciar con Windows | Lancer au démarrage de Windows | Mit Windows starten | Iniciar com o Windows | Avvia con Windows | Запускать вместе с Windows
全螢幕時隱藏 | 全屏时隐藏 | Hide in full screen | 全画面のときは隠す | 전체 화면에서 숨기기 | Ocultar en pantalla completa | Masquer en plein écran | Im Vollbild ausblenden | Ocultar em ecrã inteiro | Nascondi a schermo intero | Скрывать в полноэкранном режиме
跟著聲音動 | 跟着声音动 | React to sound | 音に合わせて動く | 소리에 맞춰 움직이기 | Reaccionar al sonido | Réagir au son | Auf Ton reagieren | Reagir ao som | Reagisci al suono | Реагировать на звук
膠囊顯示進度 | 胶囊显示进度 | Progress line on the pill | カプセルに再生位置を表示 | 캡슐에 재생 위치 표시 | Progreso en la cápsula | Progression sur la capsule | Fortschritt auf der Kapsel | Progresso na cápsula | Avanzamento sulla capsula | Прогресс на капсуле
歌詞行數 | 歌词行数 | Lyrics lines | 歌詞の行数 | 가사 줄 수 | Líneas de letra | Lignes de paroles | Textzeilen | Linhas da letra | Righe del testo | Строк текста
少 | 少 | Few | 少なめ | 적게 | Pocas | Peu | Wenige | Poucas | Poche | Мало
中 | 中 | Medium | ふつう | 보통 | Normal | Moyen | Mittel | Médio | Medio | Средне
多 | 多 | Many | 多め | 많이 | Muchas | Beaucoup | Viele | Muitas | Molte | Много
天藍 | 天蓝 | Sky blue | スカイブルー | 하늘색 | Celeste | Bleu ciel | Himmelblau | Azul-celeste | Azzurro | Небесный
薄荷 | 薄荷 | Mint | ミント | 민트 | Menta | Menthe | Mint | Menta | Menta | Мятный
櫻花粉 | 樱花粉 | Sakura pink | 桜色 | 벚꽃색 | Rosa sakura | Rose sakura | Kirschblütenrosa | Rosa-sakura | Rosa ciliegio | Сакура
薰衣草 | 薰衣草 | Lavender | ラベンダー | 라벤더 | Lavanda | Lavande | Lavendel | Lavanda | Lavanda | Лавандовый
檸檬 | 柠檬 | Lemon | レモン | 레몬 | Limón | Citron | Zitrone | Limão | Limone | Лимонный
一般 | 常规 | General | 一般 | 일반 | General | Général | Allgemein | Geral | Generale | Общие
AI 與工具 | AI 与工具 | AI & tools | AI とツール | AI 및 도구 | IA y herramientas | IA et outils | KI & Werkzeuge | IA e ferramentas | IA e strumenti | ИИ и инструменты
顯示訊息通知 | 显示消息通知 | Show message notifications | メッセージ通知を表示 | 메시지 알림 표시 | Mostrar notificaciones de mensajes | Afficher les notifications de messages | Benachrichtigungen anzeigen | Mostrar notificações de mensagens | Mostra notifiche dei messaggi | Показывать уведомления о сообщениях
最近的通知 | 最近的通知 | Recent notifications | 最近の通知 | 최근 알림 | Notificaciones recientes | Notifications récentes | Letzte Benachrichtigungen | Notificações recentes | Notifiche recenti | Недавние уведомления
提醒 | 提醒 | Reminder | リマインダー | 알림 | Recordatorio | Rappel | Erinnerung | Lembrete | Promemoria | Напоминание
完成了 | 完成了 | Finished | 完了しました | 완료 | Terminó | Terminé | Fertig | Terminou | Finito | Завершено
無法取得 Shima 的連線金鑰，沒有更動 Claude Code 的設定 | 无法获取 Shima 的连接密钥，没有改动 Claude Code 的设置 | Couldn't get Shima's connection key, so Claude Code's settings were left untouched | Shima の接続キーを取得できなかったため、Claude Code の設定は変更していません | Shima 연결 키를 가져오지 못해 Claude Code 설정을 변경하지 않았습니다 | No se pudo obtener la clave de conexión de Shima; no se cambió la configuración de Claude Code | Impossible d'obtenir la clé de connexion de Shima ; les réglages de Claude Code n'ont pas été modifiés | Der Verbindungsschlüssel von Shima konnte nicht gelesen werden; die Claude-Code-Einstellungen wurden nicht geändert | Não foi possível obter a chave de ligação do Shima; as definições do Claude Code não foram alteradas | Impossibile ottenere la chiave di connessione di Shima; le impostazioni di Claude Code non sono state modificate | Не удалось получить ключ подключения Shima, настройки Claude Code не изменены
寫入的設定檔內容不完整，沒有更動 | 写入的设置文件内容不完整，没有改动 | The settings file didn't write out completely, so nothing was changed | 設定ファイルを正しく書き込めなかったため、変更していません | 설정 파일이 온전히 기록되지 않아 변경하지 않았습니다 | El archivo de configuración no se escribió completo; no se cambió nada | Le fichier de réglages n'a pas été écrit en entier ; rien n'a été modifié | Die Einstellungsdatei wurde nicht vollständig geschrieben; nichts wurde geändert | O ficheiro de definições não foi escrito por completo; nada foi alterado | Il file delle impostazioni non è stato scritto per intero; nessuna modifica | Файл настроек записан не полностью, изменений нет
Roon 搜尋 | Roon 搜索 | Roon search | Roon 検索 | Roon 검색 | Búsqueda en Roon | Recherche Roon | Roon-Suche | Pesquisa no Roon | Ricerca in Roon | Поиск в Roon
等待授權… | 等待授权… | Waiting for approval… | 承認を待っています… | 승인 대기 중… | Esperando autorización… | En attente d'autorisation… | Warte auf Freigabe… | A aguardar autorização… | In attesa di autorizzazione… | Ожидание разрешения…
先填 API key 與 Shared secret | 先填 API key 与 Shared secret | Enter the API key and shared secret first | 先に API key と Shared secret を入力してください | 먼저 API key와 Shared secret을 입력하세요 | Introduce primero la API key y el shared secret | Saisissez d'abord l'API key et le shared secret | Zuerst API key und Shared secret eintragen | Introduza primeiro a API key e o shared secret | Inserisci prima API key e shared secret | Сначала введите API key и Shared secret
Last.fm 連接失敗 | Last.fm 连接失败 | Couldn't connect to Last.fm | Last.fm に接続できませんでした | Last.fm에 연결하지 못했습니다 | No se pudo conectar con Last.fm | Connexion à Last.fm impossible | Verbindung mit Last.fm fehlgeschlagen | Não foi possível ligar ao Last.fm | Impossibile connettersi a Last.fm | Не удалось подключиться к Last.fm
Last.fm 授權失效，請重新連接 | Last.fm 授权失效，请重新连接 | Last.fm authorization expired; please connect again | Last.fm の認証が無効になりました。もう一度接続してください | Last.fm 인증이 만료되었습니다. 다시 연결하세요 | La autorización de Last.fm ya no es válida; vuelve a conectar | L'autorisation Last.fm n'est plus valide, reconnectez-vous | Last.fm-Autorisierung ungültig, bitte erneut verbinden | A autorização do Last.fm deixou de ser válida; ligue novamente | L'autorizzazione di Last.fm non è più valida, collega di nuovo | Авторизация Last.fm недействительна, подключитесь заново
到 Roon「設定 → 擴充功能」啟用 Shima | 到 Roon“设置 → 扩展”启用 Shima | Enable Shima in Roon under Settings → Extensions | Roon の「設定 → 拡張機能」で Shima を有効にしてください | Roon의 “설정 → 확장”에서 Shima를 활성화하세요 | Activa Shima en Roon, en Ajustes → Extensiones | Activez Shima dans Roon sous Paramètres → Extensions | Shima in Roon unter Einstellungen → Erweiterungen aktivieren | Ative o Shima no Roon em Definições → Extensões | Attiva Shima in Roon da Impostazioni → Estensioni | Включите Shima в Roon: Настройки → Расширения
重新啟動 Shima，再到 Roon「設定 → 擴充功能」啟用一次 | 重新启动 Shima，再到 Roon“设置 → 扩展”启用一次 | Restart Shima, then enable it once more in Roon under Settings → Extensions | Shima を再起動して、Roon の「設定 → 拡張機能」でもう一度有効にしてください | Shima를 다시 시작한 뒤 Roon의 “설정 → 확장”에서 한 번 더 활성화하세요 | Reinicia Shima y vuelve a activarlo en Roon, en Ajustes → Extensiones | Redémarrez Shima, puis réactivez-le dans Roon sous Paramètres → Extensions | Shima neu starten und in Roon unter Einstellungen → Erweiterungen erneut aktivieren | Reinicie o Shima e volte a ativá-lo no Roon em Definições → Extensões | Riavvia Shima, poi riattivalo in Roon da Impostazioni → Estensioni | Перезапустите Shima и снова включите его в Roon: Настройки → Расширения
複製歌詞卡片 | 复制歌词卡片 | Copy lyric card | 歌詞カードをコピー | 가사 카드 복사 | Copiar tarjeta de letra | Copier la carte de paroles | Songtext-Karte kopieren | Copiar cartão da letra | Copia scheda del testo | Скопировать карточку с текстом
歌詞卡片已複製 | 歌词卡片已复制 | Lyric card copied | 歌詞カードをコピーしました | 가사 카드를 복사했습니다 | Tarjeta de letra copiada | Carte de paroles copiée | Songtext-Karte kopiert | Cartão da letra copiado | Scheda del testo copiata | Карточка скопирована
檢查新版 | 检查新版本 | Check for updates | アップデートを確認 | 업데이트 확인 | Buscar actualizaciones | Rechercher les mises à jour | Nach Updates suchen | Procurar atualizações | Controlla aggiornamenti | Проверять обновления
有新版 {0} | 有新版本 {0} | Version {0} is available | 新しいバージョン {0} があります | 새 버전 {0}이 있습니다 | Hay una versión nueva: {0} | La version {0} est disponible | Version {0} ist verfügbar | Há uma versão nova: {0} | È disponibile la versione {0} | Доступна версия {0}
更新到 {0} | 更新到 {0} | Update to {0} | {0} にアップデート | {0}(으)로 업데이트 | Actualizar a {0} | Mettre à jour vers {0} | Auf {0} aktualisieren | Atualizar para {0} | Aggiorna a {0} | Обновить до {0}
正在下載新版… | 正在下载新版本… | Downloading the update… | アップデートをダウンロード中… | 업데이트 다운로드 중… | Descargando la actualización… | Téléchargement de la mise à jour… | Update wird heruntergeladen… | A transferir a atualização… | Download dell'aggiornamento… | Загрузка обновления…
更新失敗 | 更新失败 | Update failed | アップデートに失敗しました | 업데이트 실패 | La actualización falló | Échec de la mise à jour | Update fehlgeschlagen | A atualização falhou | Aggiornamento non riuscito | Не удалось обновить
重新開啟 Stream Dock 後，把 Shima 拖到按鍵上 | 重新打开 Stream Dock 后，把 Shima 拖到按键上 | Restart Stream Dock, then drag Shima onto a key | Stream Dock を再起動して、Shima をキーにドラッグ | Stream Dock을 다시 연 뒤 Shima를 키에 끌어다 놓으세요 | Reinicia Stream Dock y arrastra Shima a una tecla | Redémarrez Stream Dock, puis glissez Shima sur une touche | Stream Dock neu starten, dann Shima auf eine Taste ziehen | Reinicie o Stream Dock e arraste Shima para uma tecla | Riavvia Stream Dock, poi trascina Shima su un tasto | Перезапустите Stream Dock и перетащите Shima на клавишу
顯示音量、電量、網路的變化 | 显示音量、电量、网络的变化 | Show volume, battery and network changes | 音量・バッテリー・ネットワークの変化を表示 | 음량·배터리·네트워크 변화 표시 | Mostrar cambios de volumen, batería y red | Afficher les changements de volume, batterie et réseau | Lautstärke, Akku und Netzwerk anzeigen | Mostrar alterações de volume, bateria e rede | Mostra variazioni di volume, batteria e rete | Показывать изменения громкости, заряда и сети
每隔幾分鐘 | 每隔几分钟 | Every how many minutes | 何分おき | 몇 분마다 | Cada cuántos minutos | Toutes les combien de minutes | Alle wie viele Minuten | De quantos em quantos minutos | Ogni quanti minuti | Каждые сколько минут
分鐘 | 分钟 | min | 分 | 분 | min | min | Min. | min | min | мин
喝水提醒 | 喝水提醒 | Water reminder | 水分補給リマインダー | 물 마시기 알림 | Recordatorio de agua | Rappel d'hydratation | Trinkerinnerung | Lembrete de água | Promemoria per bere | Напоминание о воде
歌詞來源 | 歌词来源 | Lyrics sources | 歌詞の取得元 | 가사 출처 | Fuentes de letras | Sources des paroles | Songtext-Quellen | Fontes de letras | Fonti dei testi | Источники текстов
加入 | 添加 | Add | 追加 | 추가 | Añadir | Ajouter | Hinzufügen | Adicionar | Aggiungi | Добавить
歌詞資料夾 | 歌词文件夹 | Lyrics folders | 歌詞フォルダー | 가사 폴더 | Carpetas de letras | Dossiers de paroles | Songtext-Ordner | Pastas de letras | Cartelle dei testi | Папки с текстами
番茄鐘：專注／休息（分鐘） | 番茄钟：专注／休息（分钟） | Pomodoro: focus / break (minutes) | ポモドーロ：集中／休憩（分） | 뽀모도로: 집중／휴식(분) | Pomodoro: concentración / descanso (minutos) | Pomodoro : concentration / pause (minutes) | Pomodoro: Fokus / Pause (Minuten) | Pomodoro: foco / pausa (minutos) | Pomodoro: concentrazione / pausa (minuti) | Помодоро: работа / отдых (минуты)
回終端機 | 回终端 | In terminal | ターミナルで | 터미널에서 | En la terminal | Dans le terminal | Im Terminal | No terminal | Nel terminale | В терминале
拒絕 | 拒绝 | Deny | 拒否 | 거부 | Denegar | Refuser | Ablehnen | Recusar | Nega | Отклонить
允許 | 允许 | Allow | 許可 | 허용 | Permitir | Autoriser | Erlauben | Permitir | Consenti | Разрешить
{0} 不是有效的 JSON，沒有更動 | {0} 不是有效的 JSON，未作改动 | {0} is not valid JSON; nothing was changed | {0} は有効な JSON ではないため変更していません | {0}은(는) 올바른 JSON이 아니어서 변경하지 않았습니다 | {0} no es JSON válido; no se cambió nada | {0} n'est pas un JSON valide ; rien n'a été modifié | {0} ist kein gültiges JSON; nichts wurde geändert | {0} não é JSON válido; nada foi alterado | {0} non è JSON valido; nessuna modifica | {0} — некорректный JSON; ничего не изменено
語音辨識無法使用：請到 Windows 設定 → 隱私權 → 語音，開啟線上語音辨識 | 语音识别无法使用：请到 Windows 设置 → 隐私 → 语音，开启在线语音识别 | Speech recognition is unavailable: turn on online speech recognition in Windows Settings → Privacy → Speech | 音声認識を使えません：Windows の設定 → プライバシー → 音声認識 でオンライン音声認識をオンにしてください | 음성 인식을 사용할 수 없습니다: Windows 설정 → 개인 정보 → 음성에서 온라인 음성 인식을 켜세요 | El reconocimiento de voz no está disponible: actívalo en Configuración de Windows → Privacidad → Voz | Reconnaissance vocale indisponible : activez-la dans Paramètres Windows → Confidentialité → Voix | Spracherkennung nicht verfügbar: in Windows-Einstellungen → Datenschutz → Spracherkennung aktivieren | Reconhecimento de voz indisponível: ative-o em Definições do Windows → Privacidade → Voz | Riconoscimento vocale non disponibile: attivalo in Impostazioni di Windows → Privacy → Riconoscimento vocale | Распознавание речи недоступно: включите его в Параметрах Windows → Конфиденциальность → Речь
聆聽中… | 聆听中… | Listening… | 聞いています… | 듣는 중… | Escuchando… | À l'écoute… | Ich höre zu… | A ouvir… | In ascolto… | Слушаю…
API 金鑰無效 | API 密钥无效 | Invalid API key | API キーが無効です | API 키가 올바르지 않습니다 | Clave de API no válida | Clé API non valide | Ungültiger API-Schlüssel | Chave de API inválida | Chiave API non valida | Неверный ключ API
用量已達上限，請稍後再試 | 用量已达上限，请稍后再试 | Rate limit reached; try again later | 利用上限に達しました。しばらくしてからお試しください | 사용 한도에 도달했습니다. 잠시 후 다시 시도하세요 | Límite de uso alcanzado; inténtalo más tarde | Limite d'utilisation atteinte ; réessayez plus tard | Nutzungslimit erreicht; später erneut versuchen | Limite de utilização atingido; tente mais tarde | Limite di utilizzo raggiunto; riprova più tardi | Лимит исчерпан; попробуйте позже
沒有麥克風權限：請到 Windows 設定 → 隱私權 → 麥克風開啟 | 没有麦克风权限：请到 Windows 设置 → 隐私 → 麦克风开启 | No microphone access: allow it in Windows Settings → Privacy → Microphone | マイクを使えません：Windows の設定 → プライバシー → マイク で許可してください | 마이크 권한이 없습니다: Windows 설정 → 개인 정보 → 마이크에서 허용하세요 | Sin acceso al micrófono: permítelo en Configuración de Windows → Privacidad → Micrófono | Pas d'accès au micro : autorisez-le dans Paramètres Windows → Confidentialité → Microphone | Kein Mikrofonzugriff: in Windows-Einstellungen → Datenschutz → Mikrofon erlauben | Sem acesso ao microfone: permita-o em Definições do Windows → Privacidade → Microfone | Nessun accesso al microfono: consentilo in Impostazioni di Windows → Privacy → Microfono | Нет доступа к микрофону: разрешите его в Параметрах Windows → Конфиденциальность → Микрофон
語音助手無法啟動 | 语音助手无法启动 | The voice assistant could not start | 音声アシスタントを開始できません | 음성 비서를 시작할 수 없습니다 | No se pudo iniciar el asistente de voz | Impossible de lancer l'assistant vocal | Sprachassistent konnte nicht starten | Não foi possível iniciar o assistente de voz | Impossibile avviare l'assistente vocale | Не удалось запустить голосового помощника
這個我沒辦法幫忙。 | 这个我帮不上忙。 | I can't help with that. | それはお手伝いできません。 | 그건 도와드릴 수 없어요. | No puedo ayudar con eso. | Je ne peux pas vous aider pour cela. | Dabei kann ich nicht helfen. | Não posso ajudar com isso. | Non posso aiutarti con questo. | С этим я помочь не могу.
嗯。 | 嗯。 | Mm. | うん。 | 음. | Mm. | Hmm. | Hm. | Hum. | Mm. | Угу.
我有點卡住了，再說一次好嗎？ | 我有点卡住了，再说一次好吗？ | I got stuck. Could you say that again? | うまくいきませんでした。もう一度お願いします。 | 잠깐 막혔어요. 다시 말씀해 주시겠어요? | Me he atascado. ¿Puedes repetirlo? | Je suis bloqué. Pouvez-vous répéter ? | Ich hänge fest. Kannst du das wiederholen? | Fiquei preso. Pode repetir? | Mi sono bloccato. Puoi ripetere? | Я запутался. Повторите, пожалуйста.
Claude Code 完成了 · {0} | Claude Code 完成了 · {0} | Claude Code finished · {0} | Claude Code が完了 · {0} | Claude Code 완료 · {0} | Claude Code ha terminado · {0} | Claude Code a terminé · {0} | Claude Code ist fertig · {0} | Claude Code terminou · {0} | Claude Code ha finito · {0} | Claude Code закончил · {0}
Claude Code 完成了 | Claude Code 完成了 | Claude Code finished | Claude Code が完了 | Claude Code 완료 | Claude Code ha terminado | Claude Code a terminé | Claude Code ist fertig | Claude Code terminou | Claude Code ha finito | Claude Code закончил
中斷 | 断开 | Disconnect | 切断 | 연결 해제 | Desconectar | Déconnecter | Trennen | Desligar | Scollega | Отключить
無法啟動內嵌瀏覽器（需要 Microsoft Edge WebView2） | 无法启动内嵌浏览器（需要 Microsoft Edge WebView2） | The embedded browser could not start (Microsoft Edge WebView2 is required) | 埋め込みブラウザーを起動できません（Microsoft Edge WebView2 が必要です） | 내장 브라우저를 시작할 수 없습니다(Microsoft Edge WebView2 필요) | No se pudo iniciar el navegador integrado (se necesita Microsoft Edge WebView2) | Impossible de lancer le navigateur intégré (Microsoft Edge WebView2 requis) | Der eingebettete Browser konnte nicht starten (Microsoft Edge WebView2 erforderlich) | Não foi possível iniciar o navegador incorporado (requer Microsoft Edge WebView2) | Impossibile avviare il browser incorporato (richiede Microsoft Edge WebView2) | Не удалось запустить встроенный браузер (нужен Microsoft Edge WebView2)
顯示／隱藏 | 显示／隐藏 | Show / hide | 表示／非表示 | 표시／숨기기 | Mostrar / ocultar | Afficher / masquer | Anzeigen / ausblenden | Mostrar / ocultar | Mostra / nascondi | Показать / скрыть
跟著 Windows | 跟随 Windows | Same as Windows | Windows に合わせる | Windows와 동일 | Igual que Windows | Comme Windows | Wie Windows | Igual ao Windows | Come Windows | Как в Windows
自己的 .lrc | 自己的 .lrc | My .lrc files | 自分の .lrc | 내 .lrc | Mis .lrc | Mes .lrc | Eigene .lrc | Os meus .lrc | I miei .lrc | Свои .lrc
網易雲 | 网易云 | NetEase | NetEase | NetEase | NetEase | NetEase | NetEase | NetEase | NetEase | NetEase
QQ 音樂 | QQ 音乐 | QQ Music | QQ Music | QQ Music | QQ Music | QQ Music | QQ Music | QQ Music | QQ Music | QQ Music
酷狗 | 酷狗 | Kugou | Kugou | Kugou | Kugou | Kugou | Kugou | Kugou | Kugou | Kugou
按一下移除 | 点一下移除 | Click to remove | クリックで削除 | 클릭하여 제거 | Clic para quitar | Cliquer pour retirer | Klicken zum Entfernen | Clique para remover | Clic per rimuovere | Нажмите, чтобы убрать
Windows 沒有允許通知存取 | Windows 没有允许通知访问 | Windows did not allow notification access | Windows が通知へのアクセスを許可していません | Windows에서 알림 접근이 허용되지 않았습니다 | Windows no permitió el acceso a las notificaciones | Windows n'a pas autorisé l'accès aux notifications | Windows hat den Zugriff auf Benachrichtigungen nicht erlaubt | O Windows não permitiu o acesso às notificações | Windows non ha consentito l'accesso alle notifiche | Windows не разрешил доступ к уведомлениям
放 .lrc 歌詞檔的資料夾 | 存放 .lrc 歌词文件的文件夹 | Folder with .lrc lyrics files | .lrc 歌詞ファイルのフォルダー | .lrc 가사 파일이 있는 폴더 | Carpeta con archivos de letras .lrc | Dossier contenant des fichiers de paroles .lrc | Ordner mit .lrc-Songtextdateien | Pasta com ficheiros de letras .lrc | Cartella con file di testo .lrc | Папка с файлами текстов .lrc
該喝水了 | 该喝水了 | Time for some water | 水を飲みましょう | 물 마실 시간이에요 | Hora de beber agua | C'est l'heure de boire de l'eau | Zeit, etwas zu trinken | Hora de beber água | È ora di bere | Пора выпить воды
休息一下 | 休息一下 | Take a break | 休憩しましょう | 잠깐 쉬어요 | Tómate un descanso | Faites une pause | Mach eine Pause | Faça uma pausa | Fai una pausa | Сделайте перерыв
回來專注 | 回来专注 | Back to focus | 集中に戻りましょう | 다시 집중할 시간 | De vuelta al trabajo | Retour à la concentration | Zurück zum Fokus | De volta ao foco | Torna a concentrarti | Снова за работу
休息 | 休息 | Break | 休憩 | 휴식 | Descanso | Pause | Pause | Pausa | Pausa | Отдых
專注 | 专注 | Focus | 集中 | 집중 | Concentración | Concentration | Fokus | Foco | Concentrazione | Работа
沒有正在播放的音樂 | 没有正在播放的音乐 | Nothing is playing | 再生中の曲はありません | 재생 중인 음악이 없습니다 | No hay nada reproduciéndose | Rien en lecture | Es läuft nichts | Nada a tocar | Niente in riproduzione | Ничего не играет
睡眠定時 | 睡眠定时 | Sleep timer | スリープタイマー | 취침 타이머 | Temporizador de apagado | Minuteur de veille | Einschlaf-Timer | Temporizador de sono | Timer di spegnimento | Таймер сна
{0} 分鐘 | {0} 分钟 | {0} min | {0}分 | {0}분 | {0} min | {0} min | {0} Min. | {0} min | {0} min | {0} мин
這首播完 | 这首播完 | After this song | この曲の終わりまで | 이 곡이 끝나면 | Al terminar esta canción | À la fin de ce titre | Nach diesem Lied | Após esta música | Alla fine di questo brano | После этой песни
關閉 | 关闭 | Off | オフ | 끄기 | Desactivar | Désactiver | Aus | Desligar | Disattiva | Выключить
睡眠定時：已暫停播放 | 睡眠定时：已暂停播放 | Sleep timer: playback paused | スリープタイマー：再生を一時停止しました | 취침 타이머: 재생을 일시정지했어요 | Temporizador de apagado: reproducción en pausa | Minuteur de veille : lecture en pause | Einschlaf-Timer: Wiedergabe pausiert | Temporizador de sono: reprodução pausada | Timer di spegnimento: riproduzione in pausa | Таймер сна: воспроизведение приостановлено
睡眠定時：剩 {0} | 睡眠定时：剩 {0} | Sleep timer: {0} left | スリープタイマー：残り {0} | 취침 타이머: {0} 남음 | Temporizador de apagado: quedan {0} | Minuteur de veille : {0} restantes | Einschlaf-Timer: noch {0} | Temporizador de sono: faltam {0} | Timer di spegnimento: mancano {0} | Таймер сна: осталось {0}
睡眠定時：這首播完 | 睡眠定时：这首播完 | Sleep timer: after this song | スリープタイマー：この曲の終わりまで | 취침 타이머: 이 곡이 끝나면 | Temporizador de apagado: al terminar esta canción | Minuteur de veille : à la fin de ce titre | Einschlaf-Timer: nach diesem Lied | Temporizador de sono: após esta música | Timer di spegnimento: alla fine di questo brano | Таймер сна: после этой песни
播放區域：{0} | 播放区域：{0} | Zone: {0} | ゾーン：{0} | 영역: {0} | Zona: {0} | Zone : {0} | Zone: {0} | Zona: {0} | Zona: {0} | Зона: {0}
音量 {0} | 音量 {0} | Volume {0} | 音量 {0} | 음량 {0} | Volumen {0} | Volume {0} | Lautstärke {0} | Volume {0} | Volume {0} | Громкость {0}
靜音 | 静音 | Muted | ミュート | 음소거 | Silenciado | Muet | Stumm | Sem som | Muto | Без звука
取消靜音 | 取消静音 | Unmuted | ミュート解除 | 음소거 해제 | Sonido activado | Son rétabli | Ton an | Som ativado | Audio attivo | Звук включён
展開的快速鍵：{0} | 展开的快捷键：{0} | Expand shortcut: {0} | 展開のショートカット：{0} | 펼치기 단축키: {0} | Atajo para expandir: {0} | Raccourci pour déployer : {0} | Tastenkürzel zum Aufklappen: {0} | Atalho para expandir: {0} | Scorciatoia per espandere: {0} | Сочетание для раскрытия: {0}
找不到其他歌詞 | 找不到其他歌词 | No other lyrics found | ほかの歌詞が見つかりません | 다른 가사를 찾지 못했습니다 | No se encontraron otras letras | Aucune autre parole trouvée | Kein anderer Songtext gefunden | Não foram encontradas outras letras | Nessun altro testo trovato | Других текстов не найдено
只有 {0} 有這首歌的歌詞 | 只有 {0} 有这首歌的歌词 | Only {0} has lyrics for this song | この曲の歌詞があるのは {0} だけです | 이 곡의 가사는 {0}에만 있습니다 | Solo {0} tiene la letra de esta canción | Seul {0} a les paroles de ce titre | Nur {0} hat den Text dieses Titels | Só {0} tem a letra desta música | Solo {0} ha il testo di questo brano | Текст этой песни есть только в {0}
存成 .lrc | 存成 .lrc | Save as .lrc | .lrc として保存 | .lrc로 저장 | Guardar como .lrc | Enregistrer en .lrc | Als .lrc speichern | Guardar como .lrc | Salva come .lrc | Сохранить как .lrc
歌詞已存成 {0} | 歌词已存为 {0} | Lyrics saved as {0} | 歌詞を {0} に保存しました | 가사를 {0}(으)로 저장했습니다 | Letra guardada como {0} | Paroles enregistrées sous {0} | Songtext gespeichert als {0} | Letra guardada como {0} | Testo salvato come {0} | Текст сохранён как {0}
無法儲存歌詞 | 无法保存歌词 | Couldn't save the lyrics | 歌詞を保存できません | 가사를 저장할 수 없습니다 | No se pudo guardar la letra | Impossible d'enregistrer les paroles | Songtext konnte nicht gespeichert werden | Não foi possível guardar a letra | Impossibile salvare il testo | Не удалось сохранить текст
複製全文 | 复制全文 | Copy all lyrics | 歌詞をすべてコピー | 가사 전체 복사 | Copiar toda la letra | Copier toutes les paroles | Ganzen Songtext kopieren | Copiar toda a letra | Copia tutto il testo | Скопировать весь текст
用 AI 翻譯 | 用 AI 翻译 | Translate with AI | AI で翻訳 | AI로 번역 | Traducir con IA | Traduire avec l'IA | Mit KI übersetzen | Traduzir com IA | Traduci con l'IA | Перевести с помощью ИИ
翻譯中… | 翻译中… | Translating… | 翻訳中… | 번역 중… | Traduciendo… | Traduction… | Wird übersetzt… | A traduzir… | Traduzione in corso… | Перевод…
翻譯好了 | 翻译好了 | Translation ready | 翻訳できました | 번역했습니다 | Traducción lista | Traduction prête | Übersetzung fertig | Tradução pronta | Traduzione pronta | Перевод готов
AI 的翻譯跟歌詞對不上 | AI 的翻译跟歌词对不上 | The AI translation doesn't match the lyrics | AI の翻訳が歌詞と合いません | AI 번역이 가사와 맞지 않습니다 | La traducción de la IA no coincide con la letra | La traduction de l'IA ne correspond pas aux paroles | Die KI-Übersetzung passt nicht zum Songtext | A tradução da IA não corresponde à letra | La traduzione dell'IA non corrisponde al testo | Перевод ИИ не совпадает с текстом
歌詞：{0} | 歌词：{0} | Lyrics: {0} | 歌詞：{0} | 가사: {0} | Letra: {0} | Paroles : {0} | Songtext: {0} | Letra: {0} | Testo: {0} | Текст: {0}
歌詞時間：原本 | 歌词时间：原始 | Lyrics timing: original | 歌詞のタイミング：元のまま | 가사 타이밍: 원래대로 | Tiempo de la letra: original | Synchronisation : d'origine | Songtext-Timing: Original | Tempo da letra: original | Sincronia del testo: originale | Синхронизация: исходная
歌詞提早 {0} 秒 | 歌词提前 {0} 秒 | Lyrics {0} s earlier | 歌詞を {0} 秒早く | 가사 {0}초 빠르게 | Letra {0} s antes | Paroles {0} s plus tôt | Songtext {0} s früher | Letra {0} s antes | Testo {0} s prima | Текст на {0} с раньше
歌詞延後 {0} 秒 | 歌词延后 {0} 秒 | Lyrics {0} s later | 歌詞を {0} 秒遅く | 가사 {0}초 느리게 | Letra {0} s después | Paroles {0} s plus tard | Songtext {0} s später | Letra {0} s depois | Testo {0} s dopo | Текст на {0} с позже
中文翻譯 | 中文翻译 | Chinese translation | 中国語訳 | 중국어 번역 | Traducción al chino | Traduction en chinois | Chinesische Übersetzung | Tradução em chinês | Traduzione in cinese | Китайский перевод
羅馬拼音 | 罗马音 | Romaji | ローマ字 | 로마자 | Romaji | Romaji | Romaji | Romaji | Romaji | Ромадзи
不顯示 | 不显示 | None | 表示しない | 표시 안 함 | Ninguna | Aucune | Keine | Nenhuma | Nessuna | Не показывать
眼睛 | 眼睛 | Eyes | 目 | 눈 | Ojos | Yeux | Augen | Olhos | Occhi | Глаза
貓 | 猫 | Cat | 猫 | 고양이 | Gato | Chat | Katze | Gato | Gatto | Кот
幽靈 | 幽灵 | Ghost | おばけ | 유령 | Fantasma | Fantôme | Geist | Fantasma | Fantasma | Привидение
不要角色 | 不要角色 | No character | キャラクターなし | 캐릭터 없음 | Sin personaje | Aucun personnage | Keine Figur | Sem personagem | Nessun personaggio | Без персонажа
{0} · {1}：{2} | {0} · {1}：{2} | {0} · {1}: {2} | {0} · {1}：{2} | {0} · {1}: {2} | {0} · {1}: {2} | {0} · {1} : {2} | {0} · {1}: {2} | {0} · {1}: {2} | {0} · {1}: {2} | {0} · {1}: {2}
靜音 | 静音 | Muted | ミュート | 음소거 | Silenciado | Muet | Stumm | Sem som | Muto | Без звука
亮度 {0} | 亮度 {0} | Brightness {0} | 明るさ {0} | 밝기 {0} | Brillo {0} | Luminosité {0} | Helligkeit {0} | Brilho {0} | Luminosità {0} | Яркость {0}
充電中 {0}% | 充电中 {0}% | Charging {0}% | 充電中 {0}% | 충전 중 {0}% | Cargando {0} % | En charge {0} % | Lädt {0} % | A carregar {0}% | In carica {0}% | Зарядка {0} %
使用電池 {0}% | 使用电池 {0}% | On battery {0}% | バッテリー駆動 {0}% | 배터리 사용 중 {0}% | Con batería {0} % | Sur batterie {0} % | Akkubetrieb {0} % | A bateria {0}% | A batteria {0}% | От батареи {0} %
電量不足 {0}% | 电量不足 {0}% | Battery low {0}% | バッテリー残量低下 {0}% | 배터리 부족 {0}% | Batería baja {0} % | Batterie faible {0} % | Akku schwach {0} % | Bateria fraca {0}% | Batteria scarica {0}% | Низкий заряд {0} %
已連上 {0} | 已连接 {0} | Connected to {0} | {0} に接続しました | {0}에 연결됨 | Conectado a {0} | Connecté à {0} | Verbunden mit {0} | Ligado a {0} | Connesso a {0} | Подключено к {0}
網路已連線 | 网络已连接 | Network connected | ネットワークに接続しました | 네트워크 연결됨 | Red conectada | Réseau connecté | Netzwerk verbunden | Rede ligada | Rete connessa | Сеть подключена
網路已中斷 | 网络已断开 | Network disconnected | ネットワークが切断されました | 네트워크 연결 끊김 | Red desconectada | Réseau déconnecté | Netzwerk getrennt | Rede desligada | Rete disconnessa | Сеть отключена
問 AI，按 Enter | 问 AI，按 Enter | Ask the AI, then press Enter | AI に質問して Enter | AI에게 묻고 Enter | Pregunta a la IA y pulsa Intro | Posez une question à l'IA, puis Entrée | Die KI fragen, dann Enter | Pergunte à IA e prima Enter | Chiedi all'IA e premi Invio | Спросите ИИ и нажмите Enter
AI 服務 | AI 服务 | AI service | AI サービス | AI 서비스 | Servicio de IA | Service d'IA | KI-Dienst | Serviço de IA | Servizio IA | Сервис ИИ
模型 | 模型 | Model | モデル | 모델 | Modelo | Modèle | Modell | Modelo | Modello | Модель
選擇模型 | 选择模型 | Choose a model | モデルを選択 | 모델 선택 | Elegir modelo | Choisir un modèle | Modell wählen | Escolher modelo | Scegli un modello | Выбрать модель
自訂 | 自定义 | Custom | カスタム | 사용자 지정 | Personalizado | Personnalisé | Benutzerdefiniert | Personalizado | Personalizzato | Свой
API 金鑰 | API 密钥 | API key | API キー | API 키 | Clave de API | Clé API | API-Schlüssel | Chave de API | Chiave API | Ключ API
API 網址（OpenAI 相容，結尾到 /v1） | API 地址（OpenAI 兼容，以 /v1 结尾） | API URL (OpenAI-compatible, ending in /v1) | API の URL（OpenAI 互換、/v1 まで） | API 주소(OpenAI 호환, /v1까지) | URL de la API (compatible con OpenAI, termina en /v1) | URL de l'API (compatible OpenAI, se terminant par /v1) | API-URL (OpenAI-kompatibel, endet auf /v1) | URL da API (compatível com OpenAI, termina em /v1) | URL dell'API (compatibile con OpenAI, termina con /v1) | Адрес API (совместимый с OpenAI, заканчивается на /v1)
先貼上 API 金鑰 | 先粘贴 API 密钥 | Paste an API key first | 先に API キーを貼り付けてください | 먼저 API 키를 붙여 넣으세요 | Pega primero una clave de API | Collez d'abord une clé API | Zuerst einen API-Schlüssel einfügen | Cole primeiro uma chave de API | Incolla prima una chiave API | Сначала вставьте ключ API
先填 API 網址 | 先填写 API 地址 | Enter the API URL first | 先に API の URL を入力してください | 먼저 API 주소를 입력하세요 | Introduce primero la URL de la API | Saisissez d'abord l'URL de l'API | Zuerst die API-URL eingeben | Introduza primeiro o URL da API | Inserisci prima l'URL dell'API | Сначала укажите адрес API
先選模型 | 先选择模型 | Choose a model first | 先にモデルを選んでください | 먼저 모델을 선택하세요 | Elige primero un modelo | Choisissez d'abord un modèle | Zuerst ein Modell wählen | Escolha primeiro um modelo | Scegli prima un modello | Сначала выберите модель
先到 AI 分頁設定好 AI 服務 | 先到 AI 分页设置好 AI 服务 | Set up an AI service on the AI tab first | 先に AI タブで AI サービスを設定してください | 먼저 AI 탭에서 AI 서비스를 설정하세요 | Configura primero un servicio de IA en la pestaña IA | Configurez d'abord un service d'IA dans l'onglet IA | Zuerst im KI-Tab einen KI-Dienst einrichten | Configure primeiro um serviço de IA no separador IA | Configura prima un servizio IA nella scheda IA | Сначала настройте сервис ИИ на вкладке ИИ
找不到這個模型 | 找不到这个模型 | Model not found | モデルが見つかりません | 모델을 찾을 수 없습니다 | Modelo no encontrado | Modèle introuvable | Modell nicht gefunden | Modelo não encontrado | Modello non trovato | Модель не найдена
找不到可用的模型 | 找不到可用的模型 | No models available | 利用できるモデルがありません | 사용할 수 있는 모델이 없습니다 | No hay modelos disponibles | Aucun modèle disponible | Keine Modelle verfügbar | Nenhum modelo disponível | Nessun modello disponibile | Нет доступных моделей
{0} API 錯誤 | {0} API 错误 | {0} API error | {0} API エラー | {0} API 오류 | Error de la API de {0} | Erreur de l'API {0} | {0}-API-Fehler | Erro da API de {0} | Errore dell'API {0} | Ошибка API {0}
連不上 {0} | 连不上 {0} | Cannot reach {0} | {0} に接続できません | {0}에 연결할 수 없습니다 | No se puede conectar con {0} | Impossible de joindre {0} | {0} ist nicht erreichbar | Não é possível ligar a {0} | Impossibile raggiungere {0} | Не удаётся подключиться к {0}
這台電腦沒有安裝 {0} 的語音辨識，改用 {1} 聆聽 | 这台电脑没有安装 {0} 的语音识别，改用 {1} 聆听 | Speech recognition for {0} is not installed; listening in {1} instead | このパソコンには {0} の音声認識がないため、{1} で聞き取ります | 이 컴퓨터에는 {0} 음성 인식이 없어 {1}(으)로 듣습니다 | No hay reconocimiento de voz para {0}; se escuchará en {1} | La reconnaissance vocale pour {0} n'est pas installée ; écoute en {1} | Spracherkennung für {0} ist nicht installiert; es wird auf {1} zugehört | Não há reconhecimento de voz para {0}; a ouvir em {1} | Il riconoscimento vocale per {0} non è installato; ascolto in {1} | Распознавание речи для {0} не установлено; слушаю на {1}
下載語音模型中… {0} MB | 正在下载语音模型… {0} MB | Downloading the speech model… {0} MB | 音声モデルをダウンロード中… {0} MB | 음성 모델 다운로드 중… {0} MB | Descargando el modelo de voz… {0} MB | Téléchargement du modèle vocal… {0} Mo | Sprachmodell wird geladen… {0} MB | A descarregar o modelo de voz… {0} MB | Download del modello vocale… {0} MB | Загрузка речевой модели… {0} МБ
語音模型下載失敗 | 语音模型下载失败 | Could not download the speech model | 音声モデルをダウンロードできませんでした | 음성 모델을 내려받지 못했습니다 | No se pudo descargar el modelo de voz | Échec du téléchargement du modèle vocal | Sprachmodell konnte nicht geladen werden | Não foi possível descarregar o modelo de voz | Impossibile scaricare il modello vocale | Не удалось загрузить речевую модель
語音辨識用 Windows 的聽寫 | 语音识别使用 Windows 的听写 | Use Windows dictation for speech recognition | 音声認識に Windows の音声入力を使う | 음성 인식에 Windows 받아쓰기 사용 | Usar el dictado de Windows para el reconocimiento de voz | Utiliser la dictée de Windows pour la reconnaissance vocale | Windows-Diktat für die Spracherkennung verwenden | Usar o ditado do Windows para o reconhecimento de voz | Usa la dettatura di Windows per il riconoscimento vocale | Диктовка Windows для распознавания речи
現在播的是哪一首？ | 现在播的是哪一首？ | What's playing? | 今かかっている曲は？ | 지금 나오는 곡은? | ¿Qué está sonando? | Qu'est-ce qui passe ? | Was läuft gerade? | O que está a tocar? | Cosa sta suonando? | Что сейчас играет?
這句歌詞是什麼意思？ | 这句歌词是什么意思？ | What does this line mean? | この歌詞の意味は？ | 이 가사는 무슨 뜻이야? | ¿Qué significa este verso? | Que veut dire cette phrase ? | Was bedeutet diese Zeile? | O que significa este verso? | Cosa significa questo verso? | Что значит эта строка?
下一首 | 下一首 | Next song | 次の曲 | 다음 곡 | Siguiente canción | Titre suivant | Nächster Titel | Música seguinte | Brano successivo | Следующая песня
提醒我買牛奶 | 提醒我买牛奶 | Remind me to buy milk | 牛乳を買うのを忘れないように | 우유 사는 거 기억해 줘 | Recuérdame comprar leche | Rappelle-moi d'acheter du lait | Erinnere mich daran, Milch zu kaufen | Lembra-me de comprar leite | Ricordami di comprare il latte | Напомни купить молоко
專注 25 分鐘 | 专注 25 分钟 | Focus for 25 minutes | 25分集中する | 25분 집중 | Concentrarme 25 minutos | Concentration pendant 25 minutes | 25 Minuten Fokus | Foco durante 25 minutos | Concentrazione per 25 minuti | Сосредоточиться на 25 минут
辨識中… | 识别中… | Recognising… | 認識中… | 인식 중… | Reconociendo… | Reconnaissance… | Erkenne… | A reconhecer… | Riconoscimento… | Распознаю…
輸出：{0} | 输出：{0} | Output: {0} | 出力：{0} | 출력: {0} | Salida: {0} | Sortie : {0} | Ausgabe: {0} | Saída: {0} | Uscita: {0} | Выход: {0}
聆聽 | 聆听 | Listening | リスニング | 감상 | Escucha | Écoute | Hören | Audição | Ascolto | Прослушивание
收藏這句歌詞 | 收藏这句歌词 | Save this line | この歌詞を保存 | 이 가사 저장 | Guardar este verso | Enregistrer cette ligne | Diese Zeile speichern | Guardar este verso | Salva questo verso | Сохранить строку
已收藏這句歌詞 | 已收藏这句歌词 | Line saved | 保存済みの歌詞 | 저장한 가사 | Verso guardado | Ligne enregistrée | Zeile gespeichert | Verso guardado | Verso salvato | Строка сохранена
解釋這句歌詞 | 解释这句歌词 | Explain this line | この歌詞を解説 | 이 가사 설명 | Explicar este verso | Expliquer cette ligne | Diese Zeile erklären | Explicar este verso | Spiega questo verso | Объяснить строку
現在沒有歌詞 | 现在没有歌词 | No lyrics right now | 今は歌詞がありません | 지금은 가사가 없습니다 | Ahora no hay letra | Pas de paroles pour le moment | Gerade kein Songtext | Sem letra de momento | Nessun testo al momento | Сейчас нет текста
解釋這句歌詞的讀音、單字與文法：「{0}」（{1} — {2}） | 解释这句歌词的读音、单词与语法：“{0}”（{1} — {2}） | Explain the pronunciation, vocabulary and grammar of this lyric: "{0}" ({1} — {2}) | この歌詞の読み方・単語・文法を解説して：「{0}」（{1} — {2}） | 이 가사의 발음, 단어, 문법을 설명해 줘: "{0}" ({1} — {2}) | Explica la pronunciación, el vocabulario y la gramática de este verso: «{0}» ({1} — {2}) | Explique la prononciation, le vocabulaire et la grammaire de ces paroles : « {0} » ({1} — {2}) | Erkläre Aussprache, Wortschatz und Grammatik dieser Zeile: „{0}“ ({1} — {2}) | Explica a pronúncia, o vocabulário e a gramática deste verso: «{0}» ({1} — {2}) | Spiega pronuncia, vocaboli e grammatica di questo verso: «{0}» ({1} — {2}) | Объясни произношение, слова и грамматику этой строки: «{0}» ({1} — {2})
桌面歌詞 | 桌面歌词 | Desktop lyrics | デスクトップ歌詞 | 바탕화면 가사 | Letra en el escritorio | Paroles sur le bureau | Desktop-Songtext | Letra no ambiente de trabalho | Testo sul desktop | Текст на рабочем столе
鎖定桌面歌詞 | 锁定桌面歌词 | Lock desktop lyrics | デスクトップ歌詞をロック | 바탕화면 가사 잠금 | Bloquear la letra del escritorio | Verrouiller les paroles du bureau | Desktop-Songtext fixieren | Bloquear a letra no ambiente de trabalho | Blocca il testo sul desktop | Закрепить текст на рабочем столе
要移動桌面歌詞，先在右鍵選單取消鎖定 | 要移动桌面歌词，先在右键菜单取消锁定 | To move the desktop lyrics, unlock them from the right-click menu first | デスクトップ歌詞を動かすには、右クリックメニューでロックを解除してください | 바탕화면 가사를 옮기려면 먼저 오른쪽 클릭 메뉴에서 잠금을 해제하세요 | Para mover la letra, desbloquéala primero en el menú del clic derecho | Pour déplacer les paroles, déverrouillez-les d'abord dans le menu contextuel | Zum Verschieben zuerst im Kontextmenü die Fixierung lösen | Para mover a letra, desbloqueie-a primeiro no menu do botão direito | Per spostare il testo, sbloccalo prima dal menu del tasto destro | Чтобы переместить текст, сначала открепите его в контекстном меню
接下來播放 | 接下来播放 | Up next | 次に再生 | 다음 재생 | A continuación | À suivre | Als Nächstes | A seguir | In coda | Далее
重新啟動後生效 | 重启后生效 | Takes effect after a restart | 再起動後に反映されます | 다시 시작하면 적용됩니다 | Se aplica al reiniciar | Prend effet après redémarrage | Wird nach einem Neustart wirksam | Aplica-se depois de reiniciar | Effettivo dopo il riavvio | Вступит в силу после перезапуска
自己的角色 | 自己的角色 | My character | 自分のキャラクター | 내 캐릭터 | Mi personaje | Mon personnage | Eigene Figur | A minha personagem | Il mio personaggio | Свой персонаж
選圖片 | 选图片 | Choose image | 画像を選ぶ | 이미지 선택 | Elegir imagen | Choisir une image | Bild wählen | Escolher imagem | Scegli immagine | Выбрать картинку
自己的圖片 | 自己的图片 | My image | 自分の画像 | 내 이미지 | Mi imagen | Mon image | Eigenes Bild | A minha imagem | La mia immagine | Своя картинка
用 Roon 的話，到 Roon「設定 → 擴充功能」啟用 Shima | 用 Roon 的话，到 Roon“设置 → 扩展”启用 Shima | Using Roon? Enable Shima in Roon under Settings → Extensions | Roon を使う場合は、Roon の「設定 → 拡張機能」で Shima を有効にしてください | Roon을 쓴다면 Roon의 “설정 → 확장”에서 Shima를 켜세요 | Si usas Roon, activa Shima en Roon, en «Ajustes → Extensiones» | Avec Roon, activez Shima dans Roon, sous « Paramètres → Extensions » | Mit Roon: Shima in Roon unter „Einstellungen → Erweiterungen“ aktivieren | Se usa o Roon, ative o Shima no Roon em «Definições → Extensões» | Se usi Roon, attiva Shima in Roon da «Impostazioni → Estensioni» | Если пользуетесь Roon, включите Shima в Roon: «Настройки → Расширения»
在島上按右鍵：換角色、校正歌詞、桌面歌詞 | 在岛上点右键：换角色、校正歌词、桌面歌词 | Right-click the island: change character, adjust lyrics, desktop lyrics | 島を右クリック：キャラクター変更・歌詞の調整・デスクトップ歌詞 | 섬을 오른쪽 클릭: 캐릭터 변경, 가사 조정, 바탕화면 가사 | Clic derecho en la isla: cambiar de personaje, ajustar la letra, letra en el escritorio | Clic droit sur l'île : changer de personnage, caler les paroles, paroles sur le bureau | Rechtsklick auf die Insel: Figur wechseln, Songtext anpassen, Desktop-Songtext | Clique direito na ilha: mudar de personagem, acertar a letra, letra no ambiente de trabalho | Clic destro sull'isola: cambia personaggio, regola il testo, testo sul desktop | Правый клик по острову: смена персонажа, подстройка текста, текст на рабочем столе
{0} 展開，{1} 問 AI | {0} 展开，{1} 问 AI | {0} to expand, {1} to ask the AI | {0} で展開、{1} で AI に質問 | {0}: 펼치기, {1}: AI에게 질문 | {0} para expandir, {1} para preguntar a la IA | {0} pour déployer, {1} pour interroger l'IA | {0} zum Aufklappen, {1} fragt die KI | {0} para expandir, {1} para perguntar à IA | {0} per espandere, {1} per chiedere all'IA | {0} — раскрыть, {1} — спросить ИИ
滑鼠移到島上就會展開 | 鼠标移到岛上就会展开 | Hover over the island to expand it | 島にマウスを乗せると展開します | 섬에 마우스를 올리면 펼쳐집니다 | Pasa el ratón por la isla para expandirla | Survolez l'île pour la déployer | Mit der Maus auf die Insel zeigen, um sie aufzuklappen | Passe o rato sobre a ilha para a expandir | Passa il mouse sull'isola per espanderla | Наведите курсор на остров, чтобы раскрыть его
知道了 | 知道了 | Got it | 了解 | 알겠어요 | Entendido | J'ai compris | Verstanden | Entendido | Ho capito | Понятно
週 | 周 | Week | 週 | 주 | Semana | Semaine | Woche | Semana | Settimana | Неделя
月 | 月 | Month | 月 | 월 | Mes | Mois | Monat | Mês | Mese | Месяц
年 | 年 | Year | 年 | 년 | Año | Année | Jahr | Ano | Anno | Год
全部 | 全部 | All | すべて | 전체 | Todo | Tout | Alle | Tudo | Tutto | Всё
連續 {0} 天 | 连续 {0} 天 | {0}-day streak | {0}日連続 | {0}일 연속 | Racha de {0} días | {0} jours d'affilée | {0} Tage in Folge | {0} dias seguidos | {0} giorni di fila | {0} дн. подряд
{0} 次 | {0} 次 | {0} plays | {0}回 | {0}회 | {0} reproducciones | {0} écoutes | {0} Wiedergaben | {0} reproduções | {0} ascolti | Прослушиваний: {0}
{0} 分 | {0} 分 | {0} min | {0}分 | {0}분 | {0} min | {0} min | {0} Min. | {0} min | {0} min | {0} мин
{0} 小時 | {0} 小时 | {0} h | {0}時間 | {0}시간 | {0} h | {0} h | {0} Std. | {0} h | {0} h | {0} ч
{0} 小時 {1} 分 | {0} 小时 {1} 分 | {0} h {1} min | {0}時間{1}分 | {0}시간 {1}분 | {0} h {1} min | {0} h {1} min | {0} Std. {1} Min. | {0} h {1} min | {0} h {1} min | {0} ч {1} мин
以後都允許 {0} | 以后都允许 {0} | Always allow {0} | {0} を常に許可 | {0} 항상 허용 | Permitir siempre {0} | Toujours autoriser {0} | {0} immer erlauben | Permitir sempre {0} | Consenti sempre {0} | Всегда разрешать {0}
已自動允許 {0} | 已自动允许 {0} | Automatically allowed {0} | {0} を自動で許可しました | {0} 자동 허용됨 | {0} permitido automáticamente | {0} autorisé automatiquement | {0} automatisch erlaubt | {0} permitido automaticamente | {0} consentito automaticamente | {0} разрешено автоматически
更新連接 | 更新连接 | Update connection | 接続を更新 | 연결 업데이트 | Actualizar conexión | Mettre à jour la connexion | Verbindung aktualisieren | Atualizar ligação | Aggiorna collegamento | Обновить подключение
分享聆聽回顧 | 分享聆听回顾 | Share listening recap | 聴いた曲のまとめを共有 | 청취 요약 공유 | Compartir resumen de escucha | Partager le récap d'écoute | Hörrückblick teilen | Partilhar resumo de audição | Condividi il riepilogo d'ascolto | Поделиться итогами прослушивания
聆聽回顧已複製 | 聆听回顾已复制 | Recap copied | まとめをコピーしました | 요약을 복사했어요 | Resumen copiado | Récap copié | Rückblick kopiert | Resumo copiado | Riepilogo copiato | Итоги скопированы
這個期間還沒有聽歌 | 这个期间还没有听歌 | Nothing played in this period | この期間はまだ再生していません | 이 기간에는 들은 곡이 없어요 | Nada reproducido en este periodo | Rien écouté sur cette période | In diesem Zeitraum nichts gehört | Nada reproduzido neste período | Nulla riprodotto in questo periodo | За этот период ничего не слушали
返回 | 返回 | Back | 戻る | 뒤로 | Atrás | Retour | Zurück | Voltar | Indietro | Назад
搜尋 | 搜索 | Search | 検索 | 검색 | Buscar | Rechercher | Suchen | Pesquisar | Cerca | Поиск
隨機播放 | 随机播放 | Shuffle | シャッフル | 셔플 | Aleatorio | Lecture aléatoire | Zufallswiedergabe | Aleatório | Riproduzione casuale | Перемешать
循環播放 | 循环播放 | Repeat | リピート | 반복 | Repetir | Répéter | Wiederholen | Repetir | Ripeti | Повтор
播放 | 播放 | Play | 再生 | 재생 | Reproducir | Lecture | Wiedergabe | Reproduzir | Riproduci | Воспроизвести
今天 {0} | 今天 {0} | Today {0} | 今日 {0} | 오늘 {0} | Hoy {0} | Aujourd'hui {0} | Heute {0} | Hoje {0} | Oggi {0} | Сегодня {0}
時間到了 | 时间到了 | Time's up | 時間です | 시간이 됐어요 | Es la hora | C'est l'heure | Es ist so weit | Chegou a hora | È ora | Время пришло
喚醒詞無法啟動 | 唤醒词无法启动 | The wake word could not start | ウェイクワードを開始できません | 호출어를 시작할 수 없습니다 | No se pudo iniciar la palabra de activación | Impossible de lancer le mot d'activation | Aktivierungswort konnte nicht starten | Não foi possível iniciar a palavra de ativação | Impossibile avviare la parola di attivazione | Не удалось запустить слово активации
麥克風中斷了 | 麦克风断开了 | Microphone disconnected | マイクが切断されました | 마이크 연결이 끊겼습니다 | Micrófono desconectado | Micro déconnecté | Mikrofon getrennt | Microfone desligado | Microfono disconnesso | Микрофон отключился
外觀 | 外观 | Appearance | 外観 | 모양 | Apariencia | Apparence | Darstellung | Aspeto | Aspetto | Внешний вид
底色 | 底色 | Background | 背景 | 배경 | Fondo | Fond | Hintergrund | Fundo | Sfondo | Фон
不透明 | 不透明 | Opaque | 不透明 | 불투명 | Opaco | Opaque | Deckend | Opaco | Opaco | Непрозрачный
顏色 | 颜色 | Colour | 色 | 색 | Color | Couleur | Farbe | Cor | Colore | Цвет
跟著封面 | 跟随封面 | Match album art | ジャケットに合わせる | 앨범 커버에 맞추기 | Según la portada | Comme la pochette | Wie das Cover | Conforme a capa | Come la copertina | Как обложка
白 | 白 | White | 白 | 흰색 | Blanco | Blanc | Weiß | Branco | Bianco | Белый
橘 | 橙 | Orange | オレンジ | 주황 | Naranja | Orange | Orange | Laranja | Arancione | Оранжевый
歌詞 | 歌词 | Lyrics | 歌詞 | 가사 | Letras | Paroles | Songtexte | Letras | Testi | Тексты песен
全螢幕時換歌提示 | 全屏时提示换歌 | Show song changes in full screen | 全画面でも曲の切り替えを表示 | 전체 화면에서 곡 변경 알림 | Avisar del cambio de canción en pantalla completa | Signaler le changement de titre en plein écran | Titelwechsel im Vollbild anzeigen | Avisar mudança de música em ecrã inteiro | Avvisa del cambio brano a schermo intero | Показывать смену трека в полноэкранном режиме
記下聽了什麼 | 记录听了什么 | Keep listening history | 聴いた曲を記録 | 들은 곡 기록 | Registrar lo que escucho | Garder l'historique d'écoute | Gehörtes aufzeichnen | Registar o que ouço | Registra cosa ascolto | Запоминать, что я слушаю
Discord 顯示封面 | Discord 显示封面 | Show album art on Discord | Discord にジャケットを表示 | Discord에 앨범 커버 표시 | Mostrar la portada en Discord | Afficher la pochette sur Discord | Cover in Discord anzeigen | Mostrar a capa no Discord | Mostra la copertina su Discord | Показывать обложку в Discord
語音 | 语音 | Voice | 音声 | 음성 | Voz | Voix | Sprachsteuerung | Voz | Voce | Голос
辨識模型 | 识别模型 | Recognition model | 認識モデル | 인식 모델 | Modelo de reconocimiento | Modèle de reconnaissance | Erkennungsmodell | Modelo de reconhecimento | Modello di riconoscimento | Модель распознавания
聽的時候暫停音樂 | 听的时候暂停音乐 | Pause music while listening | 聞き取り中は音楽を一時停止 | 듣는 동안 음악 일시정지 | Pausar la música mientras escucha | Mettre la musique en pause pendant l'écoute | Musik beim Zuhören pausieren | Pausar a música enquanto ouve | Metti in pausa la musica durante l'ascolto | Ставить музыку на паузу, пока слушаю
複製 | 复制 | Copy | コピー | 복사 | Copiar | Copier | Kopieren | Copiar | Copia | Копировать
一直對話到再按一次 | 一直对话到再按一次 | Keep talking until pressed again | もう一度押すまで会話を続ける | 다시 누를 때까지 대화 계속 | Seguir conversando hasta volver a pulsar | Continuer la conversation jusqu'au prochain appui | Gespräch fortsetzen, bis erneut gedrückt wird | Continuar a conversa até premir de novo | Continua a conversare finché non premi di nuovo | Продолжать разговор до повторного нажатия
說「Shima」就開始聽 | 说“Shima”就开始听 | Say "Shima" to start listening | 「Shima」と言うと聞き取りを開始 | "Shima"라고 말하면 듣기 시작 | Empezar a escuchar al decir «Shima» | Lancer l'écoute en disant « Shima » | „Shima“ sagen, um das Zuhören zu starten | Começar a ouvir ao dizer «Shima» | Avvia l'ascolto dicendo «Shima» | Начинать слушать по слову «Shima»
番茄鐘 | 番茄钟 | Pomodoro | ポモドーロ | 뽀모도로 | Pomodoro | Pomodoro | Pomodoro | Pomodoro | Pomodoro | Помодоро
"""

root = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
rows = [[c.strip() for c in line.split(" | ")] for line in ROWS.strip().split("\n")]
problems = 0
for r in rows:
    if len(r) != len(LANGS) or any(not c for c in r):
        print("欄位數不對:", r[0], len(r)); problems += 1; continue
    marks = sorted(re.findall(r"\{\d+\}", r[0]))
    for code, text in zip(LANGS[1:], r[1:]):
        if sorted(re.findall(r"\{\d+\}", text)) != marks:
            print(f"參數不一致 [{code}]:", r[0]); problems += 1

# 程式裡用到的文字都要在表裡
used, cjk = [], re.compile(r"[一-鿿]")
def add(k):
    if k and cjk.search(k) and k not in used: used.append(k)
src = os.path.join(root, "src", "Shima")
for f in glob.glob(os.path.join(src, "*.xaml")):
    for m in re.finditer(r'(?:Text|Content|ToolTip|Header)="([^"]*)"', open(f, encoding="utf-8").read()): add(m.group(1))
for f in glob.glob(os.path.join(src, "**", "*.cs"), recursive=True):
    if os.sep + "obj" + os.sep in f or os.sep + "bin" + os.sep in f: continue
    s = open(f, encoding="utf-8").read()
    for m in re.finditer(r'L\.[TF]\((?:[^"()]*\?\s*)?"((?:[^"\\]|\\.)*)"(?:\s*:\s*"((?:[^"\\]|\\.)*)")?', s): add(m.group(1)); add(m.group(2))
    if f.endswith("Mascot.cs"):
        for m in re.finditer(r'\("\w+", "([^"]+)"\)', s): add(m.group(1))
for k in ["中文翻譯", "羅馬拼音", "不顯示"]: add(k)
agents = open(os.path.join(src, "IslandWindow.Agents.cs"), encoding="utf-8").read()
for m in re.finditer(r'Examples = \[(.*?)\];', agents, flags=re.S):
    for k in re.findall(r'"([^"]+)"', m.group(1)): add(k)
have = {r[0] for r in rows}
for k in used:
    if k not in have: print("沒有翻譯:", k); problems += 1
for k in have - set(used): print("表裡多的（程式沒用到）:", k)

out = os.path.join(src, "Assets", "strings.tsv")
with open(out, "w", encoding="utf-8", newline="\n") as f:
    f.write("\t".join(LANGS) + "\n")
    for r in rows: f.write("\t".join(r) + "\n")
print(f"{len(rows)} 筆 × {len(LANGS) - 1} 種語言，問題 {problems} 個")
sys.exit(1 if problems else 0)
