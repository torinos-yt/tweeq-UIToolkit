# Bundled fonts

## Geist / Geist Mono

| Item | Value |
| --- | --- |
| Upstream | https://github.com/vercel/geist-font |
| Release | v1.7.2 (`geist-font-v1.7.2.zip`) |
| Download URL | https://github.com/vercel/geist-font/releases/download/v1.7.2/geist-font-v1.7.2.zip |
| Author | The Geist Project Authors (Vercel) — Copyright 2024 |
| License | SIL Open Font License, Version 1.1 (full text: `OFL.txt`) |
| Modifications | None (static TTFs from the release zip, verbatim) |

Bundled files (original path in the zip → location here):

| File | Original path | Size |
| --- | --- | --- |
| `Resources/Tweeq/Geist-Regular.ttf` | `geist-font/Geist/ttf/Geist-Regular.ttf` | 126,048 B |
| `Resources/Tweeq/Geist-SemiBold.ttf` | `geist-font/Geist/ttf/Geist-SemiBold.ttf` | 127,872 B |
| `Resources/Tweeq/GeistMono-Regular.ttf` | `geist-font/GeistMono/ttf/GeistMono-Regular.ttf` | 148,516 B |
| `OFL.txt` | `geist-font/OFL.txt` | 4,383 B |

TTF total: 402,436 B (~393 KB).

The variable fonts (`Geist[wght].ttf` etc.) are not used because Unity's
legacy `Font` importer cannot interpret the weight axis. The static weights
are the minimum set the original tweeq tokens need: body Regular, heading
SemiBold, mono Regular.

## Build size

Assets under `Resources/` are included in **every build regardless of
references**. Projects that don't need Geist (the default Unity font is fine)
may delete the `Runtime/Fonts/Resources/` folder. `TweeqFonts` detects the
missing files and returns `default(FontDefinition)` (no font specified), so
nothing throws — text falls back to the default font of the USS /
PanelSettings.

Keeping a subset also works (e.g. keep only `GeistMono-Regular.ttf` if you
only want the mono font). Whether deleting or redistributing, keep `OFL.txt`.

## License compliance notes

- OFL-1.1 allows redistribution; derivatives must inherit the license and
  bundle its full text.
- When modifying or renaming the font files for distribution, mind the
  Reserved Font Name clause ("Geist" is not a reserved name, but check
  Vercel's trademark guidelines separately).
