# Deviations from the original

Everywhere else this port follows the original tweeq (Vue) faithfully — down to
gesture constants and transition timings. The differences below are deliberate,
each with its reason. When something looks different from the Vue version and
is *not* listed here, it is a bug worth reporting.

## Platform substitutions

Web capabilities without a Unity equivalent, replaced rather than emulated:

- **Pointer Lock → plain drags.** Infinite scrubbing with a hidden, warping
  cursor does not exist in Unity. Widgets use ordinary drags; the rotary is
  unaffected (screen-angle based).
- **`backdrop-filter: blur` → opaque composite surfaces.** The original's
  floating chrome uses a semi-transparent surface color that only reads well
  over a blur. Without blur, the background bleeds through, so every floating
  surface (balloons, dropdown popups, overlay labels, modal panes) uses
  `TweeqTheme.SurfaceOpaque` — the surface color pre-composited over the
  background at full opacity.
- **CSS anchor positioning → manual placement math** (`PopoverLogic` in
  `Tweeq.Core`: placement, flip, shift, screen-edge avoidance).
- **SVG → `Painter2D`** vector drawing.
- **`system-ui` font stack → panel default font**, with bundled Geist for
  numeric/heading/code text.

## Interaction

- **`Escape` during text editing restores the value at edit start.** The
  original only cancels drags; this port treats text editing as an edit session
  with the same Begin/Commit/Cancel contract.
- **`TranslateInput` Y axis is inverted: dragging up increases Y.** The
  original follows DOM convention (down = +Y); Unity users expect +Y up.
  Only the value mapping is flipped — the overlay grid still follows the
  pointer visually.
- **Fields with an active range clamp the internal value during the drag**,
  not just the output, so leaving the clamped zone responds immediately.
- **Scrub scale ticks can show the actual reachable values as numbers.** The
  default is faithful to the original (`NumberInput.ScaleStyle = Dots`); the
  numeric readout is opt-in via `ScaleStyle = Values` (UXML `scale-style`),
  which drops the original's stepped-and-clamped gate and shows labels on every
  field. Unranged scrub sensitivity is fixed at `step / 20 px` in both styles.
- **`Tab` focus enters text-edit mode with select-all**, enabling
  Tab → type → Tab → type flows across a parameter panel.
- **Arrow-key focus navigation can be suppressed** via
  `TweeqNavigation.DisableArrowFocusNavigation(root)` (opt-in per app), since
  arrow keys are value-editing keys in tweeq widgets.

## Modals

- **The backdrop dims (background @ 50 %) and blocks pointers.** The original
  only blurs the backdrop and leaves the background interactive. This port
  targets live-performance tools, where a stray click behind a modal is an
  accident; the dim also compensates for the missing blur. Outside clicks
  trigger the emphasize bounce (faithful) and raise `OutsideClicked`, so an app
  can opt into close-on-outside-click with one line
  (`dialog.OutsideClicked += dialog.PerformCancel;`).
- **Cancel does not auto-rollback edited values.** The original can restore
  state through its app-level store; a widget library has no such layer, so
  rollback on Cancel is the host's responsibility (the demo shows the pattern).
- **Inactive tab panels are `display: none`** (kept in the tree, not unmounted).

## API shape

- Values follow UI Toolkit's `INotifyValueChanged<T>` / `ChangeEvent<T>`
  convention instead of Vue's `modelValue` binding.
- `ColorInput` uses **`UnityEngine.Color`** as its value type.
- `VecInput` (`float[]`) uses **copy semantics** on get/set to avoid exposing a
  mutable internal buffer; the typed `Vec2/3/4Input` variants use structs and
  are allocation-free during gestures.
- Persistence (collapsed groups, active tabs) goes through **`PlayerPrefs`** by
  default, replaceable via `ITweeqTabStorage`.

## Visual details

- **Disabled state is uniformly opacity 0.4** including widgets the original
  leaves without a distinct disabled look (rotary, translate pad).
- The demo seeds its accent with `#4a76ff` instead of pure blue — a demo-side
  choice for legibility on dark backgrounds; the theme engine itself stays
  faithful to the Radix pipeline.

## Fixes to inherited quirks

Behaviors that differ from the Vue original because the original behavior
appears unintended:

- **Tabs**: activating a disabled tab is refused unconditionally; every
  fallback tier of active-tab resolution skips disabled tabs; duplicate ids and
  out-of-range indices are guarded; a custom `storageKey` is honored for
  persistence; keyboard navigation (arrows / Home / End with roving tabindex)
  is added per WAI-ARIA.
- **Tab indicator transition** uses the intended `ActiveTransitionDuration`
  (64 ms) — the original references a token that doesn't resolve.
- **Color presets** emit a confirmed edit session when clicked, like every
  other one-shot edit.
