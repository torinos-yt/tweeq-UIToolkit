using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// Tweeq.Core を丸ごと using すると TweeqRect / TweeqVec2 が UnityEngine 側と紛らわしくなるので
// （TweeqPopover と同じ理由）、このファイルで実際に使う 2 つだけ別名で引き込む
using HSVA = Tweeq.Core.Hsva;
using CoreRgba = Tweeq.Core.Rgba;
using TweeqColorLogic = Tweeq.Core.TweeqColorLogic;

namespace Tweeq.UIToolkit
{
    /// <summary>ピッカー内でドラッグ中の軸。</summary>
    public enum ColorPickerAxis
    {
        /// <summary>ドラッグしていない。</summary>
        None,

        /// <summary>SV パッド（彩度と明度を同時に）。</summary>
        SaturationValue,

        /// <summary>Hue バー。</summary>
        Hue,

        /// <summary>Alpha バー。</summary>
        Alpha,
    }

    /// <summary>
    /// スウォッチを直接ドラッグしたとき（チャンネルスクラブ）に動かす対象。
    /// 修飾キーで切り替わる（m6-wave2-spec.md §A の tweakMode）。
    /// </summary>
    public enum ColorTweakMode
    {
        /// <summary>キー無し。横=彩度・縦=明度。</summary>
        Pad,

        /// <summary>Shift / H / F。</summary>
        Hue,

        /// <summary>S。</summary>
        Saturation,

        /// <summary>V。縦ドラッグのみ。</summary>
        Value,

        /// <summary>R。</summary>
        Red,

        /// <summary>G。</summary>
        Green,

        /// <summary>B。</summary>
        Blue,

        /// <summary>Alt / A。</summary>
        Alpha,
    }

