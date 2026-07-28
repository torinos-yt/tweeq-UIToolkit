using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// 2D 位置の複合入力（M6 第2波仕様 §C）。
    /// 左に <see cref="TranslateInput"/>、右に <see cref="Vec2Input"/> を並べ、値を双方向に同期する。
    /// </summary>
    /// <remarks>
    /// 通知は 1 系統に集約する。どちらの子で操作しても ValueChanged は毎更新 1 回、
    /// Confirmed は 1 ジェスチャ 1 回だけになる。
    /// </remarks>
    [UxmlElement]
    public partial class PositionInput : VisualElement, INotifyValueChanged<Vector2>, ITweeqThemed
    {
        #region Fields

        readonly InputGroup _group;
        readonly TranslateInput _translate;
        readonly Vec2Input _field;

        Vector2 _value;
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

        /// <summary>左のドラッグスクラバー。オーバーレイ設定などを個別に触りたい場合に使う。</summary>
        public TranslateInput Translate => _translate;

        /// <summary>右の数値タプル。軸ごとの Precision などを触りたい場合に使う。</summary>
        public Vec2Input Field => _field;

        /// <summary>配色テーマ。子へそのまま伝播する。</summary>
        public TweeqTheme Theme
        {
            get => _theme;
            set
            {
                _theme = value ?? TweeqTheme.Dark();
                _group.Theme = _theme;
                _translate.Theme = _theme;
                _field.Theme = _theme;
                ApplyBoxFusion();
            }
        }

        /// <summary>下限。スクラバーと数値欄の両方に効く。</summary>
        [UxmlAttribute]
        public Vector2 Min
        {
            get => _translate.Min;
            set
            {
                _translate.Min = value;
                _field.Min = new double[] { value.x, value.y };
            }
        }

        /// <summary>上限。スクラバーと数値欄の両方に効く。</summary>
        [UxmlAttribute]
        public Vector2 Max
        {
            get => _translate.Max;
            set
            {
                _translate.Max = value;
                _field.Max = new double[] { value.x, value.y };
            }
        }

        /// <summary>数値欄の量子化幅（両軸共通）。スクラバー側は px 1:1 なので影響しない。</summary>
        [UxmlAttribute]
        public double Step
        {
            get
            {
                NumberInput axis = _field.GetAxis(0);
                return axis != null ? axis.Step : 0.0;
            }

            set => _field.Step = new[] { value };
        }

        /// <summary>数値欄の静止時表示桁（両軸共通）。</summary>
        [UxmlAttribute]
        public int Precision
        {
            get => _field.Precision;
            set => _field.Precision = value;
        }

        /// <summary>操作不能状態。スクラバーと数値欄の両方へ配る。</summary>
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
                _translate.Disabled = _disabled;
                _field.Disabled = _disabled;
            }
        }

        /// <summary>不正値表示。数値欄だけに配る（スクラバーは Vue にも invalid 表現が無い）。</summary>
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
        /// 起きないため、外部ドライバとテストのために口を開けてある。
        /// </summary>
        public void PerformFieldConfirm()
        {
            if (_disabled)
            {
                return;
            }

            OnChildConfirmed(_value);
        }

        /// <summary>ChangeEvent を発火せずに値を設定する。</summary>
        public void SetValueWithoutNotify(Vector2 newValue)
        {
            _value = newValue;

            _syncing = true;
            _translate.SetValueWithoutNotify(newValue);
            _field.SetValueWithoutNotify(newValue);
            _syncing = false;
        }

        #endregion

        #region Construction

        public PositionInput()
        {
            this.AddToClassList("tweeq-position-input");
            this.style.flexDirection = FlexDirection.Row;
            this.style.flexGrow = 1f;

            _group = new InputGroup { Theme = _theme };

            _translate = new TranslateInput
            {
                name = "tweeq-position-translate",
                Theme = _theme,

                // Vue InputPosition は常にラベル付きで呼ぶ
                ShowOverlayLabel = true,
            };

            // スクラバーは 24px 固定。InputGroup の等分割に巻き込ませない
            _translate.style.flexGrow = 0f;
            _translate.style.flexShrink = 0f;

            _field = new Vec2Input
            {
                name = "tweeq-position-field",
                Theme = _theme,
            };

            _translate.ValueChanged += OnTranslateChanged;
            _translate.Confirmed += OnChildConfirmed;
            _field.ValueChanged += OnFieldChanged;
            _field.Confirmed += OnChildConfirmed;

            _group.Add(_translate);
            _group.Add(_field);
            this.hierarchy.Add(_group);

            // Vec2Input は ITweeqInputBox ではないので、InputGroup は端の角丸を割り当てられない。
            // グループが位置を配り直した後で上書きするため、レイアウト確定のたびに掛け直す
            this.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            ApplyBoxFusion();
        }

        #endregion

        #region Internals

        void OnGeometryChanged(GeometryChangedEvent evt)
        {
            ApplyBoxFusion();
        }

        // [Translate][X][Y] を 1 つながりに見せる。各セッタは同値なら何もしないので毎回呼んでよい
        void ApplyBoxFusion()
        {
            _translate.InlinePosition = TweeqBoxPosition.Start;

            NumberInput x = _field.GetAxis(0);
            if (x != null)
            {
                x.InlinePosition = TweeqBoxPosition.Middle;
            }

            NumberInput y = _field.GetAxis(1);
            if (y != null)
            {
                y.InlinePosition = TweeqBoxPosition.End;
            }
        }

        void OnTranslateChanged(Vector2 next)
        {
            Adopt(next, _field);
        }

        void OnFieldChanged(Vector2 next)
        {
            Adopt(next, _translate);
        }

        void Adopt(Vector2 next, INotifyValueChanged<Vector2> other)
        {
            if (_syncing || _value.Equals(next))
            {
                return;
            }

            Vector2 previous = _value;
            _value = next;

            _syncing = true;
            other.SetValueWithoutNotify(next);
            _syncing = false;

            Notify(previous, next);
        }

        void OnChildConfirmed(Vector2 childValue)
        {
            if (_syncing)
            {
                return;
            }

            Confirmed?.Invoke(_value);
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
