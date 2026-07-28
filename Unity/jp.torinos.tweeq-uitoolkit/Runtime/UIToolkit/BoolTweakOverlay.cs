using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// スワイプトグル中だけ生きるプレビューオーバーレイ（仕様「共通」）。
    /// コントロールの左右に 18px の円バッジを 2 個描き、プレビュー値側だけ Accent にする。
    ///
    /// 置き場所は TweeqOverlayLayer（パネル最前面）ではなく「コントロール自身の子」にした。
    /// バッジの位置はコントロールの箱に対する ±1.2×24px という純粋なローカル量なので、
    /// パネル座標へ変換する理由が無く、レイアウト追従（スクロール・グループの開閉）も
    /// 親に任せられる。前提として親側は overflow を Visible にしておくこと。
    /// ParameterGroup は開き切ると Visible に戻るので、通常のレイアウトでは切られない。
    /// </summary>
    sealed class BoolTweakOverlay : VisualElement
    {
        #region Constants

        // 出現時に ±1.0× から ±1.2× へ広がる（仕様「共通」・Vue の v-enter-from）
        const float COLLAPSED_FACTOR = 1.0f;
        const float EXPANDED_FACTOR = 1.2f;

        const float BADGE_SIZE = 18f;
        const float BADGE_STROKE_WIDTH = 2f;

        // active 系トランジション 64ms（仕様の遷移表）
        const float ACTIVE_TRANSITION_DURATION = 0.064f;

        // check-circle グリフの中のチェックは、円に対しておよそ 6 割の大きさ
        const float BADGE_CHECK_SCALE = 0.62f;

        // チェックマーク（正方形内の正規化座標）。mdi:check-bold を 2 セグメントの折れ線に単純化した
        static readonly Vector2 MARK_START = new Vector2(0.18f, 0.50f);
        static readonly Vector2 MARK_ELBOW = new Vector2(0.42f, 0.74f);
        static readonly Vector2 MARK_END = new Vector2(0.82f, 0.26f);

        #endregion

        #region Fields

        TweeqTheme _theme;
        bool _previewValue;
        float _unit = 24f;
        bool _expanded;

        #endregion

        #region Construction

        public BoolTweakOverlay()
        {
            this.name = "tweeq-bool-tweak-overlay";
            this.pickingMode = PickingMode.Ignore;
            this.style.position = Position.Absolute;
            this.style.top = 0f;
            this.style.bottom = 0f;
            this.style.overflow = Overflow.Visible;

            ApplyInsets();
            ApplyTransition();

            this.generateVisualContent += OnGenerateVisualContent;
            this.RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
        }

        void ApplyTransition()
        {
            // Vue は cubic-bezier(0.4,0,0.2,1)。UI Toolkit に同一カーブが無いため
            // EaseInOutCubic で近似する（RotaryInput / NumberInput と同じ判断）
            this.style.transitionProperty = new StyleList<StylePropertyName>(
                new List<StylePropertyName>
                {
                    new StylePropertyName("left"),
                    new StylePropertyName("right"),
                });
            this.style.transitionDuration = new StyleList<TimeValue>(
                new List<TimeValue>
                {
                    new TimeValue(ACTIVE_TRANSITION_DURATION, TimeUnit.Second),
                    new TimeValue(ACTIVE_TRANSITION_DURATION, TimeUnit.Second),
                });
            this.style.transitionTimingFunction = new StyleList<EasingFunction>(
                new List<EasingFunction>
                {
                    new EasingFunction(EasingMode.EaseInOutCubic),
                    new EasingFunction(EasingMode.EaseInOutCubic),
                });
        }

        #endregion

        #region Public API

        /// <summary>描画パラメータを更新する。unit はコントロールの基準高さ（＝ InputHeight）。</summary>
        public void Sync(TweeqTheme theme, bool previewValue, float unit)
        {
            _theme = theme;
            _previewValue = previewValue;

            if (unit > 0f && !float.IsNaN(unit))
            {
                _unit = unit;
            }

            ApplyInsets();
            this.MarkDirtyRepaint();
        }

        #endregion

        #region Internals

        void OnAttachToPanel(AttachToPanelEvent evt)
        {
            if (_expanded)
            {
                return;
            }

            // 折り畳んだ inset が 1 フレーム描かれてからでないとトランジションが走らない
            this.schedule.Execute(() =>
            {
                _expanded = true;
                ApplyInsets();
            }).StartingIn(0);
        }

        void ApplyInsets()
        {
            float amount = _unit * (_expanded ? EXPANDED_FACTOR : COLLAPSED_FACTOR);
            this.style.left = -amount;
            this.style.right = -amount;
        }

        void OnGenerateVisualContent(MeshGenerationContext context)
        {
            if (context == null || _theme == null)
            {
                return;
            }

            Painter2D painter = context.painter2D;
            if (painter == null)
            {
                return;
            }

            float width = this.layout.width;
            float height = this.layout.height;
            if (float.IsNaN(width) || float.IsNaN(height) || width < BADGE_SIZE || height <= 0f)
            {
                return;
            }

            float radius = BADGE_SIZE * 0.5f;
            float centerY = height * 0.5f;

            // Painter2D は色を補間できないので、色の 64ms 遷移は諦めて即時切り替えにしている
            Color offColor = _previewValue ? _theme.Border : _theme.Accent;
            Color onColor = _previewValue ? _theme.Accent : _theme.Border;

            PaintOffBadge(painter, new Vector2(radius, centerY), radius, offColor);
            PaintOnBadge(painter, new Vector2(width - radius, centerY), radius, onColor);
        }

        // ic:baseline-radio-button-unchecked 相当。Background の円の上にリングを描く
        void PaintOffBadge(Painter2D painter, Vector2 center, float radius, Color color)
        {
            FillCircle(painter, center, radius, _theme.Background);

            painter.strokeColor = color;
            painter.lineWidth = BADGE_STROKE_WIDTH;
            painter.lineCap = LineCap.Butt;
            painter.BeginPath();
            painter.Arc(
                center,
                Mathf.Max(radius - BADGE_STROKE_WIDTH * 0.5f, 0.5f),
                new Angle(0f, AngleUnit.Degree),
                new Angle(360f, AngleUnit.Degree));
            painter.ClosePath();
            painter.Stroke();
        }

        // ic:baseline-check-circle 相当。塗り円からチェックを抜いた形（＝背景色で描く）
        void PaintOnBadge(Painter2D painter, Vector2 center, float radius, Color color)
        {
            FillCircle(painter, center, radius, color);

            float half = radius * BADGE_CHECK_SCALE;
            Rect box = new Rect(center.x - half, center.y - half, half * 2f, half * 2f);
            PaintCheck(painter, box, BADGE_STROKE_WIDTH, _theme.Background);
        }

        static void FillCircle(Painter2D painter, Vector2 center, float radius, Color color)
        {
            painter.fillColor = color;
            painter.BeginPath();
            painter.Arc(
                center,
                radius,
                new Angle(0f, AngleUnit.Degree),
                new Angle(360f, AngleUnit.Degree));
            painter.ClosePath();
            painter.Fill();
        }

        static void PaintCheck(Painter2D painter, Rect box, float strokeWidth, Color color)
        {
            painter.strokeColor = color;
            painter.lineWidth = strokeWidth;
            painter.lineCap = LineCap.Round;
            painter.lineJoin = LineJoin.Round;
            painter.BeginPath();
            painter.MoveTo(Map(box, MARK_START));
            painter.LineTo(Map(box, MARK_ELBOW));
            painter.LineTo(Map(box, MARK_END));
            painter.Stroke();
        }

        static Vector2 Map(Rect box, Vector2 normalized)
        {
            return new Vector2(
                box.xMin + box.width * normalized.x,
                box.yMin + box.height * normalized.y);
        }

        #endregion
    }
}