    /// <summary>
    /// カラー入力（string-color-spec.md「ColorInput」）。
    /// フィールドは 24×24 のスウォッチ 1 個で、クリックすると <see cref="TweeqPopover"/> の上に
    /// SV パッド／Hue バー／Alpha バー／数値行／プリセットを載せたピッカーが開く。
    ///
    /// 値は <see cref="UnityEngine.Color"/>（意図的逸脱: Vue の契約は CSS 文字列）。
    /// HSVA は「黒・彩度 0 で hue が失われる」のを避けるためピッカー操作の間だけ内部状態として持ち、
    /// 出力は常に Color へ畳む。
    ///
    /// 開閉・ドラッグセッション・プリセット・HEX 同期は panel 非依存の論理層として実装してあり
    /// （<see cref="OpenPicker"/> / <see cref="BeginPickerDrag"/> / <see cref="PerformPresetClick"/> …）、
    /// 表示だけがその上に乗る。EditMode テストはこの層を叩く（DropdownInput と同じ設計）。
    /// </summary>
    [UxmlElement]
    public partial class ColorInput
        : VisualElement, INotifyValueChanged<Color>, ITweeqInputBox, ITweeqThemed
    {
        #region Constants

        /// <summary>colorSpace ドロップダウンの選択肢。Vue InputColorChannelValues と同じ順・同じ綴り。</summary>
        public const string COLOR_SPACE_RGB = "rgb";

        /// <summary>colorSpace: HSV。</summary>
        public const string COLOR_SPACE_HSV = "hsv";

        /// <summary>colorSpace: HEX。</summary>
        public const string COLOR_SPACE_HEX = "hex";

        // チェッカーボードの 1 マス（Vue common.styl の background-checkerboard() は size = 6px）
        const float CHECKER_CELL = 6f;

        // SV グラデーションのテクスチャ解像度。バイリニア拡大前提なので 64 で十分
        // （240px 幅へ引き伸ばしても HSV は線形に近く、バンドは見えない）
        const int SV_TEXTURE_SIZE = 64;

        // 虹テクスチャは横 1 列。全インスタンスで 1 枚を共有する
        const int HUE_TEXTURE_WIDTH = 256;

        // Hue / Alpha バーの高さ。Vue の 0.7 * inputHeight = 16.8 を整数化した値（仕様 §ColorInput）
        const float SLIDER_HEIGHT = 17f;

        // 数値行の colorSpace ドロップダウン幅。Vue は 5rem だが、
        // 幅 240 のパネルでは 4 チャンネルが潰れるので「RGB」が収まる最小に詰める
        // DropdownInput はシェブロン幅（16.8px）を左右対称に逃がすため、
        // 3 文字ラベル（RGB/HSV/HEX）には 56px では足りず省略記号になる
        const float COLOR_SPACE_WIDTH = 72f;

        const float CURSOR_RADIUS = 6f;
        const float CURSOR_RING_WIDTH = 1.5f;
        const float CURSOR_SHADE_WIDTH = 1f;

        const float FIELD_OUTLINE_WIDTH = 1f;

        // 1 行に並ぶプリセット数。24 + gap6 = 30px × 7 = 210 で 222px の中身幅に収まる
        const int PRESETS_PER_ROW = 7;

        // 数値行のチャンネル数（RGB/HSV + A）
        const int CHANNEL_COUNT = 4;

        // 素の値がこの範囲を出たら畳む。HSVA の s/v/a は [0,1]、h は [0,360)
        const double HUE_RANGE = 360.0;

        // スウォッチのクリックとチャンネルスクラブを分ける閾値（RotaryInput と同値）
        const float MOUSE_DRAG_THRESHOLD = 3f;
        const float TOUCH_DRAG_THRESHOLD = 5f;

        // スクラブの感度基準。Theme.PopupWidth が壊れている時のフォールバック（仕様 §A の 240）
        const float TWEAK_WIDTH_FALLBACK = 240f;

        #endregion

        #region Fields

        /// <summary>colorSpace の選択肢（RGB / HSV / HEX）。</summary>
        static readonly string[] ColorSpaceOptions = { COLOR_SPACE_RGB, COLOR_SPACE_HSV, COLOR_SPACE_HEX };

        /// <summary>
        /// 既定のプリセットパレット。Vue 版はアプリ側からの inject 前提で空だが、
        /// 単体で置いても使えるよう「無彩色 5 + 色相 9」を用意した（<see cref="Presets"/> で差し替え可）。
        /// </summary>
        static readonly Color[] DefaultPresetPalette =
        {
            new Color32(0x00, 0x00, 0x00, 0xFF),
            new Color32(0x40, 0x40, 0x40, 0xFF),
            new Color32(0x80, 0x80, 0x80, 0xFF),
            new Color32(0xC0, 0xC0, 0xC0, 0xFF),
            new Color32(0xFF, 0xFF, 0xFF, 0xFF),
            new Color32(0xFF, 0x00, 0x00, 0xFF),
            new Color32(0xFF, 0x80, 0x00, 0xFF),
            new Color32(0xFF, 0xFF, 0x00, 0xFF),
            new Color32(0x00, 0xFF, 0x00, 0xFF),
            new Color32(0x00, 0xFF, 0xFF, 0xFF),
            new Color32(0x00, 0x80, 0xFF, 0xFF),
            new Color32(0x00, 0x00, 0xFF, 0xFF),
            new Color32(0x80, 0x00, 0xFF, 0xFF),
            new Color32(0xFF, 0x00, 0xFF, 0xFF),
        };

        // チェッカーボードの 2 色。Vue は white / #ddd 固定（テーマに追従しない）
        static readonly Color CheckerLight = new Color32(0xFF, 0xFF, 0xFF, 0xFF);
        static readonly Color CheckerDark = new Color32(0xDD, 0xDD, 0xDD, 0xFF);

        // カーソルの輪郭。Vue の box-shadow 0 0 0 1.5px #fff / inset 0 0 0 1px rgba(0,0,0,.2)
        static readonly Color CursorRing = new Color(1f, 1f, 1f, 1f);
        static readonly Color CursorShade = new Color(0f, 0f, 0f, 0.2f);

        // 虹テクスチャは色相以外に依存しないので 1 枚を使い回す
        static Texture2D SharedHueTexture;

        TweeqTheme _theme = TweeqTheme.Dark();

        Color _value = Color.white;

        // ピッカー操作の間の権威。black / 彩度 0 でも hue を失わないために Color とは別に持つ
        HSVA _hsva;

        Color[] _presets = DefaultPresetPalette;
        string _colorSpace = COLOR_SPACE_HSV;

        bool _disabled;
        bool _hovered;
        bool _focused;
        bool _open;

        TweeqBoxPosition _inlinePosition = TweeqBoxPosition.None;
        TweeqBoxPosition _blockPosition = TweeqBoxPosition.None;

        // HEX 文字列は「実際に値が変わっていて、かつ HEX 行が見えている」ときだけ作る。
        // SV ドラッグ中に RGB/HSV 行が出ていれば 1 文字も確保しない
        string _hexText = string.Empty;
        bool _hexDirty = true;

        // HEX 欄への打鍵から値を更新している最中は、正規形の書き戻しを止める（キャレットが飛ぶため）
        bool _syncingHex;

        // フィールド
        VisualElement _swatch;

        // ピッカー（panel 付きで初めて組む。開くたびには作らない）
        TweeqPopover _popover;
        VisualElement _picker;
        VisualElement _svPad;
        VisualElement _svCursor;
        VisualElement _hueBar;
        VisualElement _hueCursor;
        VisualElement _alphaBar;
        VisualElement _alphaChecker;
        VisualElement _alphaGradient;
        VisualElement _alphaCursor;
        InputGroup _valuesRow;
        DropdownInput<string> _spaceDropdown;
        readonly NumberInput[] _channels = new NumberInput[CHANNEL_COUNT];
        StringInput _hexField;
        VisualElement _presetsRow;
        readonly List<VisualElement> _presetButtons = new List<VisualElement>();

        // SV グラデーション。hue が変わった時だけ焼き直し、バッファは使い回す
        Texture2D _svTexture;
        Color32[] _svPixels;
        double _svTextureHue = double.NaN;

        ColorPickerAxis _dragAxis = ColorPickerAxis.None;
        int _dragPointerId = PointerId.invalidPointerId;

        // ドラッグ開始時の値。Escape でのキャンセル先
        Color _valueOnDragStart;

        // light dismiss はスウォッチ押下でも走る（popover は panel root の TrickleDown で拾うので
        // こちらの PointerDown より先に閉じてしまう）。同一フレームの再オープンだけ抑止してトグルにする
        bool _suppressReopen;
        IVisualElementScheduledItem _reopenGuardItem;

        // スウォッチの押下。閾値を超えるまではクリック（＝ピッカーのトグル）候補のまま保留する
        bool _swatchPressed;
        int _swatchPointerId = PointerId.invalidPointerId;
        Vector2 _pressPanelPosition;
        float _scrubThreshold = MOUSE_DRAG_THRESHOLD;

        // 押した瞬間の開閉。light dismiss は PointerDown より先に走るので、
        // トグルの判定材料は PointerUp まで持ち越さず押下時に確定させる
        bool _openOnPress;

        // チャンネルスクラブ。値は「基準 HSVA ＋ 基準位置からの移動量」で決める。
        // 累積 delta を積まないので、モード切替で基準を取り直すだけで値が飛ばない（仕様 §A）
        bool _scrubbing;
        ColorTweakMode _scrubMode = ColorTweakMode.Pad;
        Vector2 _scrubOrigin;
        Vector2 _scrubAnchor;
        Vector2 _scrubPointer;
        HSVA _scrubBase;
        Color _valueOnScrubStart;
        bool _cursorHidden;
        ColorTweakOverlay _scrubOverlay;

        // tweakMode の材料。修飾キーはイベントの modifiers、英字キーは押下状態を自前で追う
        bool _shiftHeld;
        bool _altHeld;
        bool _hueKeyHeld;
        bool _fillKeyHeld;
        bool _satKeyHeld;
        bool _valKeyHeld;
        bool _redKeyHeld;
        bool _greenKeyHeld;
        bool _blueKeyHeld;
        bool _alphaKeyHeld;

        // メソッドグループ変換のたびにデリゲートを確保しないよう、登録／解除で使い回す実体を持つ
        readonly EventCallback<PointerDownEvent> _onSvPointerDown;
        readonly EventCallback<PointerMoveEvent> _onSvPointerMove;
        readonly EventCallback<PointerUpEvent> _onSvPointerUp;

        #endregion

        #region Public API

        /// <summary>値が変わるたびに発火する。ドラッグ中は pointermove ごとに飛ぶ。</summary>
        public event Action<Color> ValueChanged;

        /// <summary>
        /// ドラッグ終了・プリセットクリック・ピッカー内フィールドの確定で 1 操作につき 1 回発火する。
        /// </summary>
        public event Action<Color> Confirmed;

        /// <summary>現在の色。α も値の一部なので、UXML では #RRGGBBAA で与える。</summary>
        [UxmlAttribute]
        public Color value
        {
            get => _value;
            set
            {
                if (SameColor(_value, value))
                {
                    return;
                }

                Color previous = _value;
                SetValueWithoutNotify(value);
                ValueChanged?.Invoke(_value);
                NotifyValueChanged(previous, _value);
            }
        }

        /// <summary>ChangeEvent / ValueChanged を発火せずに値を設定する。HSVA も引き直す。</summary>
        public void SetValueWithoutNotify(Color newValue)
        {
            _value = newValue;
            _hsva = DeriveHsva(newValue, _hsva);
            _hexDirty = true;
            Refresh();
        }

        /// <summary>配色テーマ。null を渡した場合は Dark() にフォールバックする。</summary>
        public TweeqTheme Theme
        {
            get => _theme;
            set
            {
                _theme = value ?? TweeqTheme.Dark();
                ApplyStaticStyles();
                Refresh();
            }
        }

        /// <summary>操作不能状態。開いていればピッカーも閉じる。</summary>
        [UxmlAttribute]
        public bool Disabled
        {
            get => _disabled;
            set
            {
                if (_disabled == value)
                {
                    return;
                }

                _disabled = value;

                if (_disabled)
                {
                    // 無効化の瞬間に開いたままだと閉じる手段が無くなる
                    CancelPickerDrag();
                    CancelChannelScrub();
                    ClosePicker();
                }

                this.pickingMode = _disabled ? PickingMode.Ignore : PickingMode.Position;

                if (_swatch != null)
                {
                    _swatch.pickingMode = _disabled ? PickingMode.Ignore : PickingMode.Position;
                }

                Refresh();
            }
        }

        /// <summary>横方向グループでの位置。</summary>
        public TweeqBoxPosition InlinePosition
        {
            get => _inlinePosition;
            set
            {
                if (_inlinePosition == value)
                {
                    return;
                }

                _inlinePosition = value;
                ApplyCornerRadius();
            }
        }

        /// <summary>縦方向グループでの位置。</summary>
        public TweeqBoxPosition BlockPosition
        {
            get => _blockPosition;
            set
            {
                if (_blockPosition == value)
                {
                    return;
                }

                _blockPosition = value;
                ApplyCornerRadius();
            }
        }

        /// <summary>既定のプリセットパレットのコピー。<see cref="Presets"/> の初期値。</summary>
        public static Color[] DefaultPresets
        {
            get
            {
                Color[] copy = new Color[DefaultPresetPalette.Length];
                Array.Copy(DefaultPresetPalette, copy, DefaultPresetPalette.Length);
                return copy;
            }
        }

        /// <summary>
        /// プリセットパレット。設定・取得ともにコピーを通す（呼び出し側の配列と内部状態を切り離す）。
        /// null / 空を渡すとプリセット行は消える。
        /// </summary>
        public Color[] Presets
        {
            get
            {
                Color[] copy = new Color[_presets.Length];
                Array.Copy(_presets, copy, _presets.Length);
                return copy;
            }

            set
            {
                if (value == null)
                {
                    _presets = Array.Empty<Color>();
                }
                else
                {
                    _presets = new Color[value.Length];
                    Array.Copy(value, _presets, value.Length);
                }

                RebuildPresetButtons();
                Refresh();
            }
        }

        /// <summary>数値行の表示形式（<see cref="COLOR_SPACE_RGB"/> / HSV / HEX）。</summary>
        [UxmlAttribute]
        public string ColorSpace
        {
            get => _colorSpace;
            set
            {
                string next = NormalizeColorSpace(value);
                if (_colorSpace == next)
                {
                    return;
                }

                _colorSpace = next;

                if (_spaceDropdown != null)
                {
                    _spaceDropdown.SetValueWithoutNotify(_colorSpace);
                }

                RebuildValuesRow();
                Refresh();
            }
        }

        /// <summary>ピッカーが開いているか（論理状態。panel が無くても進む）。</summary>
        public bool IsPickerOpen => _open;

        /// <summary>ドラッグ中の軸。</summary>
        public ColorPickerAxis ActiveAxis => _dragAxis;

        /// <summary>現在の HSVA（h は度、s/v/a は [0,1]）。</summary>
        public HSVA Hsva => _hsva;

        /// <summary>
        /// HEX 表記。α=1 なら 6 桁、α&lt;1 なら 8 桁（<see cref="TweeqColorLogic.FormatHex"/> の契約）。
        /// 実際に読まれた時にだけ組み立てるので、ドラッグ中に HEX 行が出ていなければ文字列は作られない。
        /// </summary>
        public string HexText
        {
            get
            {
                EnsureHexText();
                return _hexText;
            }
        }

        /// <summary>ピッカーを開く。無効化中は何もしない。</summary>
        public void OpenPicker()
        {
            if (_open || _disabled)
            {
                return;
            }

            _open = true;
            ShowPicker();
            Refresh();
        }

        /// <summary>ピッカーを閉じる。色は巻き戻さない（開いている間の変更は逐次通知済み）。</summary>
        public void ClosePicker()
        {
            if (!_open)
            {
                return;
            }

            _open = false;
            _popover?.Close();
            Refresh();
        }

        /// <summary>開いていれば閉じ、閉じていれば開く。</summary>
        public void TogglePicker()
        {
            if (_open)
            {
                ClosePicker();
            }
            else
            {
                OpenPicker();
            }
        }

        /// <summary>
        /// HSVA を直接設定する。<see cref="ValueChanged"/> は飛ぶが <see cref="Confirmed"/> は飛ばない。
        /// h は度（範囲外は 0-360 へ畳む）、s/v/a は [0,1] にクランプされる。
        /// </summary>
        public void SetHsva(double h, double s, double v, double a)
        {
            ApplyHsva(new HSVA(WrapHue(h), Clamp01(s), Clamp01(v), Clamp01(a)));
        }

        /// <summary>
        /// ドラッグセッションを開始する。すでに別軸を掴んでいたら、そちらは確定させずに切り替える。
        /// </summary>
        public void BeginPickerDrag(ColorPickerAxis axis)
        {
            if (_disabled || axis == ColorPickerAxis.None)
            {
                return;
            }

            _dragAxis = axis;
            _valueOnDragStart = _value;
            Refresh();
        }

        /// <summary>
        /// ドラッグ中の位置を反映する。x / y は対象要素内の正規化座標（0-1、y は上が 0）。
        /// pointermove ごとに呼ぶ想定で、毎回 <see cref="ValueChanged"/> が飛ぶ（間引きなし・Vue 準拠）。
        /// </summary>
        public void UpdatePickerDrag(float normalizedX, float normalizedY)
        {
            if (_dragAxis == ColorPickerAxis.None || _disabled)
            {
                return;
            }

            double x = Clamp01(normalizedX);
            double y = Clamp01(normalizedY);

            switch (_dragAxis)
            {
                case ColorPickerAxis.SaturationValue:
                    // 縦は上が v=1。Vue の pad と同じ向き
                    ApplyHsva(new HSVA(_hsva.H, x, 1.0 - y, _hsva.A));
                    break;

                case ColorPickerAxis.Hue:
                    ApplyHsva(new HSVA(x * HUE_RANGE, _hsva.S, _hsva.V, _hsva.A));
                    break;

                case ColorPickerAxis.Alpha:
                    ApplyHsva(new HSVA(_hsva.H, _hsva.S, _hsva.V, x));
                    break;
            }
        }

        /// <summary>ドラッグを終了して <see cref="Confirmed"/> を 1 回だけ発火する。</summary>
        public void EndPickerDrag()
        {
            if (_dragAxis == ColorPickerAxis.None)
            {
                return;
            }

            _dragAxis = ColorPickerAxis.None;
            Refresh();
            Confirmed?.Invoke(_value);
        }

        /// <summary>ドラッグ開始時の値へ戻して終了する。<see cref="Confirmed"/> は発火しない。</summary>
        public void CancelPickerDrag()
        {
            if (_dragAxis == ColorPickerAxis.None)
            {
                return;
            }

            _dragAxis = ColorPickerAxis.None;

            // ドラッグ中に通知した値を巻き戻すので、戻す方向も通知する
            this.value = _valueOnDragStart;
            Refresh();
        }

        /// <summary>チャンネルスクラブ中か。</summary>
        public bool IsScrubbing => _scrubbing;

        /// <summary>現在の tweakMode。スクラブ外でも次に掴んだときの初期モードとして残る。</summary>
        public ColorTweakMode ScrubMode => _scrubMode;

        /// <summary>クリックとスクラブを分ける移動量（px・マウス）。</summary>
        public static float ScrubThreshold => MOUSE_DRAG_THRESHOLD;

        /// <summary>
        /// チャンネルスクラブを開始する。<paramref name="panelPosition"/> はオーバーレイの
        /// 原点であると同時に移動量の基準点になる（パネル座標）。
        /// ピッカーが開いていれば閉じる（Vue: tweaking になったら open=false）。
        /// </summary>
        public void BeginChannelScrub(Vector2 panelPosition)
        {
            if (_disabled || _scrubbing)
            {
                return;
            }

            _scrubbing = true;
            _scrubOrigin = panelPosition;
            _scrubAnchor = panelPosition;
            _scrubPointer = panelPosition;
            _scrubBase = _hsva;
            _valueOnScrubStart = _value;

            ClosePicker();
            HideCursor();
            AcquireScrubOverlay();
            Refresh();
        }

        /// <summary>
        /// スクラブ中のポインタ位置（パネル座標）を反映する。基準点からの移動量を
        /// そのまま値へ写すので、毎ムーブ <see cref="ValueChanged"/> が飛ぶ（間引きなし）。
        /// </summary>
        public void UpdateChannelScrub(Vector2 panelPosition)
        {
            if (!_scrubbing || _disabled)
            {
                return;
            }

            _scrubPointer = panelPosition;
            ApplyScrub();
        }

        /// <summary>
        /// tweakMode を切り替える。スクラブ中なら現在値と現在位置を新しい基準として
        /// 取り直すので、切替の瞬間に値が飛ばない（egui は累積 delta のまま切り替えてジャンプする）。
        /// </summary>
        public void SetScrubMode(ColorTweakMode mode)
        {
            if (_scrubMode == mode)
            {
                return;
            }

            _scrubMode = mode;

            if (!_scrubbing)
            {
                return;
            }

            _scrubBase = _hsva;
            _scrubAnchor = _scrubPointer;
            Refresh();
        }

        /// <summary>スクラブを終了して <see cref="Confirmed"/> を 1 回だけ発火する。</summary>
        public void EndChannelScrub()
        {
            if (!_scrubbing)
            {
                return;
            }

            StopScrub();
            Refresh();
            Confirmed?.Invoke(_value);
        }

        /// <summary>
        /// スクラブ開始時の色へ戻して終了する（Escape）。<see cref="Confirmed"/> は発火しない。
        /// </summary>
        public void CancelChannelScrub()
        {
            if (!_scrubbing)
            {
                return;
            }

            Color restored = _valueOnScrubStart;
            StopScrub();

            // スクラブ中に通知した値を巻き戻すので、戻す方向も通知する
            this.value = restored;
            Refresh();
        }

        /// <summary>
        /// プリセットのクリック。<see cref="ValueChanged"/> と <see cref="Confirmed"/> を対で出す
        /// （原典は confirm が飛ばないバグ。React 修正版 + test-contracts inputColor.ts の契約を採用）。
        /// </summary>
        public void PerformPresetClick(int index)
        {
            if (_disabled || index < 0 || index >= _presets.Length)
            {
                return;
            }

            this.value = _presets[index];
            Confirmed?.Invoke(_value);
        }

        /// <summary>
        /// HEX 欄への入力（バリデータ通過ごと）。パースできた時だけ値へ反映する。
        /// <see cref="Confirmed"/> は飛ばない（確定は <see cref="PerformHexConfirm"/>）。
        /// </summary>
        public void PerformHexInput(string text)
        {
            if (_disabled || !TryParseHex(text, out Color parsed))
            {
                return;
            }

            // 打った文字そのものを正規形へ書き戻すと編集中にキャレットが飛ぶので、
            // 値の反映が誘発する再同期を止めておく。表示を揃えるのは確定時（PerformHexConfirm）
            _syncingHex = true;

            try
            {
                this.value = parsed;
            }
            finally
            {
                _syncingHex = false;
            }

            _hexText = text;
            _hexDirty = false;
        }

        /// <summary>HEX 欄の確定（blur / Enter）。表示を正規形へ揃えて <see cref="Confirmed"/> を出す。</summary>
        public void PerformHexConfirm()
        {
            if (_disabled)
            {
                return;
            }

            _hexDirty = true;
            RefreshHexField(true);
            Confirmed?.Invoke(_value);
        }

        /// <summary>HEX 文字列として妥当か（StringInput の Validator にそのまま渡せる）。</summary>
        public static bool IsValidHex(string text)
        {
            return TryParseHex(text, out _);
        }

        #endregion

        #region Construction

        public ColorInput()
        {
            this.AddToClassList("tweeq-color-input");

            // Enter / Space / Escape を受け取るためルート自身がフォーカスを持つ
            this.focusable = true;
            this.style.flexDirection = FlexDirection.Row;
            this.style.alignItems = Align.Center;
            this.style.flexShrink = 0f;

            _onSvPointerDown = OnPickerPointerDown;
            _onSvPointerMove = OnPickerPointerMove;
            _onSvPointerUp = OnPickerPointerUp;

            BuildField();
            ApplyStaticStyles();

            this.RegisterCallback<KeyDownEvent>(OnKeyDown);
            this.RegisterCallback<KeyUpEvent>(OnKeyUp);
            this.RegisterCallback<FocusInEvent>(OnFocusIn);
            this.RegisterCallback<FocusOutEvent>(OnFocusOut);
            this.RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            this.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);

            _hsva = DeriveHsva(_value, new HSVA(0.0, 0.0, 1.0, 1.0));
            Refresh();
        }

