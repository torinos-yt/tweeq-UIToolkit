# 同梱フォント

## Geist / Geist Mono

| 項目 | 内容 |
| --- | --- |
| 出典 | https://github.com/vercel/geist-font |
| リリース | v1.7.2 (`geist-font-v1.7.2.zip`) |
| ダウンロード URL | https://github.com/vercel/geist-font/releases/download/v1.7.2/geist-font-v1.7.2.zip |
| 作者 | The Geist Project Authors (Vercel) — Copyright 2024 |
| ライセンス | SIL Open Font License, Version 1.1（全文: `OFL.txt`） |
| 改変 | なし（リリース zip 内の静的 TTF をそのまま配置） |

同梱ファイル（zip 内の元パス → 配置先）:

| ファイル | 元パス | サイズ |
| --- | --- | --- |
| `Resources/Tweeq/Geist-Regular.ttf` | `geist-font/Geist/ttf/Geist-Regular.ttf` | 126,048 B |
| `Resources/Tweeq/Geist-SemiBold.ttf` | `geist-font/Geist/ttf/Geist-SemiBold.ttf` | 127,872 B |
| `Resources/Tweeq/GeistMono-Regular.ttf` | `geist-font/GeistMono/ttf/GeistMono-Regular.ttf` | 148,516 B |
| `OFL.txt` | `geist-font/OFL.txt` | 4,383 B |

TTF 合計 402,436 B（約 393 KB）。

バリアブルフォント（`Geist[wght].ttf` 等）は Unity のレガシー `Font`
インポータがウェイト軸を解釈できないため採用していない。静的ウェイトは
本家トークンに必要な最小構成（本文 Regular・見出し SemiBold・等幅 Mono Regular）だけ。

## ビルドサイズについて

`Resources/` 配下のアセットは**参照の有無に関係なく全ビルドに含まれる**。
Geist を使わない（Unity 既定フォントで十分な）プロジェクトでは、
`Runtime/Fonts/Resources/` フォルダを削除してよい。
`TweeqFonts` は欠落を検知して `default(FontDefinition)`（＝フォント未指定）を返すため、
削除しても例外は出ず、USS / PanelSettings の既定フォントへフォールバックする。

一部だけ残すこともできる（例: 等幅だけ欲しいなら `GeistMono-Regular.ttf` のみ残す）。
削除・再配布のいずれの場合も `OFL.txt` は残すこと。

## ライセンス遵守メモ

- OFL-1.1 は再配布可。派生物に同ライセンスの継承と本文同梱を要求する
- フォントファイル自体を改変・リネームして配布する場合、Reserved Font Name 条項に注意
  （"Geist" は予約名ではないが、Vercel の商標ガイドラインは別途確認すること）
