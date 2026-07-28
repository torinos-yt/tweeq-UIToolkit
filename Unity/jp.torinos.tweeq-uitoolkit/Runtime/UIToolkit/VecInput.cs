using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// 次元数を実行時に決める数値タプル（仕様 §2）。軸数が固定なら
    /// <see cref="Vec2Input"/> / <see cref="Vec3Input"/> / <see cref="Vec4Input"/> を使う。
    /// </summary>
    /// <remarks>
    /// 値型が配列なので <c>INotifyValueChanged&lt;T&gt;</c> は採用していない（仕様 §5-3 の意図的逸脱）。
    /// 出入りは常に防御的コピーで、内部配列の参照は外へ出さない。
    /// そのぶん通知のたびに配列を 1 本作るので、ドラッグ中の GC を嫌う用途では typed 版を選ぶ。
    /// </remarks>
    [UxmlElement]
    public partial class VecInput : VecInputBase
    {
        #region Constants

        // UXML 経由（＝パラメータなし生成）の軸数。Dimensions は配列を確保した後は動かせないので
        // UXML 属性にできず、既定を最小軸数に置く
        const int UXML_DIMENSIONS = 2;

        #endregion

        #region Public API

        /// <summary>
        /// 値が変わるたびに発火する。1 ジェスチャで動くのは 1 軸だけなので、
        /// 仕様 §2 の「1 フレーム 1 回」はコアレスなしで満たされる。
        /// </summary>
        public event Action<float[]> ValueChanged;

        /// <summary>ドラッグ確定・Enter・blur で 1 回だけ発火する（軸数ぶんは発火しない）。</summary>
        public event Action<float[]> Confirmed;

        /// <summary>
        /// 現在値。get は複製を返し、set は複製を受け取る。
        /// UXML から与える場合は長さを軸数（既定 <see cref="UXML_DIMENSIONS"/>）に合わせる
        /// （合わない配列は警告を出して無視される）。
        /// </summary>
        [UxmlAttribute]
        public float[] Value
        {
            get => ReadValue();
            set
            {
                if (!SetValueWithoutNotifyInternal(value))
                {
                    return;
                }

                RaiseValueChanged();
            }
        }

        /// <summary>イベントを発火せずに値を設定する。長さが軸数と違う場合は無視する。</summary>
        public void SetValueWithoutNotify(float[] value)
        {
            SetValueWithoutNotifyInternal(value);
        }

        #endregion

        #region Construction

        /// <summary>
        /// UXML / UI Builder から生成するための既定コンストラクタ。軸数は
        /// <see cref="UXML_DIMENSIONS"/>。軸数を選ぶなら <see cref="VecInput(int)"/> を使う。
        /// </summary>
        public VecInput() : base(UXML_DIMENSIONS)
        {
        }

        public VecInput(int dimensions) : base(dimensions)
        {
        }

        #endregion

        #region Notification

        protected override void OnAxesChanged(int changedAxis, float previousAxisValue)
        {
            // 配列版は「どの軸が」を区別しないので、そのまま現在値を配って終わり
            RaiseValueChanged();
        }

        protected override void OnConfirmed()
        {
            Confirmed?.Invoke(ReadValue());
        }

        void RaiseValueChanged()
        {
            ValueChanged?.Invoke(ReadValue());
        }

        #endregion

        #region Internals

        bool SetValueWithoutNotifyInternal(float[] value)
        {
            if (value == null)
            {
                Debug.LogWarning("VecInput: null は設定できないため無視した。");
                return false;
            }

            if (value.Length != this.Dimensions)
            {
                Debug.LogWarning(
                    $"VecInput: 長さ {value.Length} は軸数 {this.Dimensions} と一致しないため無視した。");
                return false;
            }

            // 軸数は 2〜4 に丸められているので、超過分は基底が捨てる
            this.SetAxesWithoutNotify(
                value[0],
                value[1],
                this.Dimensions > 2 ? value[2] : 0f,
                this.Dimensions > 3 ? value[3] : 0f);

            return true;
        }

        float[] ReadValue()
        {
            float[] snapshot = new float[this.Dimensions];

            for (int i = 0; i < snapshot.Length; i++)
            {
                snapshot[i] = this.GetAxisValue(i);
            }

            return snapshot;
        }

        #endregion
    }
}