        void BuildField()
        {
            _swatch = new VisualElement { name = "tweeq-color-swatch" };
            _swatch.style.flexShrink = 0f;
            _swatch.style.overflow = Overflow.Hidden;
            _swatch.generateVisualContent += OnGenerateSwatch;
            _swatch.RegisterCallback<PointerDownEvent>(OnSwatchPointerDown);
            _swatch.RegisterCallback<PointerMoveEvent>(OnSwatchPointerMove);
            _swatch.RegisterCallback<PointerUpEvent>(OnSwatchPointerUp);
            _swatch.RegisterCallback<PointerCaptureOutEvent>(OnSwatchPointerCaptureOut);
            _swatch.RegisterCallback<PointerEnterEvent>(OnSwatchPointerEnter);
            _swatch.RegisterCallback<PointerLeaveEvent>(OnSwatchPointerLeave);
            this.hierarchy.Add(_swatch);
        }

        void ApplyStaticStyles()
        {
            if (_theme == null)
            {
                return;
            }

            this.style.height = _theme.InputHeight;
            this.style.minWidth = _theme.InputHeight;

            if (_swatch != null)
            {
                _swatch.style.width = _theme.InputHeight;
                _swatch.style.height = _theme.InputHeight;
            }

            ApplyCornerRadius();
            ApplyPickerStyles();
        }

        // 仕様 §1 の角丸表。角丸が乗るのはスウォッチ側（ルートは行に伸びるだけの箱）
        void ApplyCornerRadius()
        {
            if (_swatch == null)
            {
                return;
            }

            float radius = _theme != null ? _theme.InputRadius : 0f;

            bool topLeft = true;
            bool topRight = true;
            bool bottomLeft = true;
            bool bottomRight = true;

            switch (_inlinePosition)
            {
                case TweeqBoxPosition.Start:
                    topRight = false;
                    bottomRight = false;
                    break;

                case TweeqBoxPosition.Middle:
                    topLeft = false;
                    topRight = false;
                    bottomLeft = false;
                    bottomRight = false;
                    break;

                case TweeqBoxPosition.End:
                    topLeft = false;
                    bottomLeft = false;
                    break;
            }

            switch (_blockPosition)
            {
                case TweeqBoxPosition.Start:
                    bottomLeft = false;
                    bottomRight = false;
                    break;

                case TweeqBoxPosition.Middle:
                    topLeft = false;
                    topRight = false;
                    bottomLeft = false;
                    bottomRight = false;
                    break;

                case TweeqBoxPosition.End:
                    topLeft = false;
                    topRight = false;
                    break;
            }

            SetCornerRadius(_swatch, radius, topLeft, topRight, bottomLeft, bottomRight);
        }

        #endregion

        #region Picker construction

