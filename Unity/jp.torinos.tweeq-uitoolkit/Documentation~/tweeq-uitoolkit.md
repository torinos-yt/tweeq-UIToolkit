# Tweeq UI Toolkit

Parameter-tuning GUI widgets for Unity UI Toolkit — a port of
[Tweeq](https://github.com/baku89/tweeq) by Baku Hashimoto.

Widgets keep the original's interaction design: drag-to-tweak gestures with
continuous sensitivity control, snapping, modifier keys, and cancelable edit
sessions. Everything is a plain `VisualElement` — no uGUI, no prefabs.

- Repository & full documentation: <https://github.com/torinos-yt/tweeq-UIToolkit>
- Architecture, component coverage, and intentional differences from the
  original are documented in the repository's `docs/` folder.

## Requirements

- Unity 6000.3 or newer, UI Toolkit (runtime or editor panels).
- Optional: [ZString](https://github.com/Cysharp/ZString) — detected
  automatically, reduces per-gesture string allocations.

## Getting started (C#)

```csharp
using Tweeq.UIToolkit;
using UnityEngine;
using UnityEngine.UIElements;

public class Example : MonoBehaviour
{
    [SerializeField] UIDocument _document;

    void OnEnable()
    {
        var root = new TweeqRoot();          // generates and distributes the theme

        var speed = new NumberInput { Min = 0, Max = 2, Step = 0.01, Bar = true };
        speed.RegisterValueChangedCallback(evt => Debug.Log($"speed: {evt.newValue}"));

        root.Add(speed);
        _document.rootVisualElement.Add(root);
    }
}
```

Values follow the standard UI Toolkit contract:
`INotifyValueChanged<T>` / `ChangeEvent<T>` for live changes plus a
`Confirmed` event that fires once per edit session (drag release, Enter,
option pick).

## Getting started (UXML)

All widgets are `[UxmlElement]`s and can be authored entirely in
UXML/UI Builder. Import the **UXML Demo** sample from the Package Manager for
a complete panel with zero C#.

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements" xmlns:tq="Tweeq.UIToolkit">
  <tq:TweeqRoot>
    <tq:ParameterGrid>
      <tq:Parameter label="Amount">
        <tq:NumberInput value="0.5" min="0" max="1" step="0.01" />
      </tq:Parameter>
      <tq:Parameter label="Angle">
        <tq:RotaryInput value="45" snap="15" />
      </tq:Parameter>
    </tq:ParameterGrid>
  </tq:TweeqRoot>
</ui:UXML>
```

## Theming

`TweeqRoot` owns a `TweeqTheme` and distributes it to every themed descendant.
Set it from code (`root.Theme = TweeqTheme.Light();`) or seed it from USS
custom properties — the full palette is generated from the seeds through the
Radix-scale pipeline ported to pure C#:

```css
.my-panel {
    --tq-accent: #4a76ff;
    --tq-gray: #8b8d98;
    --tq-background: #111111;
    --tq-color-mode: dark;   /* or light */
}
```

Nested `TweeqRoot`s form independent theme boundaries.

## Fonts

The theme carries four font tokens. Every widget reads one of them, so swapping a
token re-fonts the whole UI.

| Token | Applies to | Default |
| --- | --- | --- |
| `FontUi` | General text: button/checkbox/switch/radio labels, parameter labels, dropdown field & option rows, tab headings, tooltips, `StringInput`, number prefixes/suffixes/axis labels | unset — the panel's own font, the Unity analogue of `system-ui` |
| `FontNumeric` | Digits: number fields, timecode digits, ruler ticks, scrub overlays | bundled Geist |
| `FontHeading` | `ParameterHeading`, modal dialog titles | bundled Geist SemiBold |
| `FontCode` | Monospace text: HEX fields, the color scrub overlay | bundled Geist Mono |

From C#, assign the fields and hand the theme back to the root — the tokens are
read while distributing, so a mutation made after distribution needs a
re-assignment (or `Redistribute()`) to take effect:

```csharp
TweeqTheme theme = TweeqTheme.Dark();
theme.FontUi = FontDefinition.FromFont(myFont);
theme.FontCode = FontDefinition.FromSDFFont(myMonoFontAsset);

root.Theme = theme;   // distribute; re-assign (or call root.Redistribute()) after later edits
```

Setting a token back to `default` restores the panel's font rather than leaving
the previous override behind.

From USS, seed the same four tokens on the element carrying `TweeqRoot`:

```css
.my-panel {
    --tq-font-ui: url("project://database/Assets/Fonts/MyFont.ttf");
    --tq-font-numeric: url("/Assets/Fonts/MyMono.ttf");
    --tq-font-heading: url("MyFont-SemiBold.ttf");
    --tq-font-code: url("project://database/Packages/my.package/Fonts/MyMono.ttf");
}
```

Both a legacy `Font` and a TextCore `FontAsset` are accepted. Asset references
must go through `url()`; `resource()` does not resolve for these properties.
Unspecified tokens keep their defaults, and — as with the color seeds — assigning
`root.Theme` from C# wins over USS.

Geist is bundled under `Resources/Tweeq`, but nothing forces you to ship it: point
all four tokens at your own fonts and the bundled files go unused (see
`Runtime/Fonts/Licenses.md` for the OFL terms that apply if you do ship it).

## Gesture & keyboard reference

| Input | Effect |
| --- | --- |
| Drag right/up | Increase value |
| Vertical drag (number fields) | Continuously adjusts scrub sensitivity |
| `Shift` | Fast (×10) |
| `Alt` | Fine (×0.1) |
| `Q` (hold) | Snap |
| `A` / `R` | Rotary absolute / relative mode |
| `Escape` | Cancel the edit session, restore the starting value |
| `Enter` | Commit text editing |
| Arrow keys | Step the focused field |
| `Tab` | Focus next field and enter text editing with select-all |

## Persistence

`TweeqTabs` (active tab) and `ParameterGroup` (expanded state) remember their
state under `tweeq.*` keys — in memory only by default: the state survives for
the current session and nothing is written to disk. To persist across runs,
opt in:

```csharp
TweeqTabs.Storage = TweeqPlayerPrefsStorage.Instance;        // or your own ITweeqStorage
ParameterGroup.Storage = TweeqPlayerPrefsStorage.Instance;
```

## Assemblies

| asmdef | Contents |
| --- | --- |
| `Tweeq.Core` | Pure C# logic (gesture math, validation, color science). No UnityEngine reference. |
| `Tweeq.UIToolkit` | The widgets, theming, overlays. |
| `Tweeq.UIToolkit.TestSupport` | Test-only helpers, notably `TweeqRuntimeTestPanel`. |

### Testing your own widgets

Widgets built on `TweeqInputBoxStyles`, `TweeqFocusRing` and
`TweeqScrubManipulator` need a live panel before synthesized pointer and focus
events reach them. `Tweeq.UIToolkit.TestSupport` ships
`TweeqRuntimeTestPanel.Create()`, a disposable `UIDocument` pinned to
`ConstantPixelSize` at scale 1 so that one synthetic pixel stays one pixel —
drag thresholds mean nothing otherwise. The assembly lives under the package's
`Tests` folder, so Unity only compiles it for projects that opt in: add the
package to `testables` in `Packages/manifest.json`, then reference
`Tweeq.UIToolkit.TestSupport` from your own test asmdef.

```json
{
  "testables": [
    "jp.torinos.tweeq-uitoolkit"
  ]
}
```

## Licenses

MIT. Bundled third-party assets — the Geist font family (SIL OFL 1.1) and
Radix Colors scale data (MIT) — are detailed in `Third Party Notices.md`.
