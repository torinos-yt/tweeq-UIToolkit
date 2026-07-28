# Component coverage

How the original Vue components map to this port. The original source is
preserved at the git tag [`vue-final`](https://github.com/torinos-yt/tweeq-UIToolkit/tree/vue-final).

## Ported

| Original (Vue) | This port (`Tweeq.UIToolkit`) | Notes |
| --- | --- | --- |
| `InputRotary` | `RotaryInput` | Relative/absolute modes, snap ring, multi-turn overlay |
| `InputNumber` | `NumberInput` | Bar, scrub, scale ticks, text editing. No expression input |
| `InputVec` | `Vec2Input` / `Vec3Input` / `Vec4Input`, `VecInput` | Typed variants for `Vector2/3/4`; `VecInput` (`float[]`) for arbitrary arity |
| `InputPosition` | `PositionInput` | |
| `InputSize` | `SizeInput` | Aspect-ratio link with snapshot-based scaling |
| `InputTranslate` | `TranslateInput` | Y axis inverted to match Unity (see [deviations](deviations.md)) |
| `InputAngle` | `AngleInput` | Rotary + number field composite |
| `InputString` | `StringInput` | |
| `InputColor` | `ColorInput` | Value type is `UnityEngine.Color`; channel scrub, HSV pad, presets |
| `InputCheckbox` | `CheckboxInput` | Shared swipe gesture with live preview |
| `InputSwitch` | `SwitchInput` | |
| `InputButton` | `ButtonInput` | Blink/Flash/Chevron/Subtle/Narrow styles |
| `InputButtonToggle` | `ButtonToggleInput` | |
| `InputRadio` | `RadioInput` | Sliding indicator, drag selection, arrow-key wrap |
| `InputDropdown` | `DropdownInput<T>`, `StringDropdownInput` | Fuzzy filtering included |
| `InputShuffle` | `ShuffleInput<T>`, `StringShuffleInput` | |
| `InputGroup` | `InputGroup` | Corner-radius fusion, stretch layout |
| `ParameterGrid` | `ParameterGrid` / `Parameter` / `ParameterHeading` / `ParameterGroup` | Shared label column, collapsible groups with persistence |
| `Popover` | `TweeqPopover` | 12 placements, flip/shift, light dismiss |
| `Balloon` | `TweeqBalloon` | |
| `Tooltip` | `TweeqTooltip` | Single instance, show/move delays |
| `TweakOverlay` | per-widget overlays | Drawn on the shared `TweeqOverlayLayer` |
| `PaneModal` | `TweeqModal` | Centered balloon, emphasize bounce, non-dismissing |
| `PaneModalComplex` / `PaneModalTabs` | `TweeqModalDialog` (+ `TweeqTabs`) | Title, scrolling body, Cancel/Confirm footer, Escape/Enter |
| `Tabs` | `TweeqTabs` / `TweeqTab` | Keyboard navigation, persistence via `ITweeqTabStorage` |
| `TweeqProvider` / `useTweeq` / `theme` | `TweeqRoot` + `TweeqTheme` | Theme generation and distribution; USS `--tq-*` seeds |
| `validator` | `NumberValidator` (`Tweeq.Core`) | clamp → quantize composition |

All widgets are `[UxmlElement]`s and can be placed from UXML/UI Builder
(see the `UxmlDemo` sample in the package).

## Planned

| Original | Status |
| --- | --- |
| `PaneSplit`, `PaneFloating` | Planned as a separate workspace assembly (split/floating panes + layout persistence). Will be designed against concrete host-app use cases. |
| `InputDrum`, `InputTime` | Ported when a concrete need appears. |

## Not ported (out of scope)

App-level or web-specific pieces that a host application should own, or that
have no sensible Unity equivalent:

- **Editors / canvases**: `InputCode`, `MonacoEditor`, `GlslCanvas`, `Markdown`
- **App chrome**: `CommandPalette`, `Menu`, `TitleBar`, `PaneZUI`, `Timeline`,
  `Viewport`, `Ruler`
- **Selection**: `MultiSelectPopup` and simultaneous multi-parameter editing
- **Expression input**: JavaScript expressions in number fields
- **Eyedropper**: relies on a browser API
- **Icon system**: `Icon` / `SvgIcon` / `BindIcon` / `ColorIcon` /
  `IconIndicator` — this port draws its glyphs directly with `Painter2D`
- `InputCubicBezier`, `InputComplex`
- `PaneExpandable` — collapsible groups are covered by `ParameterGroup`
- `stores` (application state) — out of a widget library's responsibility

## Custom widgets

Anything missing can be built outside the package with the same look and
feel — see [Extending](architecture.md#extending) and the `EndpointInput`
sample in the demo project.