        // ピッカーの実体は panel が付いてから 1 回だけ組む。論理状態（開閉・ドラッグ・プリセット）は
        // この下に無くても進むので、EditMode テストはここを通らない
        void EnsurePickerElements()
        {
            if (_picker != null || _theme == null)
            {
                return;
            }

            _picker = new VisualElement { name = "tweeq-color-picker" };
            _picker.style.flexDirection = FlexDirection.Column;

            _svPad = new VisualElement { name = "tweeq-color-sv-pad" };
            _svPad.style.overflow = Overflow.Hidden;
            StretchBackground(_svPad);
            _svPad.RegisterCallback(_onSvPointerDown);
            _svPad.RegisterCallback(_onSvPointerMove);
            _svPad.RegisterCallback(_onSvPointerUp);
            _svPad.RegisterCallback<PointerCaptureOutEvent>(OnPickerPointerCaptureOut);
            _svPad.RegisterCallback<GeometryChangedEvent>(OnSvPadGeometryChanged);
            _picker.Add(_svPad);

            _svCursor = CreateOverlay("tweeq-color-sv-cursor");
            _svCursor.generateVisualContent += OnGenerateSvCursor;
            _svPad.Add(_svCursor);

            _hueBar = new VisualElement { name = "tweeq-color-hue-bar" };
            _hueBar.style.overflow = Overflow.Hidden;
            _hueBar.style.backgroundImage = new StyleBackground(GetHueTexture());
            StretchBackground(_hueBar);
            _hueBar.RegisterCallback(_onSvPointerDown);
            _hueBar.RegisterCallback(_onSvPointerMove);
            _hueBar.RegisterCallback(_onSvPointerUp);
            _hueBar.RegisterCallback<PointerCaptureOutEvent>(OnPickerPointerCaptureOut);
            _picker.Add(_hueBar);

            _hueCursor = CreateOverlay("tweeq-color-hue-cursor");
            _hueCursor.generateVisualContent += OnGenerateHueCursor;
            _hueBar.Add(_hueCursor);

            _alphaBar = new VisualElement { name = "tweeq-color-alpha-bar" };
            _alphaBar.style.overflow = Overflow.Hidden;
            _alphaBar.RegisterCallback(_onSvPointerDown);
            _alphaBar.RegisterCallback(_onSvPointerMove);
            _alphaBar.RegisterCallback(_onSvPointerUp);
            _alphaBar.RegisterCallback<PointerCaptureOutEvent>(OnPickerPointerCaptureOut);
            _picker.Add(_alphaBar);

            // チェッカー・グラデーション・カーソルを別要素に分けるのは、重なり順を
            // ヒエラルキーで保証するため（同一要素内の Painter2D と Allocate の順は保証されない）。
            // 併せて「色が変わってもチェッカーは描き直さない」も同時に成立する
            _alphaChecker = CreateOverlay("tweeq-color-alpha-checker");
            _alphaChecker.generateVisualContent += OnGenerateAlphaChecker;
            _alphaBar.Add(_alphaChecker);

            _alphaGradient = CreateOverlay("tweeq-color-alpha-gradient");
            _alphaGradient.generateVisualContent += OnGenerateAlphaGradient;
            _alphaBar.Add(_alphaGradient);

            _alphaCursor = CreateOverlay("tweeq-color-alpha-cursor");
            _alphaCursor.generateVisualContent += OnGenerateAlphaCursor;
            _alphaBar.Add(_alphaCursor);

            // InputGroup 既定の flexGrow 1 は「行の中で横に伸びる」ための指定。
            // 縦積みのピッカーでは高さまで伸びてしまうので落とす
            _valuesRow = new InputGroup { Theme = _theme };
            _valuesRow.style.flexGrow = 0f;
            _valuesRow.style.flexShrink = 0f;
            _picker.Add(_valuesRow);

            _spaceDropdown = new DropdownInput<string>(ColorSpaceOptions)
            {
                Theme = _theme,
                Labelizer = ToUpperLabel,
            };
            _spaceDropdown.SetValueWithoutNotify(_colorSpace);
            _spaceDropdown.ValueChanged += OnColorSpaceChanged;

            // InputGroup.ApplyStretch は「未指定なら flexGrow 1」なので、先に明示して固定幅にする
            _spaceDropdown.style.flexGrow = 0f;
            _spaceDropdown.style.flexShrink = 0f;
            _spaceDropdown.style.flexBasis = COLOR_SPACE_WIDTH;
            _spaceDropdown.style.width = COLOR_SPACE_WIDTH;

            for (int i = 0; i < CHANNEL_COUNT; i++)
            {
                _channels[i] = new NumberInput
                {
                    Theme = _theme,
                    Bar = false,
                    Precision = 0,
                    Step = 1.0,
                };
            }

            _channels[0].RegisterValueChangedCallback(OnChannel0Changed);
            _channels[1].RegisterValueChangedCallback(OnChannel1Changed);
            _channels[2].RegisterValueChangedCallback(OnChannel2Changed);
            _channels[3].RegisterValueChangedCallback(OnChannel3Changed);

            for (int i = 0; i < CHANNEL_COUNT; i++)
            {
                _channels[i].Confirmed += OnChildConfirmed;
            }

            _hexField = new StringInput
            {
                Theme = _theme,
                Validator = IsValidHex,
            };
            _hexField.ValueChanged += OnHexFieldChanged;
            _hexField.Confirmed += OnHexFieldConfirmed;

            _presetsRow = new VisualElement { name = "tweeq-color-presets" };
            _presetsRow.style.flexDirection = FlexDirection.Row;
            _presetsRow.style.flexWrap = Wrap.Wrap;
            _picker.Add(_presetsRow);

            RebuildValuesRow();
            RebuildPresetButtons();
            ApplyPickerStyles();
        }

