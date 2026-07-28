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

## Specifying the fonts

Four more custom properties on the same selector swap the font tokens.

| Property | Applies to | Default |
| --- | --- | --- |
| `--tq-font-ui` | general text (labels, buttons, dropdown rows, tabs) | the panel's own font |
| `--tq-font-numeric` | digits (number fields, timecode, rulers) | bundled Geist |
| `--tq-font-heading` | headings and dialog titles | bundled Geist SemiBold |
| `--tq-font-code` | monospace text (HEX fields) | bundled Geist Mono |

The value is an asset reference to a `Font` (`.ttf` / `.otf`) or a TextCore
`FontAsset`. All of these forms work:

```css
.demo-root {
    /* the form UI Builder writes */
    --tq-font-ui: url("project://database/Assets/Fonts/MyFont.ttf");

    /* project-root absolute */
    --tq-font-numeric: url("/Assets/Fonts/MyMono.ttf");

    /* relative to this .uss file */
    --tq-font-heading: url("MyFont-SemiBold.ttf");

    /* a font living in a package */
    --tq-font-code: url("project://database/Packages/my.package/Fonts/MyMono.ttf");
}
```

- `resource("...")` does **not** resolve for these properties — use `url()`.
- Unspecified tokens keep their defaults; the C#-assignment-wins rule above
  applies to the fonts too.

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
