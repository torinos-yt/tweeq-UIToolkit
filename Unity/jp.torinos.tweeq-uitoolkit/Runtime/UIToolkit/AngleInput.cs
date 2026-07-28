using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// 角度の複合入力（Vue InputAngle 相当）。左に <see cref="RotaryInput"/>、右に度数表示の
    /// <see cref="NumberInput"/> を並べ、値を双方向に同期する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 幅が足りないときは Vue 版と同じく数値欄を畳んでノブだけにする
    /// （閾値は <c>theme.inputHeight * 4</c>）。
    /// </para>
    /// <para>
    /// 通知は 1 系統に集約する。どちらの子で操作しても ValueChanged は毎更新 1 回、
    /// Confirmed は 1 ジェスチャ 1 回だけになる。
    /// </para>
    /// <para>
    /// Vue 版は 2 つを gap-control（9px）で離して置くが、こちらは <see cref="InputGroup"/> で
    /// 融合させる。Unity 側では「1 つの値を 2 つの口で編集する」ことが角丸のつながりで
    /// 読み取れる方が、離して置くより意図が伝わるため。
    /// </para>
    /// </remarks>
    [UxmlElement]
    public partial class AngleInput : VisualElement, INotifyValueChanged<float>, ITweeqInputBox, ITweeqThemed
    {
        #region Constants

        // Vue: showNumber = width > theme.inputHeight * 4
        const float SHOW_NUMBER_WIDTH_FACTOR = 4f;

        const string DEGREE_SUFFIX = "°";

        #endregion

        #region Fields

        readonly InputGroup _group;
        readonly RotaryInput _rotary;
        readonly NumberInput _number;

        float _value;
        bool _disabled;
        bool _invalid;
        TweeqTheme _theme = TweeqTheme.Dark();

        TweeqBoxPosition _inlinePosition = TweeqBoxPosition.None;
        TweeqBoxPosition _blockPosition = TweeqBoxPosition.None;

        // Vue の useElementSize は初回計測まで 0 を返すので、既定は「畳んだ状態」で揃える
        bool _showNumber;

        // 子へ書き戻している最中の通知を自分の入力と誤認しないためのガード
        bool _syncing;

        #endregion

        #region Public API

        /// <summary>値が変わるたびに発火する。</summary>
        public event Action<float> ValueChanged;

        /// <summary>ドラッグ確定・Enter・blur で 1 ジェスチャ 1 回だけ発火する。</summary>
        public event Action<float> Confirmed;

        /// <summary>現在の角度（度数）。</summary>
        [UxmlAttribute]
        public float value
        {
            get => _value;
            set
            {
                if (_value == value)
                {
                    return;
                }

                float previous = _value;
                SetValueWithoutNotify(value);
                Notify(previous, _value);
            }
        }

        /// <summary>左のノブ。オーバーレイ設定などを個別に触りたい場合に使う。</summary>
        public RotaryInput Rotary => _rotary;

        /// <summary>右の数値欄。Bar や SnapStep などを個別に触りたい場合に使う。</summary>
        public NumberInput Number => _number;

        /// <summary>数値欄が表示されているか。幅の判定結果。</summary>
        public bool ShowsNumber => _showNumber;

        /// <summary>配色テーマ。子へそのまま伝播する。</summary>
        public TweeqTheme Theme
        {
            get => _theme;
            set
            {
                _theme = value ?? TweeqTheme.Dark();
                _group.Theme = _theme;
                _rotary.Theme = _theme;
                _number.Theme = _theme;
                ApplyRotarySize();
                ApplyBoxFusion();
            }
        }

        /// <summary>スナップ角度（度数）。既定 45。ノブ側だけの概念なので数値欄へは配らない。</summary>
        [UxmlAttribute]
        public double Snap
        {
            get => _rotary.Snap;
            set => _rotary.Snap = value;
        }

        /// <summary>インジケータの角度オフセット（度数）。</summary>
        [UxmlAttribute]
        public double AngleOffset
        {
            get => _rotary.AngleOffset;
            set => _rotary.AngleOffset = value;
        }

        /// <summary>
        /// 量子化幅。ノブと数値欄の両方へ配る。
        /// 片方だけに掛けると、ノブ由来の生の角度がそのまま欄に流れ込んで粒度が揃わない。
        /// </summary>
        [UxmlAttribute]
        public double Step
        {
            get => _number.Step;
            set
            {
                _rotary.Step = value;
                _number.Step = value;
            }
        }

        /// <summary>
        /// 数値欄の下限。ノブは多回転を保持する仕様なのでクランプしない
        /// （Vue の InputAngle も min/max を持たず、ノブ側は素通し）。
        /// </summary>
        [UxmlAttribute]
        public double Min
        {
            get => _number.Min;
            set => _number.Min = value;
        }

        /// <summary>数値欄の上限。解釈は <see cref="Min"/> と同じ。</summary>
        [UxmlAttribute]
        public double Max
        {
            get => _number.Max;
            set => _number.Max = value;
        }

        /// <summary>数値欄の静止時表示桁。</summary>
        [UxmlAttribute]
        public int Precision
        {
            get => _number.Precision;
            set => _number.Precision = value;
        }

        /// <summary>操作不能状態。ノブと数値欄の両方へ配る。</summary>
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
                _rotary.Disabled = _disabled;
                _number.Disabled = _disabled;
            }
        }

        /// <summary>不正値表示。数値欄だけに配る（ノブに invalid 表現は無い）。</summary>
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
                _number.Invalid = _invalid;
            }
        }

        /// <summary>横方向グループでの位置。中の 2 部品へ分解して配り直す。</summary>
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
                ApplyBoxFusion();
            }
        }

        /// <summary>縦方向グループでの位置。2 部品は横並びなので、そのまま両方へ配る。</summary>
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
                ApplyBoxFusion();
            }
        }

        /// <summary>
        /// レイアウト幅を与えて数値欄の表示可否を引き直す。
        /// 通常は GeometryChangedEvent から自動で呼ばれるが、レイアウトが走らない環境
        /// （EditMode テスト・未アタッチ）から駆動するために口を開けてある。
        /// </summary>
        public void PerformResize(float width)
        {
            if (float.IsNaN(width))
            {
                return;
            }

            float threshold = (_theme != null ? _theme.InputHeight : 0f) * SHOW_NUMBER_WIDTH_FACTOR;
            SetShowNumber(width > threshold);
        }

        /// <summary>
        /// ノブ側の値変更を再現する。RotaryInput の ChangeEvent は panel が無いと配送されないため、
        /// 外部ドライバとテストのために口を開けてある。
        /// </summary>
        public void PerformRotaryEdit(float newValue)
        {
            // Disabled 中は実操作が届かないので、この口も塞いでおく（挙動を実経路と一致させる）
            if (_disabled)
            {
                return;
            }

            // 実経路でも値を持っているのは子なので、先に子へ書いてから集約へ流す
            _rotary.SetValueWithoutNotify(newValue);
            Adopt(newValue, _number);
        }

        /// <summary>数値欄側の値変更を再現する。用途は <see cref="PerformRotaryEdit"/> と同じ。</summary>
        public void PerformNumberEdit(float newValue)
        {
            if (_disabled)
            {
                return;
            }

            _number.SetValueWithoutNotify(newValue);
            Adopt(newValue, _rotary);
        }

        /// <summary>
        /// ジェスチャ確定を発火する。子の Confirmed は panel 上の操作でしか起きないため、
        /// 外部ドライバとテストのために口を開けてある。
        /// </summary>
        public void PerformConfirm()
        {
            if (_disabled)
            {
                return;
            }

            OnChildConfirmed(_value);
        }

        /// <summary>ChangeEvent を発火せずに値を設定する。</summary>
        public void SetValueWithoutNotify(float newValue)
        {
            _value = newValue;

            _syncing = true;
            _rotary.SetValueWithoutNotify(newValue);
            _number.SetValueWithoutNotify(newValue);
            _syncing = false;
        }

        #endregion

        #region Construction

        public AngleInput()
        {
            this.AddToClassList("tweeq-angle-input");
            this.style.flexDirection = FlexDirection.Row;
            this.style.flexGrow = 1f;

            _group = new InputGroup { Theme = _theme };

            _rotary = new RotaryInput
            {
                name = "tweeq-angle-rotary",
                Theme = _theme,
            };

            // InputGroup.ApplyStretch は flexBasis 未指定の子へ basis 0 を配る。
            // width より basis が勝つため、明示しないと 24px のノブがゼロ幅まで潰れる
            _rotary.style.flexGrow = 0f;
            _rotary.style.flexShrink = 0f;

            _number = new NumberInput
            {
                name = "tweeq-angle-number",
                Theme = _theme,
                Suffix = DEGREE_SUFFIX,
            };
            _number.style.flexGrow = 1f;
            _number.style.flexBasis = 0f;

            // 子の値変更は ChangeEvent でしか出てこない（両者とも ValueChanged を持たない）
            _rotary.RegisterValueChangedCallback(OnRotaryChanged);
            _number.RegisterValueChangedCallback(OnNumberChanged);
            _rotary.Confirmed += OnChildConfirmed;
            _number.Confirmed += OnChildConfirmed;

            _group.Add(_rotary);
            _group.Add(_number);
            this.hierarchy.Add(_group);

            // InputGroup は畳んだ数値欄も 1 個の箱として数えるので、角丸は自前で配り直す。
            // グループが位置を配った後で上書きするため、レイアウト確定のたびに掛ける
            this.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);

            ApplyRotarySize();
            ApplyNumberVisibility();
            ApplyBoxFusion();
        }

        #endregion

        #region Internals

        void OnGeometryChanged(GeometryChangedEvent evt)
        {
            if (evt != null)
            {
                PerformResize(evt.newRect.width);
            }

            // InputGroup は attach のたびに gap と角丸を配り直すので、
            // 畳んだ数値欄ぶんの上書きはレイアウト確定のたびに掛け直す
            // （各セッタは同値なら何もしないので毎回呼んでよい）
            ApplyNumberVisibility();
            ApplyBoxFusion();
        }

        void ApplyRotarySize()
        {
            float size = _theme != null ? _theme.InputHeight : 0f;
            _rotary.style.width = size;
            _rotary.style.height = size;
            _rotary.style.flexBasis = size;
        }

        void SetShowNumber(bool show)
        {
            if (_showNumber == show)
            {
                return;
            }

            _showNumber = show;
            ApplyNumberVisibility();
            ApplyBoxFusion();
        }

        void ApplyNumberVisibility()
        {
            _number.style.display = _showNumber ? DisplayStyle.Flex : DisplayStyle.None;

            // InputGroup の gap は「末尾以外」に配られるので、畳んだ側の余白は自分で外す
            float gap = _showNumber && _theme != null ? _theme.GapGroup : 0f;
            _rotary.style.marginRight = gap;
        }

        // 外側から受けた位置を [ノブ][数値欄] の 2 箱へ分解する。
        // ノブは円形で潰す角を持たない（RotaryInput 側で no-op）が、単独表示の判定には要る
        void ApplyBoxFusion()
        {
            bool roundStart = _inlinePosition == TweeqBoxPosition.None
                || _inlinePosition == TweeqBoxPosition.Start;
            bool roundEnd = _inlinePosition == TweeqBoxPosition.None
                || _inlinePosition == TweeqBoxPosition.End;

            if (_showNumber)
            {
                _rotary.InlinePosition = roundStart
                    ? TweeqBoxPosition.Start
                    : TweeqBoxPosition.Middle;
                _number.InlinePosition = roundEnd
                    ? TweeqBoxPosition.End
                    : TweeqBoxPosition.Middle;
            }
            else
            {
                _rotary.InlinePosition = _inlinePosition;
            }

            _rotary.BlockPosition = _blockPosition;
            _number.BlockPosition = _blockPosition;
        }

        void OnRotaryChanged(ChangeEvent<float> evt)
        {
            if (evt == null)
            {
                return;
            }

            Adopt(evt.newValue, _number);
        }

        void OnNumberChanged(ChangeEvent<float> evt)
        {
            if (evt == null)
            {
                return;
            }

            Adopt(evt.newValue, _rotary);
        }

        // 変更元へは書き戻さない。ドラッグ中の子は生の累積値を持っており、
        // SetValueWithoutNotify がそれを踏み潰すとジェスチャが壊れる
        void Adopt(float next, INotifyValueChanged<float> other)
        {
            if (_syncing || _value == next)
            {
                return;
            }

            float previous = _value;
            _value = next;

            _syncing = true;
            other.SetValueWithoutNotify(next);
            _syncing = false;

            Notify(previous, next);
        }

        void OnChildConfirmed(float childValue)
        {
            if (_syncing)
            {
                return;
            }

            Confirmed?.Invoke(_value);
        }

        void Notify(float previous, float current)
        {
            if (this.panel != null)
            {
                using (ChangeEvent<float> changeEvent = ChangeEvent<float>.GetPooled(previous, current))
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
