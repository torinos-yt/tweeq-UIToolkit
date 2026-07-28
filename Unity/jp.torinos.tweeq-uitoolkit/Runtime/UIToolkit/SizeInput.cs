using System;
using Tweeq.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// 比率ロック付きの 2D サイズ入力（M6 第2波仕様 §C）。
    /// <see cref="Vec2Input"/> の右端に鎖トグルを融合させ、ロック中は片軸の変更に他軸を追従させる。
    /// </summary>
    /// <remarks>
    /// 追従の基準は「編集開始時の値」（Vue の valueOnEdit）。直前値を基準にすると
    /// ドラッグ中に倍率が積み上がって比率がずれていく。
    /// 開始点は Confirmed で閉じる 1 ジェスチャの先頭で取り直す。
    /// </remarks>
    [UxmlElement]
    public partial class SizeInput : VisualElement, INotifyValueChanged<Vector2>, ITweeqThemed
    {
        #region Constants

        static readonly string[] DEFAULT_AXIS_LABELS = { "W", "H" };

        // 鎖アイコン（egui paint_link_icon の簡易版）。24px 箱の中心基準
        const float LINK_LOOP_OFFSET = 4.5f;
        const float LINK_LOOP_RADIUS = 3.2f;
        const float LINK_STROKE_WIDTH = 1.25f;
        const float LINK_BAR_WIDTH = 1f;

        #endregion

        #region Fields

        readonly InputGroup _group;
        readonly Vec2Input _field;
        readonly ButtonToggleInput _chain;
        readonly VisualElement _chainIcon;

        Vector2 _value;
        Vector2 _baseline;
        bool _hasBaseline;
        bool _keepRatio = true;
        bool _disabled;
        bool _invalid;

        TweeqTheme _theme = TweeqTheme.Dark();

        // 子へ書き戻している最中の通知を自分の入力と誤認しないためのガード
        bool _syncing;

        #endregion

        #region Public API

        /// <summary>値が変わるたびに発火する。</summary>
        public event Action<Vector2> ValueChanged;

        /// <summary>ドラッグ確定・Enter・blur で 1 ジェスチャ 1 回だけ発火する。</summary>
        public event Action<Vector2> Confirmed;

        /// <summary>比率ロックが切り替わったときに発火する（自動解除も含む）。</summary>
        public event Action<bool> KeepRatioChanged;

        /// <summary>現在値。</summary>
        [UxmlAttribute]
        public Vector2 value
        {
            get => _value;
            set
            {
                if (_value.Equals(value))
                {
                    return;
                }

                Vector2 previous = _value;
                SetValueWithoutNotify(value);
                Notify(previous, _value);
            }
        }

        /// <summary>比率ロック。既定 on（Vue の keepRatio = ref(true)）。</summary>
        [UxmlAttribute]
        public bool KeepRatio
        {
            get => _keepRatio;
            set
            {
                if (_keepRatio == value)
                {
                    return;
                }

                _keepRatio = value;
                _chain.SetValueWithoutNotify(value);

                // ロック状態が変わった時点の値が、次の追従の基準になる
                _hasBaseline = false;
                RefreshChain();
                KeepRatioChanged?.Invoke(value);
            }
        }

        /// <summary>数値タプル本体。軸ごとの Precision などを触りたい場合に使う。</summary>
        public Vec2Input Field => _field;

        /// <summary>鎖トグル本体。</summary>
        public ButtonToggleInput Chain => _chain;

        /// <summary>配色テーマ。子へそのまま伝播する。</summary>
        public TweeqTheme Theme
        {
            get => _theme;
            set
            {
                _theme = value ?? TweeqTheme.Dark();
                _group.Theme = _theme;
                _field.Theme = _theme;
                _chain.Theme = _theme;
                ApplyChainSize();
                ApplyBoxFusion();
                RefreshChain();
            }
        }

        /// <summary>各軸の下限。null=制限なし／長さ1=全軸共通／長さ2=軸ごと。</summary>
        [UxmlAttribute]
        public double[] Min
        {
            get => _field.Min;
            set => _field.Min = value;
        }

        /// <summary>各軸の上限。</summary>
        [UxmlAttribute]
        public double[] Max
        {
            get => _field.Max;
            set => _field.Max = value;
        }

        /// <summary>各軸の量子化幅。</summary>
        [UxmlAttribute]
        public double[] Step
        {
            get => _field.Step;
            set => _field.Step = value;
        }

        /// <summary>軸ラベル。既定は W / H。</summary>
        [UxmlAttribute]
        public string[] AxisLabels
        {
            get => _field.AxisLabels;
            set => _field.AxisLabels = value;
        }

        /// <summary>数値欄の静止時表示桁（両軸共通）。</summary>
        [UxmlAttribute]
        public int Precision
        {
            get => _field.Precision;
            set => _field.Precision = value;
        }

        /// <summary>操作不能状態。数値欄と鎖トグルの両方へ配る。</summary>
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
                _field.Disabled = _disabled;
                _chain.Disabled = _disabled;
            }
        }

        /// <summary>不正値表示。数値欄だけに配る（鎖トグルに invalid 表現は無い）。</summary>
        [UxmlAttribute]
        public bool Invalid
        {
            get => _invalid;
            set
            {
                if (_invalid == value)
                {
                    return;
                }

                _invalid = value;
                _field.Invalid = _invalid;
            }
        }

        /// <summary>
        /// 数値欄側のジェスチャ確定を発火する。NumberInput の確定は panel 上のキー／ポインタ操作でしか
        /// 起きないため、外部ドライバとテストのために口を開けてある。比率追従の基準点もここで切れる。
        /// </summary>
        public void PerformFieldConfirm()
        {
            if (_disabled)
            {
                return;
            }

            OnFieldConfirmed(_value);
        }

        /// <summary>ChangeEvent を発火せずに値を設定する。編集の基準点もここで取り直す。</summary>
        public void SetValueWithoutNotify(Vector2 newValue)
        {
            _value = newValue;

            _syncing = true;
            _field.SetValueWithoutNotify(newValue);
            _syncing = false;

            // 外部からの設定は編集セッションの外にあるので、次の編集は新しい値を基準にする
            _hasBaseline = false;
        }

        #endregion

        #region Construction

        public SizeInput()
        {
            this.AddToClassList("tweeq-size-input");
            this.style.flexDirection = FlexDirection.Row;
            this.style.flexGrow = 1f;

            _group = new InputGroup { Theme = _theme };

            _field = new Vec2Input
            {
                name = "tweeq-size-field",
                Theme = _theme,
                AxisLabels = DEFAULT_AXIS_LABELS,
            };

            _chain = new ButtonToggleInput
            {
                name = "tweeq-size-chain",
                Theme = _theme,
            };
            _chain.SetValueWithoutNotify(_keepRatio);

            // 鎖は 24px 固定。InputGroup の等分割に巻き込ませない
            _chain.style.flexGrow = 0f;
            _chain.style.flexShrink = 0f;

            _chainIcon = new VisualElement
            {
                name = "tweeq-size-chain-icon",
                pickingMode = PickingMode.Ignore,
            };
            _chainIcon.style.position = Position.Absolute;
            _chainIcon.style.left = 0f;
            _chainIcon.style.top = 0f;
            _chainIcon.style.right = 0f;
            _chainIcon.style.bottom = 0f;
            _chainIcon.generateVisualContent += OnGenerateChainIcon;
            _chain.hierarchy.Add(_chainIcon);

            _field.ValueChanged += OnFieldChanged;
            _field.Confirmed += OnFieldConfirmed;
            _chain.Confirmed += OnChainConfirmed;

            _group.Add(_field);
            _group.Add(_chain);
            this.hierarchy.Add(_group);

            // Vec2Input は ITweeqInputBox ではないので、InputGroup は端の角丸を割り当てられない。
            // グループが位置を配り直した後で上書きするため、レイアウト確定のたびに掛け直す
            this.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);

            ApplyChainSize();
            ApplyBoxFusion();
            RefreshChain();
        }

        #endregion

        #region Internals

        void OnGeometryChanged(GeometryChangedEvent evt)
        {
            ApplyBoxFusion();
        }

        void ApplyChainSize()
        {
            float size = _theme != null ? _theme.InputHeight : 24f;
            _chain.style.width = size;
            _chain.style.flexBasis = size;
        }

        // [W][H][鎖] を 1 つながりに見せる。各セッタは同値なら何もしないので毎回呼んでよい
        void ApplyBoxFusion()
        {
            NumberInput x = _field.GetAxis(0);
            if (x != null)
            {
                x.InlinePosition = TweeqBoxPosition.Start;
            }

            NumberInput y = _field.GetAxis(1);
            if (y != null)
            {
                y.InlinePosition = TweeqBoxPosition.Middle;
            }

            _chain.InlinePosition = TweeqBoxPosition.End;
        }

        void OnFieldChanged(Vector2 next)
        {
            if (_syncing)
            {
                return;
            }

            if (!_hasBaseline)
            {
                // このジェスチャの基準は「動かす前の値」。次の Confirmed まで固定する
                _baseline = _value;
                _hasBaseline = true;
            }

            SizeApplyResult result = SizeLogic.Apply(
                _value.x,
                _value.y,
                next.x,
                next.y,
                _baseline.x,
                _baseline.y,
                _keepRatio);

            // 自動解除。セッタ経由なので鎖の見た目と通知もここで揃う
            this.KeepRatio = result.KeepRatio;

            Vector2 applied = new Vector2((float)result.X, (float)result.Y);
            if (_value.Equals(applied))
            {
                // 比率追従で入力が打ち消された場合でも、欄の表示は結果に合わせ直す
                WriteField(applied);
                return;
            }

            Vector2 previous = _value;
            _value = applied;
            WriteField(applied);
            Notify(previous, applied);
        }

        void WriteField(Vector2 applied)
        {
            if (_field.value.Equals(applied))
            {
                return;
            }

            _syncing = true;
            _field.SetValueWithoutNotify(applied);
            _syncing = false;
        }

        void OnFieldConfirmed(Vector2 fieldValue)
        {
            if (_syncing)
            {
                return;
            }

            // 次のジェスチャは新しい値を基準にする
            _hasBaseline = false;
            Confirmed?.Invoke(_value);
        }

        void OnChainConfirmed(bool next)
        {
            // トグル側は既に自分の値を反転済み。セッタは同値なら何もしないので二重反転にはならない
            this.KeepRatio = next;
        }

        void RefreshChain()
        {
            _chainIcon.MarkDirtyRepaint();
        }

        void OnGenerateChainIcon(MeshGenerationContext context)
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

            Vector2 center = _chainIcon.contentRect.center;

            // 面色はトグル側が塗るので、その上で読める色を選ぶ
            painter.strokeColor = _keepRatio
                ? TweeqTheme.ContrastText(_theme.Accent)
                : _theme.TextSubtle;
            painter.lineWidth = LINK_STROKE_WIDTH;
            painter.lineCap = LineCap.Butt;

            for (int side = -1; side <= 1; side += 2)
            {
                Vector2 loop = new Vector2(center.x + side * LINK_LOOP_OFFSET, center.y);
                painter.BeginPath();
                painter.Arc(
                    loop,
                    LINK_LOOP_RADIUS,
                    new Angle(0f, AngleUnit.Degree),
                    new Angle(360f, AngleUnit.Degree));
                painter.ClosePath();
                painter.Stroke();
            }

            if (!_keepRatio)
            {
                return;
            }

            // つながっているときだけ 2 つの輪を橋渡しする（切れていれば隙間がそのまま「外れている」記号になる）
            painter.lineWidth = LINK_BAR_WIDTH;
            painter.BeginPath();
            painter.MoveTo(new Vector2(center.x - LINK_LOOP_OFFSET, center.y));
            painter.LineTo(new Vector2(center.x + LINK_LOOP_OFFSET, center.y));
            painter.Stroke();
        }

        void Notify(Vector2 previous, Vector2 current)
        {
            if (this.panel != null)
            {
                using (ChangeEvent<Vector2> changeEvent = ChangeEvent<Vector2>.GetPooled(previous, current))
                {
                    changeEvent.target = this;
                    this.SendEvent(changeEvent);
                }
            }

            ValueChanged?.Invoke(current);
        }

        #endregion
    }
}
