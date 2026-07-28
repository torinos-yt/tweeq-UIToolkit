# Architecture

This port does not translate Vue components one-to-one. The unit of porting is the
**interaction semantics** of tweeq — the gesture math, editing sessions, and visual
feedback that make the widgets feel the way they do — re-expressed in idiomatic
Unity UI Toolkit.

## Layers

```
Tweeq.Core        Pure C# logic: gesture math, clamp/quantize, edit sessions,
                  color science, fuzzy search. No UnityEngine reference
                  (asmdef: noEngineReferences). All values are double.
      ↓
Tweeq.UIToolkit   VisualElement implementations, theming, drag manipulators,
                  Painter2D rendering. Values become float only at the
                  rendering/API boundary.
```

- Package: `jp.torinos.tweeq-uitoolkit`
  - `Runtime/Core/` → asmdef `Tweeq.Core` (namespace `Tweeq.Core`)
  - `Runtime/UIToolkit/` → asmdef `Tweeq.UIToolkit` (namespace `Tweeq.UIToolkit`)
  - `Tests/Editor/` → EditMode behavior tests (820+ assertions/cases)

## The interaction contract

These rules come from the original tweeq and are treated as non-negotiable.
Every widget in this port obeys them:

- Dragging right/up increases the value.
- Vertical drag changes a number field's sensitivity *continuously*
  (`speed *= 0.98^dy`) — it is not a mode switch.
- Diagonal drags blend value-change and sensitivity-change with a
  `smoothstep(0.4, 0.6, |direction EMA x|)` weight, so the two never fight.
- `Shift` = fast (×10), `Alt` = fine (×0.1), `Q` = snap,
  `A` / `R` = absolute/relative mode on the rotary knob.
- Every gesture has explicit Begin / Update / Commit / Cancel boundaries.
  `Escape` cancels and restores the value at drag start.
- Values are computed as *drag-start value + accumulated delta* (never
  incremented per frame), so there is no floating-point drift and cancel is trivial.
- Snapping applies to the **output only**, never to the raw accumulated value,
  so releasing snap returns smoothly to the true position.
- Quantization is origin-based: `round((v - origin) / step) * step + origin`.

## Unity-specific decisions

- **No Pointer Lock.** The web version hides and warps the cursor for infinite
  scrubbing. Unity has no equivalent, so widgets degrade to ordinary drags
  (the rotary is screen-angle based and is unaffected).
- **Input handling** uses `PointerEvent.modifiers` plus `KeyDown`/`KeyUp` on
  focusable elements. No dependency on the Input System package.
- **Rendering** uses `Painter2D` (`generateVisualContent`) in place of SVG.
- **Overlays** (popovers, tooltips, tweak feedback, modals) share a single
  overlay layer attached to the panel root (`TweeqOverlayLayer`), designed once
  and reused by every floating element.
- **Public value API** follows UI Toolkit convention:
  `INotifyValueChanged<T>` + `ChangeEvent<T>`, with `SetValueWithoutNotify`.
  Widgets additionally expose a `Confirmed` notion — one commit per edit
  session, not per change.
- **Zero allocation during gestures** is a standing requirement. String
  formatting is centralized in `TweeqFormat`; if
  [ZString](https://github.com/Cysharp/ZString) is present in the project it is
  picked up automatically via asmdef version defines (GUID reference — fully
  optional), cutting per-move garbage further.

## Theming

The original tweeq derives its palette from [Radix Colors](https://www.radix-ui.com/colors)
custom scales. This port reimplements that pipeline in pure C# (`Tweeq.Core`):

- `TweeqOklch` — sRGB / Display-P3 / OKLab / CIE Lab (D50) conversions, APCA
  contrast, and CSS gamut mapping.
- `RadixPaletteData` — the 29 reference scales × 12 steps × light/dark,
  pre-converted to OKLCH (MIT, from radix-ui/colors).
- `RadixThemeEngine` — generates a full accent/gray scale from arbitrary seed
  colors. Output was verified against a JavaScript oracle (backed by colorjs.io)
  with 232k assertions at 99.8 % byte-identical.

`TweeqTheme` is the flat set of resolved colors and metrics the widgets consume
(`Light()` / `Dark()` presets, accent-seeded variants). `TweeqRoot` reads USS
custom properties (`--tq-accent`, `--tq-gray`, `--tq-background`,
`--tq-color-mode`) and distributes the generated theme to every themed child,
so UXML/USS-only usage works without writing C#. Nested `TweeqRoot`s form
theme boundaries.

## Fonts

[Geist](https://vercel.com/font) v1.7.2 (static TTF, OFL) is bundled and applied
to numeric fields, headings, and code/hex text. The general UI font is left
unset on purpose, falling back to the panel's default — the Unity analogue of
the original's `system-ui` stack.
