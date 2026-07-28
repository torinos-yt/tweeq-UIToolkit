using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>バルーンの矢印が生える辺。<see cref="TweeqArrowSide.None"/> は矢印なしの角丸矩形。</summary>
    public enum TweeqArrowSide
    {
        None,
        Top,
        Bottom,
        Left,
        Right,
    }

    /// <summary>
    /// 吹き出し型のサーフェス。角丸矩形と矢印を「1本の輪郭」として Painter2D で描くので、
    /// 矢印の付け根に境界線の継ぎ目が出ない（Vue 版が clip-path + SVG stroke でやっていることの等価）。
    /// 中身は <see cref="VisualElement.contentContainer"/> 経由で普通に Add する。
    /// </summary>
    [UxmlElement]
    public sealed partial class TweeqBalloon : VisualElement, ITweeqThemed
    {
        #region Constants

        /// <summary>矢印の底辺幅（px）。視覚言語として固定。</summary>
        public const float ARROW_WIDTH = 14f;

        /// <summary>矢印の突出量（px）。</summary>
        public const float ARROW_HEIGHT = 7f;

        /// <summary>矢印の先端とアンカーの間に残す隙間（px）。</summary>
        public const float ARROW_GAP = 2f;

        const float BORDER_WIDTH = 1f;

        /// <summary>影のぼかし半径の既定値。Vue の drop-shadow(0 2px 12px) 相当。</summary>
        public const float DEFAULT_SHADOW_BLUR = 12f;

        /// <summary>影の下方向オフセットの既定値。</summary>
        public const float DEFAULT_SHADOW_OFFSET_Y = 2f;

        // box-shadow が無いので、太さの違う輪郭ストロークを重ねてぼかしを近似する。
        // 枚数を増やすほど滑らかになるが描画コストに直結するので 3 枚で打ち止め
        const int SHADOW_LAYERS = 3;

        // 出現時の scale。原点は矢印の先端（＝指している場所から生えて見える）
        const float POP_IN_SCALE = 0.96f;

        #endregion

        #region Fields

        // トランジション定義は不変なので型ごとに 1 個だけ作って全インスタンスで共有する
        // （style.transition* は毎回 List を要求するため、都度 new すると開くたびにゴミが出る）
        static readonly StyleList<StylePropertyName> ScaleProperty =
            new StyleList<StylePropertyName>(new List<StylePropertyName> { new StylePropertyName("scale") });

        static readonly StyleList<EasingFunction> EaseOut =
            new StyleList<EasingFunction>(new List<EasingFunction> { new EasingFunction(EasingMode.EaseOutCubic) });

        static readonly StyleList<TimeValue> InstantDuration =
            new StyleList<TimeValue>(new List<TimeValue> { new TimeValue(0f, TimeUnit.Second) });

        TweeqTheme _theme = TweeqTheme.Dark();
        TweeqArrowSide _arrowSide = TweeqArrowSide.None;
        float _arrowOffset;

        // NaN = テーマ既定に従う。ツールチップのピル形状のように部分的に上書きしたいケースがある
        float _radius = float.NaN;
        float _paddingVertical = float.NaN;
        float _paddingHorizontal = float.NaN;

        float _shadowBlur = DEFAULT_SHADOW_BLUR;
        float _shadowOffsetY = DEFAULT_SHADOW_OFFSET_Y;

        VisualElement _content;

        // テーマ由来の秒数。テーマ差し替え時にだけ作り直す
        StyleList<TimeValue> _popInDuration;

        // 出現アニメの「次フレームで scale を戻す」1件だけを使い回す（毎回 new しない）
        IVisualElementScheduledItem _popInItem;

        #endregion

        #region Public API

        /// <summary>矢印が生える辺。</summary>
        [UxmlAttribute("arrow-side")]
        public TweeqArrowSide ArrowSide
        {
            get => _arrowSide;
            set
            {
                if (_arrowSide == value)
                {
                    return;
                }

                _arrowSide = value;
                ApplyArrowPadding();
                UpdateTransformOrigin();
                this.MarkDirtyRepaint();
            }
        }

        /// <summary>
        /// 矢印の中心位置。矢印が生える辺に沿って、この要素の左上から測った px。
        /// 角丸に食い込まないよう描画時にクランプされる。
        /// </summary>
        // Radius / Padding* は「NaN＝テーマ既定」の番兵を持つため UXML には出さない
        // （UI Builder が既定値として 0 を書き込むと、テーマ追従が黙って壊れる）
        [UxmlAttribute("arrow-offset")]
        public float ArrowOffset
        {
            get => _arrowOffset;
            set
            {
                if (Mathf.Approximately(_arrowOffset, value))
                {
                    return;
                }

                _arrowOffset = value;
                UpdateTransformOrigin();
                this.MarkDirtyRepaint();
            }
        }

        /// <summary>角丸半径。NaN でテーマの RadiusPopup に従う。</summary>
        public float Radius
        {
            get => _radius;
            set
            {
                _radius = value;
                this.MarkDirtyRepaint();
            }
        }

        /// <summary>中身の上下パディング。NaN でテーマの PopupPadding に従う。</summary>
        public float PaddingVertical
        {
            get => _paddingVertical;
            set
            {
                _paddingVertical = value;
                ApplyContentPadding();
            }
        }

        /// <summary>中身の左右パディング。NaN でテーマの PopupPadding に従う。</summary>
        public float PaddingHorizontal
        {
            get => _paddingHorizontal;
            set
            {
                _paddingHorizontal = value;
                ApplyContentPadding();
            }
        }

        /// <summary>影のぼかし半径（px）。</summary>
        public float ShadowBlur
        {
            get => _shadowBlur;
            set
            {
                _shadowBlur = value;
                this.MarkDirtyRepaint();
            }
        }

        /// <summary>影の下方向オフセット（px）。</summary>
        public float ShadowOffsetY
        {
            get => _shadowOffsetY;
            set
            {
                _shadowOffsetY = value;
                this.MarkDirtyRepaint();
            }
        }

        /// <summary>配色テーマ。null を渡した場合は Dark() にフォールバックする。</summary>
        public TweeqTheme Theme
        {
            get => _theme;
            set
            {
                _theme = value ?? TweeqTheme.Dark();
                ApplyTheme();
            }
        }

        /// <summary>中身はコンテンツ層へ入れる（矢印ぶんのパディングを持つのは外側）。</summary>
        public override VisualElement contentContainer => _content;

        /// <summary>
        /// 出現アニメ（矢印の先端を原点にした scale 0.96→1）を頭から再生する。
        /// パネルに載せた直後に呼ぶこと。scheduler はデタッチ中の要素では走らない。
        /// </summary>
        public void PlayIn()
        {
            UpdateTransformOrigin();

            // 使い回しの2回目以降は scale が 1 のまま残っているので、そのまま 0.96 を入れると
            // 「縮むアニメ」が先に走ってしまう。縮む側だけ duration 0 で当てる
            // （Vue の @starting-style が担っていた役目）
            this.style.transitionDuration = InstantDuration;
            this.style.scale = new StyleScale(new Scale(new Vector3(POP_IN_SCALE, POP_IN_SCALE, 1f)));

            if (this.panel == null)
            {
                // scheduler が動かないので、縮んだまま固まらないよう即座に戻す
                FinishPopIn();
                return;
            }

            if (_popInItem == null)
            {
                _popInItem = this.schedule.Execute(FinishPopIn);
            }

            _popInItem.ExecuteLater(0L);
        }

        #endregion

        #region Construction

        public TweeqBalloon()
        {
            this.name = "tweeq-balloon";

            // 影と、矢印なし時に辺へまたがる 1px ストロークが切れないようにする
            this.style.overflow = Overflow.Visible;
            this.style.alignSelf = Align.FlexStart;

            _content = new VisualElement { name = "tweeq-balloon-content" };
            this.hierarchy.Add(_content);

            this.generateVisualContent += OnGenerateVisualContent;
            this.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);

            ApplyArrowPadding();
            ApplyTheme();
            UpdateTransformOrigin();
        }

        #endregion

        #region Presentation

        void ApplyTheme()
        {
            ApplyContentPadding();
            ApplyPopInTransition();
            this.MarkDirtyRepaint();
        }

        // トランジションは毎フレーム触ると StyleList の確保が積み上がるので、テーマ変更時にだけ設定する
        void ApplyPopInTransition()
        {
            float duration = _theme != null ? _theme.ActiveTransitionDuration : 0.064f;

            _popInDuration = new StyleList<TimeValue>(
                new List<TimeValue> { new TimeValue(duration, TimeUnit.Second) });

            this.style.transitionProperty = ScaleProperty;
            this.style.transitionTimingFunction = EaseOut;
            this.style.transitionDuration = _popInDuration;
        }

        void ApplyContentPadding()
        {
            if (_content == null)
            {
                return;
            }

            float fallback = _theme != null ? _theme.PopupPadding : 9f;
            float vertical = Resolve(_paddingVertical, fallback);
            float horizontal = Resolve(_paddingHorizontal, fallback);

            _content.style.paddingTop = vertical;
            _content.style.paddingBottom = vertical;
            _content.style.paddingLeft = horizontal;
            _content.style.paddingRight = horizontal;
        }

        // 矢印は外側のパディングとして場所を確保する。こうすると輪郭パスの座標と
        // 中身のレイアウトが 1:1 で対応し、描画側でオフセットを二重に持たなくて済む
        void ApplyArrowPadding()
        {
            float depth = ARROW_HEIGHT + ARROW_GAP;
            this.style.paddingTop = _arrowSide == TweeqArrowSide.Top ? depth : 0f;
            this.style.paddingBottom = _arrowSide == TweeqArrowSide.Bottom ? depth : 0f;
            this.style.paddingLeft = _arrowSide == TweeqArrowSide.Left ? depth : 0f;
            this.style.paddingRight = _arrowSide == TweeqArrowSide.Right ? depth : 0f;
        }

        void FinishPopIn()
        {
            this.style.transitionDuration = _popInDuration;
            this.style.scale = new StyleScale(new Scale(Vector3.one));
        }

        void OnGeometryChanged(GeometryChangedEvent evt)
        {
            // bottom/right の原点は「サイズ - GAP」なので、実サイズが決まってから置き直す
            UpdateTransformOrigin();
        }

        // 出現の scale 原点を矢印の先端に置く（矢印なしなら中心）
        void UpdateTransformOrigin()
        {
            float width = this.layout.width;
            float height = this.layout.height;

            switch (_arrowSide)
            {
                case TweeqArrowSide.Top:
                    SetTransformOrigin(new Length(_arrowOffset), new Length(ARROW_GAP));
                    break;
                case TweeqArrowSide.Bottom:
                    SetTransformOrigin(
                        new Length(_arrowOffset),
                        IsUsableSize(height)
                            ? new Length(height - ARROW_GAP)
                            : new Length(100f, LengthUnit.Percent));
                    break;
                case TweeqArrowSide.Left:
                    SetTransformOrigin(new Length(ARROW_GAP), new Length(_arrowOffset));
                    break;
                case TweeqArrowSide.Right:
                    SetTransformOrigin(
                        IsUsableSize(width)
                            ? new Length(width - ARROW_GAP)
                            : new Length(100f, LengthUnit.Percent),
                        new Length(_arrowOffset));
                    break;
                default:
                    SetTransformOrigin(
                        new Length(50f, LengthUnit.Percent),
                        new Length(50f, LengthUnit.Percent));
                    break;
            }
        }

        void SetTransformOrigin(Length x, Length y)
        {
            this.style.transformOrigin = new StyleTransformOrigin(new TransformOrigin(x, y, 0f));
        }

        #endregion

        #region Painting

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

            float layerWidth = this.layout.width;
            float layerHeight = this.layout.height;
            if (!IsUsableSize(layerWidth) || !IsUsableSize(layerHeight))
            {
                return;
            }

            // 本体（角丸矩形）の矩形。矢印ぶんのパディングを外側から差し引く
            float originX = _arrowSide == TweeqArrowSide.Left ? ARROW_HEIGHT + ARROW_GAP : 0f;
            float originY = _arrowSide == TweeqArrowSide.Top ? ARROW_HEIGHT + ARROW_GAP : 0f;
            float width = layerWidth - originX
                - (_arrowSide == TweeqArrowSide.Right ? ARROW_HEIGHT + ARROW_GAP : 0f);
            float height = layerHeight - originY
                - (_arrowSide == TweeqArrowSide.Bottom ? ARROW_HEIGHT + ARROW_GAP : 0f);
            if (!IsUsableSize(width) || !IsUsableSize(height))
            {
                return;
            }

            float radius = Mathf.Max(
                0f,
                Mathf.Min(Resolve(_radius, _theme.RadiusPopup), width * 0.5f, height * 0.5f));

            PaintShadow(painter, originX, originY, width, height, radius);

            BuildOutline(painter, originX, originY, width, height, radius, 0f);
            // 半透明 Surface はブラー前提の色（Vue）。ブラー無しでは背面が透けるので不透明合成で描く
            painter.fillColor = _theme.SurfaceOpaque;
            painter.Fill();

            painter.strokeColor = _theme.Border;
            painter.lineWidth = BORDER_WIDTH;
            painter.lineJoin = LineJoin.Miter;
            painter.Stroke();
        }

        // drop-shadow の代替。同じ輪郭を「太いストローク＋塗り」で数枚重ね、
        // 外側ほど薄くなるハローを作る。1枚あたり α を等分するので合計は元の α に収まる
        void PaintShadow(Painter2D painter, float originX, float originY, float width, float height, float radius)
        {
            Color shadow = _theme.Shadow;
            if (shadow.a <= 0f || _shadowBlur <= 0f)
            {
                return;
            }

            Color layerColor = shadow;
            layerColor.a = shadow.a / (SHADOW_LAYERS + 1);

            painter.strokeColor = layerColor;
            painter.fillColor = layerColor;
            painter.lineJoin = LineJoin.Round;

            for (int index = 0; index < SHADOW_LAYERS; index++)
            {
                // 外側の広いストロークから順に描く（内側ほど濃く積み上がる）
                float spread = _shadowBlur * (SHADOW_LAYERS - index) / SHADOW_LAYERS;
                BuildOutline(painter, originX, originY, width, height, radius, _shadowOffsetY);
                painter.lineWidth = spread * 2f;
                painter.Stroke();
            }

            // ストロークだけだと内側が抜けるので、本体ぶんも 1 枚敷いておく
            BuildOutline(painter, originX, originY, width, height, radius, _shadowOffsetY);
            painter.Fill();
        }

        // Vue Balloon.vue の SVG パスと同じ順路（時計回り）を Painter2D で辿る。
        // 角丸は ArcTo（canvas の arcTo 相当）で、矢印は辺の途中に差し込む折れ線で表す
        void BuildOutline(
            Painter2D painter,
            float originX,
            float originY,
            float width,
            float height,
            float radius,
            float offsetY)
        {
            float left = originX;
            float top = originY + offsetY;
            float right = originX + width;
            float bottom = originY + height + offsetY;
            float half = ARROW_WIDTH * 0.5f;

            painter.BeginPath();
            painter.MoveTo(new Vector2(left + radius, top));

            if (_arrowSide == TweeqArrowSide.Top)
            {
                float center = ClampAlongEdge(left + _arrowOffset, left, right, radius, half);
                painter.LineTo(new Vector2(center - half, top));
                painter.LineTo(new Vector2(center, top - ARROW_HEIGHT));
                painter.LineTo(new Vector2(center + half, top));
            }

            painter.LineTo(new Vector2(right - radius, top));
            painter.ArcTo(new Vector2(right, top), new Vector2(right, bottom), radius);

            if (_arrowSide == TweeqArrowSide.Right)
            {
                float center = ClampAlongEdge(top + _arrowOffset, top, bottom, radius, half);
                painter.LineTo(new Vector2(right, center - half));
                painter.LineTo(new Vector2(right + ARROW_HEIGHT, center));
                painter.LineTo(new Vector2(right, center + half));
            }

            painter.LineTo(new Vector2(right, bottom - radius));
            painter.ArcTo(new Vector2(right, bottom), new Vector2(left, bottom), radius);

            if (_arrowSide == TweeqArrowSide.Bottom)
            {
                float center = ClampAlongEdge(left + _arrowOffset, left, right, radius, half);
                painter.LineTo(new Vector2(center + half, bottom));
                painter.LineTo(new Vector2(center, bottom + ARROW_HEIGHT));
                painter.LineTo(new Vector2(center - half, bottom));
            }

            painter.LineTo(new Vector2(left + radius, bottom));
            painter.ArcTo(new Vector2(left, bottom), new Vector2(left, top), radius);

            if (_arrowSide == TweeqArrowSide.Left)
            {
                float center = ClampAlongEdge(top + _arrowOffset, top, bottom, radius, half);
                painter.LineTo(new Vector2(left, center + half));
                painter.LineTo(new Vector2(left - ARROW_HEIGHT, center));
                painter.LineTo(new Vector2(left, center - half));
            }

            painter.LineTo(new Vector2(left, top + radius));
            painter.ArcTo(new Vector2(left, top), new Vector2(right, top), radius);
            painter.ClosePath();
        }

        #endregion

        #region Helpers

        // 矢印の底辺が角丸に食い込むと輪郭が破綻するので、直線部分の中に押し込む
        static float ClampAlongEdge(float value, float min, float max, float radius, float half)
        {
            float low = min + radius + half;
            float high = max - radius - half;
            if (high < low)
            {
                // 辺が短すぎて矢印を置く直線が無い場合は中央に寄せる（破綻より歪みを選ぶ）
                return (min + max) * 0.5f;
            }

            return Mathf.Clamp(value, low, high);
        }

        static float Resolve(float value, float fallback)
        {
            return float.IsNaN(value) ? fallback : value;
        }

        static bool IsUsableSize(float value)
        {
            return !float.IsNaN(value) && value > 0f;
        }

        #endregion
    }
}
