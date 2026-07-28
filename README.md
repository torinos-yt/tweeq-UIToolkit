<div align="center">

<img src="./docs/logo.svg" width="200" />
<h1>Tweeq for Unity UI Toolkit</h1>

A Unity UI Toolkit port of <a href="https://github.com/baku89/tweeq">Tweeq</a> — parameter-tuning GUI widgets by/for creative professionals.

</div>

> [!NOTE]
> This repository is a fork of [baku89/tweeq](https://github.com/baku89/tweeq).
> The original Vue implementation this port derives from is preserved in git
> history at the tag [`vue-final`](https://github.com/torinos-yt/tweeq-UIToolkit/tree/vue-final);
> everything on `main` from that point on is the Unity port.

Tweeq is a collection of input components for design tools, developed by the
visual artist [Baku Hashimoto](https://baku89.com) — numeric sliders, rotary
knobs, color pickers and other controls built around fast, precise
micro-interactions. This fork re-implements those widgets as native
`VisualElement`s for Unity's UI Toolkit, shipped as the UPM package
`jp.torinos.tweeq-uitoolkit`, with runtime tools (live-performance /
projection-mapping control panels and similar) as the primary target.

The port keeps the original's interaction design — drag-to-tweak gestures,
continuous sensitivity control, snapping, modifier keys, cancelable edit
sessions — and its Radix-based theming pipeline, reimplemented in pure C#.
See [docs/architecture.md](docs/architecture.md) for how, and
[docs/deviations.md](docs/deviations.md) for the places it intentionally
differs.

## Installation

Add the package to your project via the Unity Package Manager
(`+` → *Install package from git URL…*):

```
https://github.com/torinos-yt/tweeq-UIToolkit.git?path=Unity/jp.torinos.tweeq-uitoolkit
```

Requires Unity 6000.3+ and no dependencies beyond UI Toolkit.
[ZString](https://github.com/Cysharp/ZString) is picked up automatically if
present (optional, reduces per-gesture allocations).

This repository stores binary assets (bundled fonts, demo scene) with Git LFS,
so `git` and `git-lfs` must be available on your machine when installing via
git URL.

## Quick start

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

All widgets are also `[UxmlElement]`s, so the same UI can be authored in
UXML/UI Builder — theming included, via USS custom properties
(`--tq-accent`, `--tq-gray`, `--tq-background`, `--tq-color-mode`). The
package's *UXML Demo* sample shows a full panel built without any C#.

## What's in the box

- **Inputs**: number (bar/scrub/scale ticks), rotary knob, vector (2/3/4),
  position / size / translate pads, angle, string, color (HSV pad, channel
  scrub, presets), dropdown with fuzzy filtering, shuffle, checkbox, switch,
  button, button toggle, radio, SMPTE time/timecode field with per-digit
  scrub, cubic-bezier easing editor.
- **Time**: a zoom/pan timeline viewport (hosts pin their own lane content)
  and a tick/label ruler with drag-to-value.
- **Layout**: input groups with fused corners, parameter grid with a shared
  label column, collapsible parameter groups.
- **Floating UI**: popover placement engine, balloons, tooltips, modals and
  modal dialogs, tabs with keyboard navigation.
- **Theming**: Radix-scale theme generation from arbitrary seed colors,
  light/dark, USS seed properties (colors and fonts), bundled
  [Geist](https://vercel.com/font) font (OFL) with all four font tokens
  replaceable.
- **Extensible**: the input-box chrome, scrub gesture wiring, focus ring and
  theming contracts are public, so host projects can build their own widgets
  with the same look and feel (a worked example ships in the demo project).
- Persistence (active tabs, collapsed groups) is in-memory by default — the
  library writes nothing to disk unless the host opts into `PlayerPrefs`.

See [docs/components.md](docs/components.md) for the full mapping against the
original component set, including what is planned and what is intentionally
out of scope.

The `Unity/tweeqDemo` project in this repository is the development/demo
project — open it and play `Assets/Scenes/Demo.unity` to try every widget.

## About the original Tweeq

Tweeq has been developed in parallel with Baku Hashimoto's animation projects
as part of the design tools used in those projects
([Koma](https://github.com/baku89/koma), [Unim](https://github.com/baku89/unim)),
following these design principles:

- support diverse input modalities to match users' nuanced control strategies,
- prioritize high-speed and accurate interaction for skilled users, and
- minimize visual footprint to preserve the creative workspace.

Research-wise, the project has been carried out by Baku, partly in his capacity
of a collaborative researcher at AIST, in collaboration with
[Jun Kato](https://junkato.jp), a senior researcher at AIST. For more details,
please refer to [the project page](https://junkato.jp/tweeq), the
[original documentation](https://baku89.github.io/tweeq/), and the following
open-access paper:

> Baku Hashimoto and Jun Kato. 2025. Tweeq: Parameter-Tuning GUI Widgets by/for Creative Professionals. In <i>The 38th Annual ACM Symposium on User Interface Software and Technology (UIST '25), September 28–October 01, 2025, Busan, Republic of Korea</i>. ACM, New York, NY, USA, 16 pages. https://doi.org/10.1145/3746059.3747723

If you find the original project useful, consider
[becoming a sponsor](https://github.com/sponsors/baku89).

## License

MIT — original work © Baku Hashimoto.
Bundled third-party assets (Geist font, Radix color scales) are listed in the
package's `Third Party Notices.md`.