        // background-size の既定は auto（＝ネイティブ解像度）なので、
        // 64×64 / 256×1 の小さなテクスチャは明示的に引き伸ばさないと中央に点で出る
        static void StretchBackground(VisualElement element)
        {
            element.style.backgroundSize =
                new BackgroundSize(Length.Percent(100f), Length.Percent(100f));
            element.style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
            element.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Left);
            element.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Top);
        }

        static VisualElement CreateOverlay(string name)
        {
            VisualElement overlay = new VisualElement
            {
                name = name,
                pickingMode = PickingMode.Ignore,
            };
            overlay.style.position = Position.Absolute;
            overlay.style.left = 0f;
            overlay.style.top = 0f;
            overlay.style.right = 0f;
            overlay.style.bottom = 0f;
            return overlay;
        }

        void ApplyPickerStyles()
        {
            if (_picker == null || _theme == null)
            {
                return;
            }

            // PopupWidth は外形。Chrome=true の popover は PopupPadding を自分で描くので、
            // 中身にはその内側の幅を渡す
            float contentWidth = Mathf.Max(0f, _theme.PopupWidth - _theme.PopupPadding * 2f);
            _picker.style.width = contentWidth;

            float gap = _theme.GapControl;
            float radius = _theme.InputRadius;

            _svPad.style.marginBottom = gap;
            SetCornerRadius(_svPad, radius, true, true, true, true);

            _hueBar.style.height = SLIDER_HEIGHT;
            _hueBar.style.marginBottom = gap;
            SetCornerRadius(_hueBar, radius, true, true, true, true);

            _alphaBar.style.height = SLIDER_HEIGHT;
            _alphaBar.style.marginBottom = gap;
            SetCornerRadius(_alphaBar, radius, true, true, true, true);

            _valuesRow.style.marginBottom = gap;
            _valuesRow.Theme = _theme;

            if (_spaceDropdown != null)
            {
                _spaceDropdown.Theme = _theme;
            }

            for (int i = 0; i < CHANNEL_COUNT; i++)
            {
                if (_channels[i] != null)
                {
                    _channels[i].Theme = _theme;
                }
            }

            if (_hexField != null)
            {
                _hexField.Theme = _theme;

                // HEX は桁が揺れると読みにくいので等幅（FontCode）。unityFontDefinition は
                // 継承プロパティなので、StringInput のルートへ掛けるだけで中の TextField まで届く
                TweeqFonts.Apply(_hexField, _theme.FontCode);
            }

            ApplyPresetStyles();
        }

        // colorSpace ごとに数値行の中身を入れ替える。切替は人の操作なので、
        // ここでの要素追加はドラッグ経路には乗らない
        void RebuildValuesRow()
        {
            if (_valuesRow == null)
            {
                return;
            }

            // 先頭の colorSpace ドロップダウンは据え置き、後ろのフィールドだけ入れ替える。
            // Clear するとドロップダウン自身が detach され、選択中のポップアップが閉じてしまう
            // （切替はドロップダウンの ValueChanged 経由＝まだ開いている最中に呼ばれる）
            for (int i = _valuesRow.childCount - 1; i >= 1; i--)
            {
                _valuesRow.Remove(_valuesRow.ElementAt(i));
            }

            if (_valuesRow.childCount == 0)
            {
                _valuesRow.Add(_spaceDropdown);
            }

            if (_colorSpace == COLOR_SPACE_HEX)
            {
                _valuesRow.Add(_hexField);
                RefreshHexField(true);
                _valuesRow.RefreshPositions();
                return;
            }

            for (int i = 0; i < CHANNEL_COUNT; i++)
            {
                NumberInput channel = _channels[i];
                if (channel == null)
                {
                    continue;
                }

                ApplyChannelRange(channel, i);
                _valuesRow.Add(channel);
            }

            _valuesRow.RefreshPositions();
            RefreshChannelFields();
        }

        void ApplyChannelRange(NumberInput channel, int index)
        {
            bool hsv = _colorSpace == COLOR_SPACE_HSV;

            if (index == 3)
            {
                channel.Min = 0.0;
                channel.Max = 100.0;
                channel.Suffix = "%";
                return;
            }

            if (!hsv)
            {
                channel.Min = 0.0;
                channel.Max = 255.0;
                channel.Suffix = string.Empty;
                return;
            }

            if (index == 0)
            {
                channel.Min = 0.0;
                channel.Max = HUE_RANGE;
                channel.Suffix = "°";
                return;
            }

            channel.Min = 0.0;
            channel.Max = 100.0;
            channel.Suffix = "%";
        }

        void RebuildPresetButtons()
        {
            if (_presetsRow == null)
            {
                return;
            }

            for (int i = _presetButtons.Count - 1; i >= _presets.Length; i--)
            {
                _presetsRow.Remove(_presetButtons[i]);
                _presetButtons.RemoveAt(i);
            }

            while (_presetButtons.Count < _presets.Length)
            {
                VisualElement button = new VisualElement { name = "tweeq-color-preset" };
                button.style.overflow = Overflow.Hidden;
                button.generateVisualContent += OnGeneratePreset;

                // 行ごとにコールバックを持たず、押された要素の index を親側で引く（RadioInput と同じ手）
                button.RegisterCallback<PointerDownEvent>(OnPresetPointerDown);
                _presetsRow.Add(button);
                _presetButtons.Add(button);
            }

            ApplyPresetStyles();
        }

        void ApplyPresetStyles()
        {
            if (_theme == null || _presetsRow == null)
            {
                return;
            }

            float size = _theme.InputHeight;
            float gap = _theme.RelatedGap;
            int count = _presetButtons.Count;
            int lastRow = count == 0 ? 0 : (count - 1) / PRESETS_PER_ROW;

            for (int i = 0; i < count; i++)
            {
                VisualElement button = _presetButtons[i];
                button.style.width = size;
                button.style.height = size;
                button.style.flexShrink = 0f;

                // gap が無いのでマージンで代替する。行末・最終行のマージンは落として、
                // ポップアップの padding に余分な余白を足さない
                button.style.marginRight = (i + 1) % PRESETS_PER_ROW == 0 ? 0f : gap;
                button.style.marginBottom = i / PRESETS_PER_ROW == lastRow ? 0f : gap;
                SetCornerRadius(button, _theme.InputRadius, true, true, true, true);
                button.MarkDirtyRepaint();
            }

            if (_valuesRow != null)
            {
                _valuesRow.style.marginBottom = count > 0 ? _theme.GapControl : 0f;
            }
        }

        #endregion

        #region Picker presentation

        void ShowPicker()
        {
            if (this.panel == null || _theme == null)
            {
                // panel 未接続では置き場所が無い。論理状態だけ進めて例外は出さない
                return;
            }

            EnsurePickerElements();

            if (_popover == null)
            {
                // 外装（Surface・border・padding・影）は popover 側に任せる。
                // Escape・外側クリックで閉じるのも popover の LightDismiss に任せる（仕様 §ColorInput）
                _popover = new TweeqPopover
                {
                    Context = this,
                    Theme = _theme,
                    Arrow = false,
                    Chrome = true,
                    Placement = Tweeq.Core.PopoverPlacement.BottomStart,
                };
                _popover.Closed += OnPopoverClosed;
                _popover.Add(_picker);
            }

            _popover.Theme = _theme;
            _popover.Open(_swatch);
        }

        void OnPopoverClosed()
        {
            if (!_open)
            {
                return;
            }

            _open = false;

            // light dismiss はスウォッチ押下でも走る。同一フレームの再オープンだけ抑止する
            _suppressReopen = true;

            if (this.panel != null)
            {
                if (_reopenGuardItem == null)
                {
                    _reopenGuardItem = this.schedule.Execute(ClearReopenGuard);
                }

                _reopenGuardItem.ExecuteLater(0L);
            }
            else
            {
                _suppressReopen = false;
            }

            Refresh();
        }

        void ClearReopenGuard()
        {
            _suppressReopen = false;
        }

        #endregion

        #region Value

        // ピッカー由来の更新。HSVA を権威にしたまま Color を作り直す（hue を失わない）
        void ApplyHsva(HSVA hsva)
        {
            _hsva = hsva;

            Color next = ToColor(hsva);
            if (SameColor(next, _value))
            {
                RefreshPicker();
                return;
            }

            Color previous = _value;
            _value = next;
            _hexDirty = true;
            Refresh();
            ValueChanged?.Invoke(_value);
            NotifyValueChanged(previous, _value);
        }

        // 黒（v=0）と無彩色（s=0）では hue / 彩度が定義できない。
        // Vue の setHSVAChannel が NaN を旧値で埋めるのと同じく、直前の値を引き継ぐ
        static HSVA DeriveHsva(Color color, HSVA previous)
        {
            HSVA next = ToHsva(color);

            if (next.V <= 0.0)
            {
                return new HSVA(previous.H, previous.S, next.V, next.A);
            }

            if (next.S <= 0.0)
            {
                return new HSVA(previous.H, next.S, next.V, next.A);
            }

            return next;
        }

        void NotifyValueChanged(Color previous, Color current)
        {
            if (this.panel == null)
            {
                return;
            }

            using (ChangeEvent<Color> changeEvent = ChangeEvent<Color>.GetPooled(previous, current))
            {
                changeEvent.target = this;
                this.SendEvent(changeEvent);
            }
        }

        void EnsureHexText()
        {
            if (!_hexDirty)
            {
                return;
            }

            _hexText = FormatHex(_value);
            _hexDirty = false;
        }

        #endregion

        #region Field interaction

        // ピッカーのトグルは PointerUp まで持ち越す。3px 動いたらチャンネルスクラブへ分岐し、
        // 動かなければ従来どおりのトグルになる（仕様 §A）
        void OnSwatchPointerDown(PointerDownEvent evt)
        {
            if (evt == null || evt.button != 0 || _disabled || _swatchPressed)
            {
                return;
            }

            if (this.panel != null)
            {
                this.Focus();
            }

            _swatchPressed = true;
            _swatchPointerId = evt.pointerId;
            _pressPanelPosition = PanelPosition(evt);
            _openOnPress = _open || _suppressReopen;
            _scrubThreshold = evt.pointerType == UnityEngine.UIElements.PointerType.mouse
                ? MOUSE_DRAG_THRESHOLD
                : TOUCH_DRAG_THRESHOLD;

            _shiftHeld = (evt.modifiers & EventModifiers.Shift) != 0;
            _altHeld = (evt.modifiers & EventModifiers.Alt) != 0;

            // 押す前から S や Shift を握っていた場合はそのモードで掴む（Vue の tweakMode は computed）
            _scrubMode = ResolveScrubMode();

            if (this.panel != null && _swatch != null)
            {
                _swatch.CapturePointer(_swatchPointerId);
            }

            evt.StopPropagation();
        }

        void OnSwatchPointerMove(PointerMoveEvent evt)
        {
            if (evt == null || !_swatchPressed || evt.pointerId != _swatchPointerId || _disabled)
            {
                return;
            }

            // ドラッグ中の修飾キーはキーイベントより pointermove の方が取りこぼしが無い
            _shiftHeld = (evt.modifiers & EventModifiers.Shift) != 0;
            _altHeld = (evt.modifiers & EventModifiers.Alt) != 0;

            Vector2 position = PanelPosition(evt);

            if (!_scrubbing)
            {
                if (Vector2.Distance(position, _pressPanelPosition) < _scrubThreshold)
                {
                    return;
                }

                BeginChannelScrub(_pressPanelPosition);
            }

            SetScrubMode(ResolveScrubMode());
            UpdateChannelScrub(position);
            evt.StopPropagation();
        }

        void OnSwatchPointerUp(PointerUpEvent evt)
        {
            if (evt == null || !_swatchPressed || evt.pointerId != _swatchPointerId)
            {
                return;
            }

            bool wasScrubbing = _scrubbing;
            bool wasOpen = _openOnPress;
            int pointerId = _swatchPointerId;

            _swatchPressed = false;
            _swatchPointerId = PointerId.invalidPointerId;

            // 解放で PointerCaptureOut が走る。そこで確定済みなら _scrubbing は落ちている
            ReleaseSwatchPointer(pointerId);

            if (_scrubbing)
            {
                EndChannelScrub();
            }
            else if (!wasScrubbing)
            {
                // 閾値未満はクリック。開閉は押した時点の状態で決める
                if (wasOpen)
                {
                    ClosePicker();
                }
                else
                {
                    OpenPicker();
                }
            }

            evt.StopPropagation();
        }

        // 掴みが外れた＝そこで操作が終わったとみなす（巻き戻しは Escape だけの責務）。
        // カーソルとオーバーレイを取り残さないよう、確定経路もここに集約する
        void OnSwatchPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            _swatchPressed = false;
            _swatchPointerId = PointerId.invalidPointerId;

            if (_scrubbing)
            {
                EndChannelScrub();
            }
        }

        void ReleaseSwatchPointer(int pointerId)
        {
            if (this.panel == null || _swatch == null || pointerId == PointerId.invalidPointerId)
            {
                return;
            }

            if (_swatch.HasPointerCapture(pointerId))
            {
                _swatch.ReleasePointer(pointerId);
            }
        }

        // オーバーレイはパネル座標で描くので、変換しない生の位置を使う
        static Vector2 PanelPosition(IPointerEvent evt)
        {
            Vector3 position = evt.position;
            return new Vector2(position.x, position.y);
        }

        void OnSwatchPointerEnter(PointerEnterEvent evt)
        {
            _hovered = true;
            Refresh();
        }

        void OnSwatchPointerLeave(PointerLeaveEvent evt)
        {
            _hovered = false;
            Refresh();
        }

        void OnKeyDown(KeyDownEvent evt)
        {
            if (evt == null || _disabled)
            {
                return;
            }

            _shiftHeld = (evt.modifiers & EventModifiers.Shift) != 0;
            _altHeld = (evt.modifiers & EventModifiers.Alt) != 0;
            bool modeKey = SetModeKey(evt.keyCode, true);

            switch (evt.keyCode)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                case KeyCode.Space:
                    if (!_scrubbing)
                    {
                        TogglePicker();
                        evt.StopPropagation();
                    }

                    return;

                case KeyCode.Escape:
                    // 操作中は開始値へ戻す。そうでなければ閉じるだけ（色は巻き戻さない）
                    if (_scrubbing)
                    {
                        CancelChannelScrub();
                        evt.StopPropagation();
                    }
                    else if (_dragAxis != ColorPickerAxis.None)
                    {
                        CancelPickerDrag();
                        evt.StopPropagation();
                    }
                    else if (_open)
                    {
                        ClosePicker();
                        evt.StopPropagation();
                    }

                    return;
            }

            // Shift / Alt 単体でもモードは変わるので、キーイベントごとに毎回引き直す（仕様 §A）
            SetScrubMode(ResolveScrubMode());

            if (_scrubbing && modeKey)
            {
                evt.StopPropagation();
            }
        }

        void OnKeyUp(KeyUpEvent evt)
        {
            if (evt == null || _disabled)
            {
                return;
            }

            _shiftHeld = (evt.modifiers & EventModifiers.Shift) != 0;
            _altHeld = (evt.modifiers & EventModifiers.Alt) != 0;
            bool modeKey = SetModeKey(evt.keyCode, false);

            SetScrubMode(ResolveScrubMode());

            if (_scrubbing && modeKey)
            {
                evt.StopPropagation();
            }
        }

        // 押下状態を更新し、tweakMode に関わるキーだったかを返す
        bool SetModeKey(KeyCode keyCode, bool held)
        {
            switch (keyCode)
            {
                case KeyCode.H:
                    _hueKeyHeld = held;
                    return true;

                case KeyCode.F:
                    _fillKeyHeld = held;
                    return true;

                case KeyCode.S:
                    _satKeyHeld = held;
                    return true;

                case KeyCode.V:
                    _valKeyHeld = held;
                    return true;

                case KeyCode.R:
                    _redKeyHeld = held;
                    return true;

                case KeyCode.G:
                    _greenKeyHeld = held;
                    return true;

                case KeyCode.B:
                    _blueKeyHeld = held;
                    return true;

                case KeyCode.A:
                    _alphaKeyHeld = held;
                    return true;
            }

            return false;
        }

        void ClearModeKeys()
        {
            _shiftHeld = false;
            _altHeld = false;
            _hueKeyHeld = false;
            _fillKeyHeld = false;
            _satKeyHeld = false;
            _valKeyHeld = false;
            _redKeyHeld = false;
            _greenKeyHeld = false;
            _blueKeyHeld = false;
            _alphaKeyHeld = false;
        }

        // 優先順は Vue の tweakMode（computed）そのまま。同時押しは上から順に勝つ
        ColorTweakMode ResolveScrubMode()
        {
            if (_shiftHeld || _hueKeyHeld || _fillKeyHeld)
            {
                return ColorTweakMode.Hue;
            }

            if (_satKeyHeld)
            {
                return ColorTweakMode.Saturation;
            }

            if (_valKeyHeld)
            {
                return ColorTweakMode.Value;
            }

            if (_redKeyHeld)
            {
                return ColorTweakMode.Red;
            }

            if (_greenKeyHeld)
            {
                return ColorTweakMode.Green;
            }

            if (_blueKeyHeld)
            {
                return ColorTweakMode.Blue;
            }

            if (_altHeld || _alphaKeyHeld)
            {
                return ColorTweakMode.Alpha;
            }

            return ColorTweakMode.Pad;
        }

        void OnFocusIn(FocusInEvent evt)
        {
            _focused = true;
            Refresh();
        }

        void OnFocusOut(FocusOutEvent evt)
        {
            // ピッカー内のフィールドへフォーカスが移るだけでも来る。閉じるのは
            // 外側クリック・Escape・detach の 3 経路に任せる（DropdownInput と同じ判断）
            _focused = false;

            // フォーカスが外れると KeyUp が届かなくなる。押しっぱなし扱いを残さない
            ClearModeKeys();
            SetScrubMode(ResolveScrubMode());

            Refresh();
        }

        void OnAttachToPanel(AttachToPanelEvent evt)
        {
            // detach で捨てた SV テクスチャを焼き直させる（次の Refresh で作られる）
            Refresh();
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            CancelPickerDrag();

            // 付け替えは操作の中断であって取り消しでも確定でもない。
            // ただしカーソルとオーバーレイだけは必ず戻す（RotaryInput と同じ判断）
            StopScrub();
            ClosePicker();

            _swatchPressed = false;
            _swatchPointerId = PointerId.invalidPointerId;
            ClearModeKeys();

            _hovered = false;
            _focused = false;
            _suppressReopen = false;

            // SV グラデーションは要素ごとの持ち物。付け替えの間ぶら下げ続けない
            // （再接続時は hue のキャッシュが外れているので次に開いた時に焼き直される）
            DestroyTexture(_svTexture);
            _svTexture = null;
            _svTextureHue = double.NaN;
        }

        #endregion

        #region Picker interaction

        void OnSvPadGeometryChanged(GeometryChangedEvent evt)
        {
            if (_svPad == null)
            {
                return;
            }

            // aspect-ratio が無いので、幅が決まったら同じ値を高さへ写す。
            // 書き戻しでこのイベントが再入するため、収束したら何もしない
            float width = _svPad.layout.width;
            if (float.IsNaN(width) || width <= 0f || Mathf.Approximately(_svPad.layout.height, width))
            {
                return;
            }

            _svPad.style.height = width;
        }

        void OnPickerPointerDown(PointerDownEvent evt)
        {
            if (evt == null || evt.button != 0 || _disabled)
            {
                return;
            }

            VisualElement target = ResolveDragElement(evt.target);
            ColorPickerAxis axis = AxisOf(target);
            if (axis == ColorPickerAxis.None)
            {
                return;
            }

            _dragPointerId = evt.pointerId;
            BeginPickerDrag(axis);
            ApplyPointer(target, evt.position);

            if (this.panel != null)
            {
                target.CapturePointer(_dragPointerId);
            }

            evt.StopPropagation();
        }

        void OnPickerPointerMove(PointerMoveEvent evt)
        {
            if (evt == null
                || _dragAxis == ColorPickerAxis.None
                || evt.pointerId != _dragPointerId)
            {
                return;
            }

            ApplyPointer(ResolveDragElement(evt.currentTarget), evt.position);
            evt.StopPropagation();
        }

        void OnPickerPointerUp(PointerUpEvent evt)
        {
            if (evt == null
                || _dragAxis == ColorPickerAxis.None
                || evt.pointerId != _dragPointerId)
            {
                return;
            }

            VisualElement target = ResolveDragElement(evt.currentTarget);
            if (this.panel != null && target != null && target.HasPointerCapture(_dragPointerId))
            {
                target.ReleasePointer(_dragPointerId);
            }

            _dragPointerId = PointerId.invalidPointerId;
            EndPickerDrag();
            evt.StopPropagation();
        }

        void OnPickerPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            if (_dragAxis == ColorPickerAxis.None)
            {
                return;
            }

            // 掴みが外れた＝そこで操作が終わったとみなす。巻き戻しは Escape だけの責務
            _dragPointerId = PointerId.invalidPointerId;
            EndPickerDrag();
        }

        // currentTarget はカーソル用オーバーレイになり得ないが（pickingMode=Ignore）、
        // target 側は将来 pickable な子が増えても壊れないよう親を辿って解決する
        VisualElement ResolveDragElement(IEventHandler handler)
        {
            VisualElement element = handler as VisualElement;

            while (element != null)
            {
                if (element == _svPad || element == _hueBar || element == _alphaBar)
                {
                    return element;
                }

                element = element.hierarchy.parent;
            }

            return null;
        }

        ColorPickerAxis AxisOf(VisualElement element)
        {
            if (element == null)
            {
                return ColorPickerAxis.None;
            }

            if (element == _svPad)
            {
                return ColorPickerAxis.SaturationValue;
            }

            if (element == _hueBar)
            {
                return ColorPickerAxis.Hue;
            }

            return element == _alphaBar ? ColorPickerAxis.Alpha : ColorPickerAxis.None;
        }

        void ApplyPointer(VisualElement element, Vector3 worldPosition)
        {
            if (element == null)
            {
                return;
            }

            Rect rect = element.contentRect;
            if (float.IsNaN(rect.width) || float.IsNaN(rect.height)
                || rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            Vector2 local = element.WorldToLocal(new Vector2(worldPosition.x, worldPosition.y));
            UpdatePickerDrag(local.x / rect.width, local.y / rect.height);
        }

        void OnPresetPointerDown(PointerDownEvent evt)
        {
            if (evt == null || evt.button != 0 || _disabled)
            {
                return;
            }

            int index = _presetButtons.IndexOf(evt.currentTarget as VisualElement);
            if (index < 0)
            {
                return;
            }

            PerformPresetClick(index);
            evt.StopPropagation();
        }

        void OnColorSpaceChanged(string space)
        {
            this.ColorSpace = space;
        }

        void OnChannel0Changed(ChangeEvent<float> evt)
        {
            ApplyChannel(0, evt);
        }

        void OnChannel1Changed(ChangeEvent<float> evt)
        {
            ApplyChannel(1, evt);
        }

        void OnChannel2Changed(ChangeEvent<float> evt)
        {
            ApplyChannel(2, evt);
        }

        void OnChannel3Changed(ChangeEvent<float> evt)
        {
            ApplyChannel(3, evt);
        }

        void ApplyChannel(int index, ChangeEvent<float> evt)
        {
            if (evt == null || _disabled)
            {
                return;
            }

            // 自分で書き戻した値は SetValueWithoutNotify なのでここへは来ない。
            // 念のため target を確認して、入れ子のフィールドからの又聞きは弾く
            if (!ReferenceEquals(evt.target, _channels[index]))
            {
                return;
            }

            double raw = evt.newValue;

            if (index == 3)
            {
                ApplyHsva(new HSVA(_hsva.H, _hsva.S, _hsva.V, Clamp01(raw / 100.0)));
                return;
            }

            if (_colorSpace == COLOR_SPACE_HSV)
            {
                switch (index)
                {
                    case 0:
                        ApplyHsva(new HSVA(WrapHue(raw), _hsva.S, _hsva.V, _hsva.A));
                        break;

                    case 1:
                        ApplyHsva(new HSVA(_hsva.H, Clamp01(raw / 100.0), _hsva.V, _hsva.A));
                        break;

                    default:
                        ApplyHsva(new HSVA(_hsva.H, _hsva.S, Clamp01(raw / 100.0), _hsva.A));
                        break;
                }

                return;
            }

            // RGB は Color 側を書き換えてから HSVA を引き直す。
            // 無彩色になった時に hue を失わないよう、旧 HSVA を引き継ぐ経路を通す
            Color next = _value;
            float channel = (float)Clamp01(raw / 255.0);

            switch (index)
            {
                case 0:
                    next.r = channel;
                    break;

                case 1:
                    next.g = channel;
                    break;

                default:
                    next.b = channel;
                    break;
            }

            ApplyHsva(DeriveHsva(next, _hsva));
        }

        // ピッカー内フィールドの確定はそのまま ColorInput の確定として外へ出す
        // （引数のチャンネル値ではなく、合成後の色を渡す）
        void OnChildConfirmed(float channelValue)
        {
            Confirmed?.Invoke(_value);
        }

        void OnHexFieldChanged(string text)
        {
            PerformHexInput(text);
        }

        void OnHexFieldConfirmed(string text)
        {
            PerformHexConfirm();
        }

        #endregion

        #region Channel scrub

        // 基準 HSVA ＋ 基準位置からの移動量で値を決める。累積 delta を積まないので、
        // モード切替で基準を取り直すだけで値が飛ばない（仕様 §A の再キャプチャ）
        void ApplyScrub()
        {
            float width = TweakWidth();

            // 正規化: 右が正・上が正（仕様 §A のマッピング）
            double dx = (_scrubPointer.x - _scrubAnchor.x) / width;
            double dy = -(_scrubPointer.y - _scrubAnchor.y) / width;

            switch (_scrubMode)
            {
                case ColorTweakMode.Hue:
                    ApplyHsva(new HSVA(
                        WrapHue(_scrubBase.H + dx * HUE_RANGE), _scrubBase.S, _scrubBase.V, _scrubBase.A));
                    break;

                case ColorTweakMode.Saturation:
                    ApplyHsva(new HSVA(
                        _scrubBase.H, Clamp01(_scrubBase.S + dx), _scrubBase.V, _scrubBase.A));
                    break;

                case ColorTweakMode.Value:
                    ApplyHsva(new HSVA(
                        _scrubBase.H, _scrubBase.S, Clamp01(_scrubBase.V + dy), _scrubBase.A));
                    break;

                case ColorTweakMode.Alpha:
                    ApplyHsva(new HSVA(
                        _scrubBase.H, _scrubBase.S, _scrubBase.V, Clamp01(_scrubBase.A + dx)));
                    break;

                case ColorTweakMode.Red:
                case ColorTweakMode.Green:
                case ColorTweakMode.Blue:
                    ApplyRgbScrub(dx);
                    break;

                default:
                    ApplyHsva(new HSVA(
                        _scrubBase.H,
                        Clamp01(_scrubBase.S + dx),
                        Clamp01(_scrubBase.V + dy),
                        _scrubBase.A));
                    break;
            }
        }

        void ApplyRgbScrub(double dx)
        {
            CoreRgba rgba = TweeqColorLogic.HsvaToRgba(_scrubBase);

            switch (_scrubMode)
            {
                case ColorTweakMode.Red:
                    rgba.R = Clamp01(rgba.R + dx);
                    break;

                case ColorTweakMode.Green:
                    rgba.G = Clamp01(rgba.G + dx);
                    break;

                default:
                    rgba.B = Clamp01(rgba.B + dx);
                    break;
            }

            Color next = new Color((float)rgba.R, (float)rgba.G, (float)rgba.B, (float)rgba.A);

            // 無彩色・黒へ落ちても hue を失わないよう、現在の HSVA を引き継ぐ経路を通す
            ApplyHsva(DeriveHsva(next, _hsva));
        }

        void StopScrub()
        {
            if (!_scrubbing)
            {
                return;
            }

            _scrubbing = false;
            RestoreCursor();
            ReleaseScrubOverlay();
        }

        // 感度基準は tweakWidth = PopupWidth = 240（仕様 §A）
        float TweakWidth()
        {
            float width = _theme != null ? _theme.PopupWidth : TWEAK_WIDTH_FALLBACK;
            return width > 0f && !float.IsNaN(width) ? width : TWEAK_WIDTH_FALLBACK;
        }

        void HideCursor()
        {
            // panel が無い＝EditMode テストなどの論理層だけの実行。OS カーソルには触らない
            if (_cursorHidden || this.panel == null)
            {
                return;
            }

            _cursorHidden = true;
            UnityEngine.Cursor.visible = false;
        }

        void RestoreCursor()
        {
            if (!_cursorHidden)
            {
                return;
            }

            _cursorHidden = false;
            UnityEngine.Cursor.visible = true;
        }

        void AcquireScrubOverlay()
        {
            if (_scrubOverlay != null)
            {
                return;
            }

            TweeqOverlayLayer layer = TweeqOverlayLayer.GetOrCreate(this);
            if (layer == null)
            {
                // パネル未接続ならガイドは諦める（操作自体は成立させる）
                return;
            }

            _scrubOverlay = new ColorTweakOverlay();
            layer.Add(_scrubOverlay);
        }

        void ReleaseScrubOverlay()
        {
            if (_scrubOverlay == null)
            {
                return;
            }

            _scrubOverlay.RemoveFromHierarchy();
            _scrubOverlay = null;
        }

        void UpdateScrubOverlay()
        {
            if (_scrubOverlay == null)
            {
                return;
            }

            if (!_scrubbing || _theme == null)
            {
                ReleaseScrubOverlay();
                return;
            }

            // SV 面が要るのは pad モードだけ。ピッカーを一度も開いていなくても焼けるようにしてある
            Texture2D svTexture = null;
            if (_scrubMode == ColorTweakMode.Pad)
            {
                RebuildSvTextureIfNeeded();
                svTexture = _svTexture;
            }

            ColorTweakOverlayState state = new ColorTweakOverlayState
            {
                Theme = _theme,
                Origin = _scrubOrigin,
                Mode = _scrubMode,
                Hsva = _hsva,
                Value = _value,
                TweakWidth = TweakWidth(),
                SvTexture = svTexture,
            };

            _scrubOverlay.Sync(in state);
        }

        #endregion

        #region Refresh

        void Refresh()
        {
            if (_swatch != null)
            {
                _swatch.MarkDirtyRepaint();
            }

            RefreshPicker();
            UpdateScrubOverlay();
        }

        void RefreshPicker()
        {
            if (_picker == null)
            {
                return;
            }

            // 閉じている間の書き戻しは目に見えないのに、数値フィールドの文字列生成だけは走る。
            // チャンネルスクラブは「一度開いたことがあるピッカー」を閉じたまま回すので、
            // ここで止めないと 1 ムーブごとに 4 本ぶんの文字列を確保してしまう。
            // 開くときは OpenPicker が Refresh を呼ぶので、表示の追従は失われない
            if (!_open)
            {
                return;
            }

            RebuildSvTextureIfNeeded();

            _svCursor?.MarkDirtyRepaint();
            _hueCursor?.MarkDirtyRepaint();
            _alphaGradient?.MarkDirtyRepaint();
            _alphaCursor?.MarkDirtyRepaint();

            RefreshChannelFields();
            RefreshHexField(false);
        }

        // 表示中の space のフィールドだけ書き戻す。
        // ドラッグ中に隠れている行を触らないことが、そのまま文字列生成の削減になる
        void RefreshChannelFields()
        {
            if (_colorSpace == COLOR_SPACE_HEX || _channels[0] == null)
            {
                return;
            }

            if (_colorSpace == COLOR_SPACE_HSV)
            {
                _channels[0].SetValueWithoutNotify((float)_hsva.H);
                _channels[1].SetValueWithoutNotify((float)(_hsva.S * 100.0));
                _channels[2].SetValueWithoutNotify((float)(_hsva.V * 100.0));
            }
            else
            {
                _channels[0].SetValueWithoutNotify(_value.r * 255f);
                _channels[1].SetValueWithoutNotify(_value.g * 255f);
                _channels[2].SetValueWithoutNotify(_value.b * 255f);
            }

            _channels[3].SetValueWithoutNotify((float)(_hsva.A * 100.0));
        }

        // force=false のときは「HEX 行が見えていて、かつ値が実際に変わった」ときだけ組み立てる
        void RefreshHexField(bool force)
        {
            if (_hexField == null)
            {
                return;
            }

            if (!force && (_syncingHex || _colorSpace != COLOR_SPACE_HEX || !_hexDirty))
            {
                return;
            }

            EnsureHexText();

            if (_hexField.value != _hexText)
            {
                _hexField.SetValueWithoutNotify(_hexText);
            }
        }

        #endregion

        #region Textures

        void RebuildSvTextureIfNeeded()
        {
            // 焼く相手はピッカーの SV パッドか、スクラブ中のオーバーレイ。
            // どちらも無いときにテクスチャを確保しない（panel 非依存の論理層を汚さないため）
            if (_svPad == null && _scrubOverlay == null)
            {
                return;
            }

            if (_svTexture != null && _svTextureHue == _hsva.H)
            {
                return;
            }

            EnsureSvTexture();

            int size = SV_TEXTURE_SIZE;
            double hue = _hsva.H;
            double denominator = size - 1;

            for (int y = 0; y < size; y++)
            {
                // Texture2D の行 0 は下端。v=0（黒）を下に置く
                double v = y / denominator;
                int rowOffset = y * size;

                for (int x = 0; x < size; x++)
                {
                    double s = x / denominator;
                    CoreRgba rgba = TweeqColorLogic.HsvaToRgba(new HSVA(hue, s, v, 1.0));

                    _svPixels[rowOffset + x] = new Color32(
                        ToByte(rgba.R), ToByte(rgba.G), ToByte(rgba.B), ToByte(rgba.A));
                }
            }

            _svTexture.SetPixels32(_svPixels);
            _svTexture.Apply(false);
            _svTextureHue = hue;

            if (_svPad != null)
            {
                _svPad.style.backgroundImage = new StyleBackground(_svTexture);
            }
        }

        void EnsureSvTexture()
        {
            if (_svPixels == null)
            {
                _svPixels = new Color32[SV_TEXTURE_SIZE * SV_TEXTURE_SIZE];
            }

            if (_svTexture != null)
            {
                return;
            }

            _svTexture = new Texture2D(SV_TEXTURE_SIZE, SV_TEXTURE_SIZE, TextureFormat.RGBA32, false)
            {
                name = "tweeq-color-sv",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
        }

        static Texture2D GetHueTexture()
        {
            if (SharedHueTexture != null)
            {
                return SharedHueTexture;
            }

            SharedHueTexture = new Texture2D(HUE_TEXTURE_WIDTH, 1, TextureFormat.RGBA32, false)
            {
                name = "tweeq-color-hue",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            Color32[] pixels = new Color32[HUE_TEXTURE_WIDTH];
            double denominator = HUE_TEXTURE_WIDTH - 1;

            for (int x = 0; x < HUE_TEXTURE_WIDTH; x++)
            {
                CoreRgba rgba = TweeqColorLogic.HsvaToRgba(
                    new HSVA(x / denominator * HUE_RANGE, 1.0, 1.0, 1.0));

                pixels[x] = new Color32(ToByte(rgba.R), ToByte(rgba.G), ToByte(rgba.B), ToByte(rgba.A));
            }

            SharedHueTexture.SetPixels32(pixels);
            SharedHueTexture.Apply(false);
            return SharedHueTexture;
        }

        static void DestroyTexture(Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(texture);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        #endregion

        #region Painting

        void OnGenerateSwatch(MeshGenerationContext context)
        {
            if (context == null || _theme == null || _swatch == null)
            {
                return;
            }

            Painter2D painter = context.painter2D;
            if (painter == null)
            {
                return;
            }

            Rect rect = _swatch.contentRect;
            if (!IsUsableRect(rect))
            {
                return;
            }

            PaintCheckerboard(painter, rect.width, rect.height);

            painter.fillColor = _value;
            FillRect(painter, 0f, 0f, rect.width, rect.height);

            // 背景に沈む色でも輪郭が読めるように、常に 1px の枠を置く。
            // hover / focus / 開いている間はアクセントへ切り替える（仕様 §ColorInput）
            painter.strokeColor = _hovered || _focused || _open ? _theme.Accent : _theme.Border;
            painter.lineWidth = FIELD_OUTLINE_WIDTH;

            float inset = FIELD_OUTLINE_WIDTH * 0.5f;
            painter.BeginPath();
            painter.MoveTo(new Vector2(inset, inset));
            painter.LineTo(new Vector2(rect.width - inset, inset));
            painter.LineTo(new Vector2(rect.width - inset, rect.height - inset));
            painter.LineTo(new Vector2(inset, rect.height - inset));
            painter.ClosePath();
            painter.Stroke();
        }

        void OnGeneratePreset(MeshGenerationContext context)
        {
            if (context == null || context.visualElement == null)
            {
                return;
            }

            Painter2D painter = context.painter2D;
            if (painter == null)
            {
                return;
            }

            int index = _presetButtons.IndexOf(context.visualElement);
            if (index < 0 || index >= _presets.Length)
            {
                return;
            }

            Rect rect = context.visualElement.contentRect;
            if (!IsUsableRect(rect))
            {
                return;
            }

            PaintCheckerboard(painter, rect.width, rect.height);

            painter.fillColor = _presets[index];
            FillRect(painter, 0f, 0f, rect.width, rect.height);
        }

        void OnGenerateSvCursor(MeshGenerationContext context)
        {
            if (context == null || _svCursor == null)
            {
                return;
            }

            Rect rect = _svCursor.contentRect;
            if (!IsUsableRect(rect))
            {
                return;
            }

            // カーソル中心は値の実位置に置く（入力マッピングと同一の全幅リニア）。
            // 内側へ畳むと端に近づくほど OS カーソルとズレるため、端では輪が
            // overflow:Hidden で半分欠ける方を取る（Web 系ピッカーと同じ見え方）
            float x = (float)_hsva.S * rect.width;
            float y = (float)(1.0 - _hsva.V) * rect.height;

            PaintCursor(context.painter2D, new Vector2(x, y), OpaqueValue());
        }

        void OnGenerateHueCursor(MeshGenerationContext context)
        {
            if (context == null || _hueCursor == null)
            {
                return;
            }

            Rect rect = _hueCursor.contentRect;
            if (!IsUsableRect(rect))
            {
                return;
            }

            float radius = Mathf.Min(CURSOR_RADIUS, rect.height * 0.5f);
            // 中心は値の実位置（SV カーソルと同じ判断。端では輪が欠ける）
            float x = (float)(_hsva.H / HUE_RANGE) * rect.width;

            CoreRgba rgba = TweeqColorLogic.HsvaToRgba(new HSVA(_hsva.H, 1.0, 1.0, 1.0));

            PaintCursor(
                context.painter2D,
                new Vector2(x, rect.height * 0.5f),
                new Color((float)rgba.R, (float)rgba.G, (float)rgba.B, (float)rgba.A),
                radius);
        }

        void OnGenerateAlphaCursor(MeshGenerationContext context)
        {
            if (context == null || _alphaCursor == null)
            {
                return;
            }

            Rect rect = _alphaCursor.contentRect;
            if (!IsUsableRect(rect))
            {
                return;
            }

            float radius = Mathf.Min(CURSOR_RADIUS, rect.height * 0.5f);
            // 中心は値の実位置（SV カーソルと同じ判断。端では輪が欠ける）
            float x = (float)_hsva.A * rect.width;

            PaintCursor(context.painter2D, new Vector2(x, rect.height * 0.5f), OpaqueValue(), radius);
        }

        void OnGenerateAlphaChecker(MeshGenerationContext context)
        {
            if (context == null || _alphaChecker == null)
            {
                return;
            }

            Painter2D painter = context.painter2D;
            if (painter == null)
            {
                return;
            }

            Rect rect = _alphaChecker.contentRect;
            if (!IsUsableRect(rect))
            {
                return;
            }

            PaintCheckerboard(painter, rect.width, rect.height);
        }

        // 透明→不透明のグラデーション。インラインスタイルにグラデーションが無いので、
        // 頂点カラーを 4 隅に置いて GPU 側で補間させる（帯を並べるより滑らかで確保も無い）
        void OnGenerateAlphaGradient(MeshGenerationContext context)
        {
            if (context == null || _alphaGradient == null)
            {
                return;
            }

            Rect rect = _alphaGradient.contentRect;
            if (!IsUsableRect(rect))
            {
                return;
            }

            Color opaque = OpaqueValue();
            Color transparent = opaque;
            transparent.a = 0f;

            MeshWriteData mesh = context.Allocate(4, 6);

            mesh.SetNextVertex(new Vertex
            {
                position = new Vector3(0f, 0f, Vertex.nearZ),
                tint = transparent,
            });
            mesh.SetNextVertex(new Vertex
            {
                position = new Vector3(rect.width, 0f, Vertex.nearZ),
                tint = opaque,
            });
            mesh.SetNextVertex(new Vertex
            {
                position = new Vector3(rect.width, rect.height, Vertex.nearZ),
                tint = opaque,
            });
            mesh.SetNextVertex(new Vertex
            {
                position = new Vector3(0f, rect.height, Vertex.nearZ),
                tint = transparent,
            });

            mesh.SetNextIndex(0);
            mesh.SetNextIndex(1);
            mesh.SetNextIndex(2);
            mesh.SetNextIndex(0);
            mesh.SetNextIndex(2);
            mesh.SetNextIndex(3);
        }

        void PaintCursor(Painter2D painter, Vector2 center, Color fill)
        {
            PaintCursor(painter, center, fill, CURSOR_RADIUS);
        }

        // Vue common.styl の circle(): 白い外周 + 内側の薄い暗色。塗りは「今の色」
        void PaintCursor(Painter2D painter, Vector2 center, Color fill, float radius)
        {
            if (painter == null || radius <= 0f)
            {
                return;
            }

            painter.fillColor = fill;
            painter.BeginPath();
            painter.Arc(center, radius, new Angle(0f, AngleUnit.Degree), new Angle(360f, AngleUnit.Degree));
            painter.ClosePath();
            painter.Fill();

            painter.strokeColor = CursorRing;
            painter.lineWidth = CURSOR_RING_WIDTH;
            painter.BeginPath();
            painter.Arc(
                center,
                radius + CURSOR_RING_WIDTH * 0.5f,
                new Angle(0f, AngleUnit.Degree),
                new Angle(360f, AngleUnit.Degree));
            painter.ClosePath();
            painter.Stroke();

            painter.strokeColor = CursorShade;
            painter.lineWidth = CURSOR_SHADE_WIDTH;
            painter.BeginPath();
            painter.Arc(
                center,
                Mathf.Max(0.5f, radius - CURSOR_SHADE_WIDTH * 0.5f),
                new Angle(0f, AngleUnit.Degree),
                new Angle(360f, AngleUnit.Degree));
            painter.ClosePath();
            painter.Stroke();
        }

        static void PaintCheckerboard(Painter2D painter, float width, float height)
        {
            painter.fillColor = CheckerLight;
            FillRect(painter, 0f, 0f, width, height);

            painter.fillColor = CheckerDark;

            int columns = Mathf.CeilToInt(width / CHECKER_CELL);
            int rows = Mathf.CeilToInt(height / CHECKER_CELL);

            for (int row = 0; row < rows; row++)
            {
                float y = row * CHECKER_CELL;
                float cellHeight = Mathf.Min(CHECKER_CELL, height - y);

                for (int column = (row & 1) == 0 ? 1 : 0; column < columns; column += 2)
                {
                    float x = column * CHECKER_CELL;
                    FillRect(painter, x, y, Mathf.Min(CHECKER_CELL, width - x), cellHeight);
                }
            }
        }

        static void FillRect(Painter2D painter, float x, float y, float width, float height)
        {
            if (width <= 0f || height <= 0f)
            {
                return;
            }

            painter.BeginPath();
            painter.MoveTo(new Vector2(x, y));
            painter.LineTo(new Vector2(x + width, y));
            painter.LineTo(new Vector2(x + width, y + height));
            painter.LineTo(new Vector2(x, y + height));
            painter.ClosePath();
            painter.Fill();
        }

        #endregion

        #region Color logic bridge

        // TweeqColorLogic は Core（noEngineReferences）側にあり UnityEngine.Color を知らない。
        // 変換の呼び出しはこの 4 つに閉じ込めてあるので、Core 側の署名が変わってもここだけ直せばよい
        static Color ToColor(HSVA hsva)
        {
            CoreRgba rgba = TweeqColorLogic.HsvaToRgba(hsva);
            return new Color((float)rgba.R, (float)rgba.G, (float)rgba.B, (float)rgba.A);
        }

        static HSVA ToHsva(Color color)
        {
            return TweeqColorLogic.RgbaToHsva(new CoreRgba(color.r, color.g, color.b, color.a));
        }

        static bool TryParseHex(string text, out Color color)
        {
            if (TweeqColorLogic.TryParseHex(text, out CoreRgba rgba))
            {
                color = new Color((float)rgba.R, (float)rgba.G, (float)rgba.B, (float)rgba.A);
                return true;
            }

            color = Color.clear;
            return false;
        }

        static string FormatHex(Color color)
        {
            return TweeqColorLogic.FormatHex(new CoreRgba(color.r, color.g, color.b, color.a));
        }

        #endregion

        #region Helpers

        // カーソルとグラデーションの「色」は α を抜いた現在色。α まで載せると
        // 透明なときにカーソルが消えて位置が読めなくなる
        Color OpaqueValue()
        {
            Color opaque = _value;
            opaque.a = 1f;
            return opaque;
        }

        static string ToUpperLabel(string space)
        {
            return space == null ? string.Empty : space.ToUpperInvariant();
        }

        static string NormalizeColorSpace(string space)
        {
            if (space == COLOR_SPACE_RGB || space == COLOR_SPACE_HSV || space == COLOR_SPACE_HEX)
            {
                return space;
            }

            return COLOR_SPACE_HSV;
        }

        // Color の == は近似比較なので、1/255 未満の変化を取りこぼす。成分ごとに厳密に比べる
        static bool SameColor(Color a, Color b)
        {
            return a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
        }

        static bool IsUsableRect(Rect rect)
        {
            return !float.IsNaN(rect.width)
                && !float.IsNaN(rect.height)
                && rect.width > 0f
                && rect.height > 0f;
        }

        static double Clamp01(double value)
        {
            if (double.IsNaN(value))
            {
                return 0.0;
            }

            return value < 0.0 ? 0.0 : value > 1.0 ? 1.0 : value;
        }

        // C# の % は負値で負を返すので符号を揃える
        static double WrapHue(double hue)
        {
            if (double.IsNaN(hue) || double.IsInfinity(hue))
            {
                return 0.0;
            }

            double wrapped = hue % HUE_RANGE;
            return wrapped < 0.0 ? wrapped + HUE_RANGE : wrapped;
        }

        static byte ToByte(double value)
        {
            double scaled = Math.Round(Clamp01(value) * 255.0, MidpointRounding.AwayFromZero);
            return (byte)scaled;
        }

        static void SetCornerRadius(
            VisualElement element,
            float radius,
            bool topLeft,
            bool topRight,
            bool bottomLeft,
            bool bottomRight)
        {
            element.style.borderTopLeftRadius = topLeft ? radius : 0f;
            element.style.borderTopRightRadius = topRight ? radius : 0f;
            element.style.borderBottomLeftRadius = bottomLeft ? radius : 0f;
            element.style.borderBottomRightRadius = bottomRight ? radius : 0f;
        }

        #endregion
    }
}
