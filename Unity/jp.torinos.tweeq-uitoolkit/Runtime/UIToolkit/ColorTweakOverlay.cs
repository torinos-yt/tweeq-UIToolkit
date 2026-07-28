using System;
using UnityEngine;
using UnityEngine.UIElements;

// ColorInput と同じ理由（Tweeq.Core を丸ごと引くと TweeqRect / TweeqVec2 が
// UnityEngine 側と紛らわしい）で、使う型だけ別名で引き込む
using HSVA = Tweeq.Core.Hsva;
using CoreRgba = Tweeq.Core.Rgba;
using TweeqColorLogic = Tweeq.Core.TweeqColorLogic;
using TweeqFormat = Tweeq.Core.TweeqFormat;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// <see cref="ColorTweakOverlay"/> へ渡す 1 フレーム分の描画パラメータ。
    /// 座標はすべてパネル座標（＝オーバーレイ層のローカル座標）。
    /// </summary>
    struct ColorTweakOverlayState
    {
        public TweeqTheme Theme;

        /// <summary>ドラッグ開始位置。プレビュー円とラベルの基準点。</summary>
        public Vector2 Origin;

        public ColorTweakMode Mode;

        /// <summary>現在の HSVA。pad のスライド量と各スライダーのグラデはここから引く。</summary>
        public HSVA Hsva;

        /// <summary>現在の色（α 込み）。</summary>
        public Color Value;

        /// <summary>感度基準にもなる描画幅（＝ PopupWidth = 240）。</summary>
        public float TweakWidth;

        /// <summary>pad モードで敷く SV グラデーション。null なら pad 面は出さない。</summary>
        public Texture2D SvTexture;
    }

    /// <summary>
    /// ColorInput のチャンネルスクラブ中だけ生きるオーバーレイ（m6-wave2-spec.md §A）。
    /// <see cref="TweeqOverlayLayer"/> へ直接ぶら下がり、パネル全面を覆って
    /// pad 面 / 色相リング / 単チャンネルスライダー / プレビュー円 / 値ラベルを描く。
    ///
    /// 描画順をヒエラルキーで保証するため 3 層に分けてある（UI Toolkit は
    /// 「自分の generateVisualContent → 子」の順に描くので、pad を子に置くと
    /// 親が描いたプレビュー円を覆ってしまう）。
    /// </summary>
    sealed class ColorTweakOverlay : VisualElement
    {
        #region Constants

        // プレビュー円の半径 21.6 = InputHeight(24) × 0.9（Vue の 24px 箱を scale 1.8 した円の半径）
        const float PREVIEW_RADIUS_FACTOR = 0.9f;
        const float PREVIEW_BORDER_WIDTH = 1f;

        // 色相リング: 直径 240（＝ TweakWidth）・線幅 4px・60 分割
        const int HUE_SEGMENTS = 60;
        const float HUE_RING_WIDTH = 4f;
        const int HUE_TICK_COUNT = 6;
        const float HUE_TICK_RADIUS = 1.8f;

        // 分割塗りの継ぎ目を消すための重ね幅（度 / px）
        const float SEGMENT_OVERLAP_DEGREES = 0.5f;
        const float SEGMENT_OVERLAP_PIXELS = 1f;

        // 単チャンネルスライダー: 240×12（val のみ 12×240 の縦）
        const float SLIDER_THICKNESS = 12f;
        const int SLIDER_SEGMENTS = 60;
        const float SLIDER_BORDER_WIDTH = 1f;

        // 現在位置マーカー。白い芯に薄い暗色の縁（ピッカーのカーソルと同じ考え方）
        const float MARKER_WIDTH = 3f;
        const float MARKER_SHADE_WIDTH = 1f;
        const float MARKER_OVERHANG = 2f;

        const float LABEL_FONT_SIZE = 10f;
        const float LABEL_PADDING_X = 6f;
        const float LABEL_PADDING_Y = 4f;
        const float LABEL_RADIUS = 4f;
        const float LABEL_BORDER_WIDTH = 1f;

        // ラベルは origin の上方 InputHeight×1.7 ＋ 自身の高さ / 2（Vue の translate 相当）
        const float LABEL_GAP_FACTOR = 1.7f;

        // 画面端クランプ（egui 準拠）
        const float LABEL_EDGE_MARGIN = 4f;

        // チェッカーボードの 1 マス。ColorInput 側と同じ 6px
        const float CHECKER_CELL = 6f;

        const double HUE_RANGE = 360.0;
        const double PERCENT_SCALE = 100.0;
        const double BYTE_SCALE = 255.0;

        // 表示キーの粒度。パーセント表示は F1 なので 0.1% = 値の 1/1000
        const double PERCENT_KEY_SCALE = 1000.0;
        const double HUE_KEY_SCALE = 10.0;

        #endregion

        #region Fields

        // Vue は white / #ddd 固定（テーマに追従しない）。ColorInput と同じ値
        static readonly Color CheckerLight = new Color32(0xFF, 0xFF, 0xFF, 0xFF);
        static readonly Color CheckerDark = new Color32(0xDD, 0xDD, 0xDD, 0xFF);

        static readonly Color MarkerCore = new Color(1f, 1f, 1f, 1f);
        static readonly Color MarkerShade = new Color(0f, 0f, 0f, 0.2f);

        // モノスペースの解決結果は 1 度引いたら全インスタンスで使い回す。
        // OS フォントは動的生成なので、参照も保持して破棄されないようにする
        static FontDefinition SharedMonospaceDefinition;
        static Font SharedOsMonospaceFont;
        static bool MonospaceResolved;

        ColorTweakOverlayState _state;
        bool _hasState;

        // フォント適用はテーマが差し替わったフレームだけにする（Sync はスクラブ中毎フレーム走る）
        TweeqTheme _fontTheme;

        VisualElement _pad;
        VisualElement _paint;
        Label _label;

        Texture2D _padTexture;

        // ラベルは表示が変わったときだけ組み直す。キーは表示の分解能で量子化した整数
        bool _hasLabelKey;
        ColorTweakMode _labelMode;
        long _labelKey0;
        long _labelKey1;

        #endregion

        #region Construction

        public ColorTweakOverlay()
        {
            this.name = "tweeq-color-tweak-overlay";
            this.pickingMode = PickingMode.Ignore;
            this.style.position = Position.Absolute;
            this.style.left = 0f;
            this.style.top = 0f;
            this.style.right = 0f;
            this.style.bottom = 0f;
            this.style.overflow = Overflow.Visible;

            BuildPad();
            BuildPaint();
            BuildLabel();
        }

        void BuildPad()
        {
            _pad = new VisualElement
            {
                name = "tweeq-color-tweak-pad",
                pickingMode = PickingMode.Ignore,
            };
            _pad.style.position = Position.Absolute;
            _pad.style.overflow = Overflow.Hidden;
            _pad.style.display = DisplayStyle.None;

            // background-size の既定は auto（＝ネイティブ解像度）。64×64 を 240px へ引き伸ばす
            _pad.style.backgroundSize =
                new BackgroundSize(Length.Percent(100f), Length.Percent(100f));
            _pad.style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
            _pad.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Left);
            _pad.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Top);

            this.Add(_pad);
        }

        void BuildPaint()
        {
            _paint = new VisualElement
            {
                name = "tweeq-color-tweak-paint",
                pickingMode = PickingMode.Ignore,
            };
            _paint.style.position = Position.Absolute;
            _paint.style.left = 0f;
            _paint.style.top = 0f;
            _paint.style.right = 0f;
            _paint.style.bottom = 0f;
            _paint.style.overflow = Overflow.Visible;
            _paint.generateVisualContent += OnGeneratePaint;
            this.Add(_paint);
        }

        void BuildLabel()
        {
            _label = new Label(string.Empty) { pickingMode = PickingMode.Ignore };
            _label.style.position = Position.Absolute;
            _label.style.fontSize = LABEL_FONT_SIZE;
            _label.style.unityTextAlign = TextAnchor.MiddleCenter;
            _label.style.paddingLeft = LABEL_PADDING_X;
            _label.style.paddingRight = LABEL_PADDING_X;
            _label.style.paddingTop = LABEL_PADDING_Y;
            _label.style.paddingBottom = LABEL_PADDING_Y;
            _label.style.marginLeft = 0f;
            _label.style.marginRight = 0f;
            _label.style.marginTop = 0f;
            _label.style.marginBottom = 0f;

            SetBorderWidth(_label, LABEL_BORDER_WIDTH);
            SetBorderRadius(_label, LABEL_RADIUS);

            // テーマが来る前でも桁が揺れないよう、既定の等幅を先に貼る
            TweeqFonts.Apply(_label, GetMonospaceFont());

            // 中心合わせは実解決サイズが要るので、確定した時点で置き直す（RotaryInput と同じ手）
            _label.RegisterCallback<GeometryChangedEvent>(OnLabelGeometryChanged);
            this.Add(_label);
        }

        // 可変幅フォントだと桁の増減でラベルが揺れる。第一候補は同梱の Geist Mono
        // （TweeqFonts.CodeFont）で、パッケージからフォントを外した構成でも等幅を保てるよう
        // OS 側のモノスペース検索をフォールバックに残す
        static FontDefinition GetMonospaceFont()
        {
            if (MonospaceResolved)
            {
                return SharedMonospaceDefinition;
            }

            MonospaceResolved = true;

            FontDefinition bundled = TweeqFonts.CodeFont;
            if (!TweeqFonts.IsEmpty(bundled))
            {
                SharedMonospaceDefinition = bundled;
                return SharedMonospaceDefinition;
            }

            SharedOsMonospaceFont = Font.CreateDynamicFontFromOSFont(
                new[] { "Consolas", "Menlo", "DejaVu Sans Mono", "Courier New" },
                Mathf.RoundToInt(LABEL_FONT_SIZE));

            if (SharedOsMonospaceFont != null)
            {
                SharedOsMonospaceFont.hideFlags = HideFlags.HideAndDontSave;
                SharedMonospaceDefinition = FontDefinition.FromFont(SharedOsMonospaceFont);
            }

            return SharedMonospaceDefinition;
        }

        #endregion

        #region Sync

        /// <summary>描画パラメータを更新する。Theme が null のフレームは何も描かない。</summary>
        public void Sync(in ColorTweakOverlayState state)
        {
            _state = state;
            _hasState = state.Theme != null && state.TweakWidth > 0f;

            if (!_hasState)
            {
                return;
            }

            SyncPad();
            SyncLabel();
            _paint.MarkDirtyRepaint();
        }

        void SyncPad()
        {
            bool visible = _state.Mode == ColorTweakMode.Pad && _state.SvTexture != null;
            _pad.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

            if (!visible)
            {
                return;
            }

            if (!ReferenceEquals(_padTexture, _state.SvTexture))
            {
                _padTexture = _state.SvTexture;
                _pad.style.backgroundImage = new StyleBackground(_padTexture);
            }

            float size = _state.TweakWidth;
            _pad.style.width = size;
            _pad.style.height = size;
            _pad.style.borderTopLeftRadius = _state.Theme.InputRadius;
            _pad.style.borderTopRightRadius = _state.Theme.InputRadius;
            _pad.style.borderBottomLeftRadius = _state.Theme.InputRadius;
            _pad.style.borderBottomRightRadius = _state.Theme.InputRadius;

            // 現在の S / V が常に origin に一致するようスライドさせる（Vue の padStyle 準拠）
            _pad.style.left = _state.Origin.x - (float)Clamp01(_state.Hsva.S) * size;
            _pad.style.top = _state.Origin.y - (float)(1.0 - Clamp01(_state.Hsva.V)) * size;
        }

        void SyncLabel()
        {
            TweeqTheme theme = _state.Theme;
            _label.style.backgroundColor = theme.SurfaceOpaque;
            _label.style.color = theme.Text;
            SetBorderColor(_label, theme.Border);

            if (!ReferenceEquals(_fontTheme, theme))
            {
                _fontTheme = theme;

                // FontCode が空（＝上書きしない指定）のテーマでも等幅は死守したいので、
                // その時だけ既定の等幅へ落とす
                FontDefinition font = TweeqFonts.IsEmpty(theme.FontCode)
                    ? GetMonospaceFont()
                    : theme.FontCode;

                TweeqFonts.Apply(_label, font);
            }

            SyncLabelText();
            UpdateLabelPosition();
        }

        // Sync はポインタが動かないフレームでも走るので、表示が変わるときだけ文字列を作る
        void SyncLabelText()
        {
            ComputeLabelKey(out long key0, out long key1);

            if (_hasLabelKey && _labelMode == _state.Mode && _labelKey0 == key0 && _labelKey1 == key1)
            {
                return;
            }

            _label.text = BuildLabelText();
            _hasLabelKey = true;
            _labelMode = _state.Mode;
            _labelKey0 = key0;
            _labelKey1 = key1;
        }

        void ComputeLabelKey(out long key0, out long key1)
        {
            key0 = 0L;
            key1 = 0L;

            switch (_state.Mode)
            {
                case ColorTweakMode.Pad:
                    key0 = PercentKey(_state.Hsva.S);
                    key1 = PercentKey(_state.Hsva.V);
                    break;

                case ColorTweakMode.Hue:
                    key0 = (long)Math.Round(_state.Hsva.H * HUE_KEY_SCALE, MidpointRounding.AwayFromZero);
                    break;

                case ColorTweakMode.Saturation:
                    key0 = PercentKey(_state.Hsva.S);
                    break;

                case ColorTweakMode.Value:
                    key0 = PercentKey(_state.Hsva.V);
                    break;

                case ColorTweakMode.Alpha:
                    key0 = PercentKey(_state.Hsva.A);
                    break;

                default:
                    key0 = (long)Math.Round(ChannelValue() * BYTE_SCALE, MidpointRounding.AwayFromZero);
                    break;
            }
        }

        static long PercentKey(double normalized)
        {
            return (long)Math.Round(Clamp01(normalized) * PERCENT_KEY_SCALE, MidpointRounding.AwayFromZero);
        }

        string BuildLabelText()
        {
            switch (_state.Mode)
            {
                case ColorTweakMode.Pad:
                    return "Sat " + Percent(_state.Hsva.S) + "%  Val " + Percent(_state.Hsva.V) + "%";

                case ColorTweakMode.Hue:
                    return "Hue " + TweeqFormat.Format(_state.Hsva.H, 1, true) + "°";

                case ColorTweakMode.Saturation:
                    return "Sat " + Percent(_state.Hsva.S) + "%";

                case ColorTweakMode.Value:
                    return "Val " + Percent(_state.Hsva.V) + "%";

                case ColorTweakMode.Alpha:
                    return "α " + Percent(_state.Hsva.A) + "%";

                case ColorTweakMode.Red:
                    return "R " + Byte255(ChannelValue());

                case ColorTweakMode.Green:
                    return "G " + Byte255(ChannelValue());

                default:
                    return "B " + Byte255(ChannelValue());
            }
        }

        static string Percent(double normalized)
        {
            return TweeqFormat.Format(Clamp01(normalized) * PERCENT_SCALE, 1, true);
        }

        // r/g/b は内部 0-1 だが、表示は 0-255（仕様 §A のマッピング表）
        static string Byte255(double normalized)
        {
            return TweeqFormat.Format(Clamp01(normalized) * BYTE_SCALE, 0, true);
        }

        void OnLabelGeometryChanged(GeometryChangedEvent evt)
        {
            UpdateLabelPosition();
        }

        void UpdateLabelPosition()
        {
            if (!_hasState)
            {
                return;
            }

            float width = _label.resolvedStyle.width;
            float height = _label.resolvedStyle.height;
            if (float.IsNaN(width) || float.IsNaN(height))
            {
                return;
            }

            float left = _state.Origin.x - width * 0.5f;
            float top = _state.Origin.y
                - (_state.Theme.InputHeight * LABEL_GAP_FACTOR + height * 0.5f)
                - height * 0.5f;

            Rect bounds = this.contentRect;
            if (!float.IsNaN(bounds.width) && bounds.width > 0f && bounds.height > 0f)
            {
                // 端クランプは「はみ出す側だけ」。ラベルが枠より大きい場合は左上を優先する
                left = Mathf.Min(left, bounds.xMax - LABEL_EDGE_MARGIN - width);
                left = Mathf.Max(left, bounds.xMin + LABEL_EDGE_MARGIN);
                top = Mathf.Min(top, bounds.yMax - LABEL_EDGE_MARGIN - height);
                top = Mathf.Max(top, bounds.yMin + LABEL_EDGE_MARGIN);
            }

            _label.style.left = left;
            _label.style.top = top;
        }

        #endregion

        #region Painting

        void OnGeneratePaint(MeshGenerationContext context)
        {
            if (!_hasState || context == null)
            {
                return;
            }

            TweeqTheme theme = _state.Theme;
            if (theme == null)
            {
                return;
            }

            Painter2D painter = context.painter2D;
            if (painter == null)
            {
                return;
            }

            switch (_state.Mode)
            {
                case ColorTweakMode.Pad:
                    // pad 面はテクスチャ背景の子要素が担当する
                    break;

                case ColorTweakMode.Hue:
                    PaintHueRing(painter, theme);
                    break;

                default:
                    PaintSlider(painter, theme);
                    break;
            }

            PaintPreview(painter, theme);
        }

        // origin 中心・直径 TweakWidth の色相リング。リング全体を hue 分だけ逆回転させ、
        // 現在の色相が常に真上を向くようにする（Vue の rotate: h * -360deg）
        void PaintHueRing(Painter2D painter, TweeqTheme theme)
        {
            float radius = _state.TweakWidth * 0.5f - HUE_RING_WIDTH * 0.5f;
            if (radius <= 0f)
            {
                return;
            }

            Vector2 center = _state.Origin;
            double step = HUE_RANGE / HUE_SEGMENTS;

            painter.lineWidth = HUE_RING_WIDTH;
            painter.lineCap = LineCap.Butt;

            for (int index = 0; index < HUE_SEGMENTS; index++)
            {
                double hue = index * step;
                painter.strokeColor = ToColor(new HSVA(hue + step * 0.5, 1.0, 1.0, 1.0));

                float start = (float)RingAngle(hue);
                painter.BeginPath();
                painter.Arc(
                    center,
                    radius,
                    new Angle(start, AngleUnit.Degree),
                    new Angle(start + (float)step + SEGMENT_OVERLAP_DEGREES, AngleUnit.Degree),
                    ArcDirection.Clockwise);
                painter.Stroke();
            }

            // 60° ごとの目盛り。Vue は mask で穴を開けているので、背景色で塗って抜けに見せる
            painter.fillColor = theme.Background;

            for (int index = 0; index < HUE_TICK_COUNT; index++)
            {
                double hue = index * (HUE_RANGE / HUE_TICK_COUNT);
                Vector2 direction = AngleDirection(RingAngle(hue));
                FillCircle(painter, center + direction * radius, HUE_TICK_RADIUS);
            }
        }

        // 現在の色相を真上（-90°）へ固定する向き
        double RingAngle(double hue)
        {
            return hue - _state.Hsva.H - 90.0;
        }

        void PaintSlider(Painter2D painter, TweeqTheme theme)
        {
            // val だけ縦（仕様 §A のマッピング: val のみ dy）
            bool vertical = _state.Mode == ColorTweakMode.Value;
            float length = _state.TweakWidth;
            Vector2 origin = _state.Origin;

            Rect rect = vertical
                ? new Rect(
                    origin.x - SLIDER_THICKNESS * 0.5f,
                    origin.y - length * 0.5f,
                    SLIDER_THICKNESS,
                    length)
                : new Rect(
                    origin.x - length * 0.5f,
                    origin.y - SLIDER_THICKNESS * 0.5f,
                    length,
                    SLIDER_THICKNESS);

            if (_state.Mode == ColorTweakMode.Alpha)
            {
                PaintCheckerboard(painter, rect);
            }

            // 頂点カラー補間（context.Allocate）ではなく分割塗りにしてある。
            // 同一要素内では Painter2D と Allocate の描画順が保証されず、
            // 枠線やマーカーがグラデの下に潜り込むため
            for (int index = 0; index < SLIDER_SEGMENTS; index++)
            {
                double from = index / (double)SLIDER_SEGMENTS;
                double to = (index + 1) / (double)SLIDER_SEGMENTS;
                painter.fillColor = ChannelColor((from + to) * 0.5);

                if (vertical)
                {
                    // 縦は下端が 0
                    float top = rect.yMax - (float)to * length;
                    FillRect(
                        painter,
                        rect.xMin,
                        top,
                        SLIDER_THICKNESS,
                        (float)(to - from) * length + SEGMENT_OVERLAP_PIXELS);
                }
                else
                {
                    FillRect(
                        painter,
                        rect.xMin + (float)from * length,
                        rect.yMin,
                        (float)(to - from) * length + SEGMENT_OVERLAP_PIXELS,
                        SLIDER_THICKNESS);
                }
            }

            StrokeRect(painter, rect, theme.Border, SLIDER_BORDER_WIDTH);
            PaintSliderMarker(painter, rect, vertical, length);
        }

        void PaintSliderMarker(Painter2D painter, Rect rect, bool vertical, float length)
        {
            float value = (float)Clamp01(ChannelValue());

            Vector2 from;
            Vector2 to;

            if (vertical)
            {
                float y = rect.yMax - value * length;
                from = new Vector2(rect.xMin - MARKER_OVERHANG, y);
                to = new Vector2(rect.xMax + MARKER_OVERHANG, y);
            }
            else
            {
                float x = rect.xMin + value * length;
                from = new Vector2(x, rect.yMin - MARKER_OVERHANG);
                to = new Vector2(x, rect.yMax + MARKER_OVERHANG);
            }

            painter.lineCap = LineCap.Butt;

            painter.strokeColor = MarkerCore;
            painter.lineWidth = MARKER_WIDTH;
            painter.BeginPath();
            painter.MoveTo(from);
            painter.LineTo(to);
            painter.Stroke();

            painter.strokeColor = MarkerShade;
            painter.lineWidth = MARKER_SHADE_WIDTH;
            painter.BeginPath();
            painter.MoveTo(from);
            painter.LineTo(to);
            painter.Stroke();
        }

        void PaintPreview(Painter2D painter, TweeqTheme theme)
        {
            float radius = theme.InputHeight * PREVIEW_RADIUS_FACTOR;
            if (radius <= 0f)
            {
                return;
            }

            Color fill = _state.Value;
            if (_state.Mode != ColorTweakMode.Alpha)
            {
                // α まで載せると透明時にプレビューが消えて位置が読めない
                // （Vue も a モード以外は不透明化している）
                fill.a = 1f;
            }

            // α モードは半透明のまま描くので、背後のガイドが透けて色が読めなくなる。
            // 下地に Background を 1 枚敷いてから色を重ねる
            Vector2 center = _state.Origin;

            painter.fillColor = theme.Background;
            FillCircle(painter, center, radius);

            painter.fillColor = fill;
            FillCircle(painter, center, radius);

            painter.strokeColor = theme.Border;
            painter.lineWidth = PREVIEW_BORDER_WIDTH;
            painter.lineCap = LineCap.Butt;
            painter.BeginPath();
            painter.Arc(
                center,
                Mathf.Max(0.5f, radius - PREVIEW_BORDER_WIDTH * 0.5f),
                new Angle(0f, AngleUnit.Degree),
                new Angle(360f, AngleUnit.Degree));
            painter.ClosePath();
            painter.Stroke();
        }

        #endregion

        #region Channel

        // スライダー上の位置 t（0-1）に対応する色。sat / val / r / g / b / alpha は
        // いずれも 1 チャンネルだけを差し替えれば足りる
        Color ChannelColor(double t)
        {
            double amount = Clamp01(t);

            switch (_state.Mode)
            {
                case ColorTweakMode.Saturation:
                    return ToColor(new HSVA(_state.Hsva.H, amount, _state.Hsva.V, 1.0));

                case ColorTweakMode.Value:
                    return ToColor(new HSVA(_state.Hsva.H, _state.Hsva.S, amount, 1.0));

                case ColorTweakMode.Alpha:
                    return new Color(_state.Value.r, _state.Value.g, _state.Value.b, (float)amount);

                case ColorTweakMode.Red:
                    return new Color((float)amount, _state.Value.g, _state.Value.b, 1f);

                case ColorTweakMode.Green:
                    return new Color(_state.Value.r, (float)amount, _state.Value.b, 1f);

                default:
                    return new Color(_state.Value.r, _state.Value.g, (float)amount, 1f);
            }
        }

        double ChannelValue()
        {
            switch (_state.Mode)
            {
                case ColorTweakMode.Saturation:
                    return _state.Hsva.S;

                case ColorTweakMode.Value:
                    return _state.Hsva.V;

                case ColorTweakMode.Alpha:
                    return _state.Hsva.A;

                case ColorTweakMode.Red:
                    return _state.Value.r;

                case ColorTweakMode.Green:
                    return _state.Value.g;

                default:
                    return _state.Value.b;
            }
        }

        static Color ToColor(HSVA hsva)
        {
            CoreRgba rgba = TweeqColorLogic.HsvaToRgba(hsva);
            return new Color((float)rgba.R, (float)rgba.G, (float)rgba.B, (float)rgba.A);
        }

        #endregion

        #region Helpers

        static void PaintCheckerboard(Painter2D painter, Rect rect)
        {
            painter.fillColor = CheckerLight;
            FillRect(painter, rect.xMin, rect.yMin, rect.width, rect.height);

            painter.fillColor = CheckerDark;

            int columns = Mathf.CeilToInt(rect.width / CHECKER_CELL);
            int rows = Mathf.CeilToInt(rect.height / CHECKER_CELL);

            for (int row = 0; row < rows; row++)
            {
                float y = row * CHECKER_CELL;
                float cellHeight = Mathf.Min(CHECKER_CELL, rect.height - y);

                for (int column = (row & 1) == 0 ? 1 : 0; column < columns; column += 2)
                {
                    float x = column * CHECKER_CELL;
                    FillRect(
                        painter,
                        rect.xMin + x,
                        rect.yMin + y,
                        Mathf.Min(CHECKER_CELL, rect.width - x),
                        cellHeight);
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

        static void StrokeRect(Painter2D painter, Rect rect, Color color, float width)
        {
            float inset = width * 0.5f;

            painter.strokeColor = color;
            painter.lineWidth = width;
            painter.lineCap = LineCap.Butt;
            painter.BeginPath();
            painter.MoveTo(new Vector2(rect.xMin + inset, rect.yMin + inset));
            painter.LineTo(new Vector2(rect.xMax - inset, rect.yMin + inset));
            painter.LineTo(new Vector2(rect.xMax - inset, rect.yMax - inset));
            painter.LineTo(new Vector2(rect.xMin + inset, rect.yMax - inset));
            painter.ClosePath();
            painter.Stroke();
        }

        static void FillCircle(Painter2D painter, Vector2 center, float radius)
        {
            if (radius <= 0f)
            {
                return;
            }

            painter.BeginPath();
            painter.Arc(
                center,
                radius,
                new Angle(0f, AngleUnit.Degree),
                new Angle(360f, AngleUnit.Degree));
            painter.ClosePath();
            painter.Fill();
        }

        static Vector2 AngleDirection(double degrees)
        {
            float radians = Mathf.Deg2Rad * (float)degrees;
            return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        }

        static double Clamp01(double value)
        {
            if (double.IsNaN(value))
            {
                return 0.0;
            }

            return value < 0.0 ? 0.0 : value > 1.0 ? 1.0 : value;
        }

        static void SetBorderWidth(VisualElement element, float width)
        {
            element.style.borderLeftWidth = width;
            element.style.borderRightWidth = width;
            element.style.borderTopWidth = width;
            element.style.borderBottomWidth = width;
        }

        static void SetBorderColor(VisualElement element, Color color)
        {
            element.style.borderLeftColor = color;
            element.style.borderRightColor = color;
            element.style.borderTopColor = color;
            element.style.borderBottomColor = color;
        }

        static void SetBorderRadius(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }

        #endregion
    }
}
