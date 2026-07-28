# UXML Demo

A sample that lays out tweeq widgets in UXML only, without writing any C#.
`TweeqRoot` generates the theme from USS custom properties and distributes it
to every widget below it.

## How to use

1. Select this package in the Package Manager and **Import** the **UXML Demo**
   sample (it lands in `Assets/Samples/tweeq UIToolkit/<version>/UXML Demo/`).
2. Create a PanelSettings asset via
   `Assets > Create > UI Toolkit > Panel Settings Asset`.
3. Add a **UI Document** component to an empty GameObject and assign
   - `Panel Settings` → the asset from step 2
   - `Source Asset` → `TweeqUxmlDemo.uxml`
4. Enter Play mode — the widgets appear fully themed.

The same UXML can be opened and edited in UI Builder.

## Specifying the theme

The four custom properties on `.demo-root` in `TweeqUxmlDemo.uss` are the input.

| Property | Type | Default |
| --- | --- | --- |
| `--tq-color-mode` | string (`"dark"` / `"light"`, quotes required) | `"dark"` |
| `--tq-accent` | color | `#0000ff` |
| `--tq-gray` | color | `#8b8d98` |
| `--tq-background` | color | mode default (dark: `#111111` / light: `#ffffff`) |

- Unspecified tokens fall back to their defaults. With no USS at all, the
  result equals `TweeqTheme.Dark()`.
- Assigning `root.Theme = ...` from C# takes precedence; the USS seeds are
  ignored from then on.
- `paint-background="false"` on `TweeqRoot` stops it from painting its own
  background.
- Call `root.Redistribute()` after adding children dynamically (it runs
  automatically on panel attach and USS resolution).

```csharp
// Swapping the theme from code
TweeqRoot root = document.rootVisualElement.Q<TweeqRoot>();
root.Theme = TweeqTheme.Light().WithAccent(new Color32(0xFF, 0x66, 0x00, 0xFF));
```

## Notes

- UXML tag/attribute names follow each component's `[UxmlElement]` /
  `[UxmlAttribute]` implementation. Attribute names are the kebab-case of the
  property name (`Min` → `min`, `LeftLabel` → `left-label`) unless an explicit
  `[UxmlAttribute("...")]` alias exists (`ButtonInput.Label` → `text`,
  `NumberInput.Bar` → `bar-visible`, `ParameterGroup.Label` → `heading-text`).
- The generic `DropdownInput<T>` / `ShuffleInput<T>` cannot be used directly
  from UXML — use the string-specialized wrappers `StringDropdownInput` /
  `StringShuffleInput`.
- Unknown attributes produce warnings at UXML import time; remove the line or
  fix the attribute name.
- `Parameter` / `ParameterGroup` route children into `InputContainer` /
  `Content` in C#; whether UXML children land there depends on each
  container's `contentContainer` implementation.
