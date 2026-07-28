using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// 3 軸固定の数値タプル（仕様 §2）。既定の軸ラベルは X / Y / Z。
    /// </summary>
    /// <remarks>
    /// 配列版の <see cref="VecInput"/> と違い、値が構造体なので通知経路で 1 バイトも確保しない。
    /// 軸ドラッグ中は毎フレーム通知が走るため、Unity では基本的にこちらを使う。
    /// </remarks>
    [UxmlElement]
    public partial class Vec3Input : VecInputBase, INotifyValueChanged<Vector3>
    {
        #region Constants

        const int DIMENSIONS = 3;

        #endregion

        #region Public API

        /// <summary>
        /// 値が変わるたびに発火する。1 ジェスチャで動くのは 1 軸だけなので、
        /// 仕様 §2 の「1 フレーム 1 回」はコアレスなしで満たされる。
        /// </summary>
        public event Action<Vector3> ValueChanged;

        /// <summary>ドラッグ確定・Enter・blur で 1 回だけ発火する（軸数ぶんは発火しない）。</summary>
        public event Action<Vector3> Confirmed;

        /// <summary>
        /// 現在値。<c>INotifyValueChanged</c> の規約に合わせて名前だけ小文字にしている。
        /// </summary>
        [UxmlAttribute]
        public Vector3 value
        {
            get => ReadValue();
            set
            {
                Vector3 previous = ReadValue();
                WriteAxes(value);
                Vector3 current = ReadValue();

                // 比較は軸から読み直した値どうしで行う（軸の保持値が唯一の正）。
                // Vector3.Equals は成分ごとの厳密比較なので、== の近似判定で潰されない
                if (previous.Equals(current))
                {
                    return;
                }

                Notify(previous, current);
            }
        }

        /// <summary>イベントを発火せずに値を設定する。</summary>
        public void SetValueWithoutNotify(Vector3 newValue)
        {
            WriteAxes(newValue);
        }

        #endregion

        #region Construction

        public Vec3Input() : base(DIMENSIONS)
        {
        }

        #endregion

        #region Notification

        protected override void OnAxesChanged(int changedAxis, float previousAxisValue)
        {
            Vector3 current = ReadValue();

            // 動いたのは 1 軸だけなので、その成分を旧値へ差し戻せば変更前の値になる
            Vector3 previous = current;
            if (changedAxis >= 0 && changedAxis < DIMENSIONS)
            {
                previous[changedAxis] = previousAxisValue;
            }

            Notify(previous, current);
        }

        protected override void OnConfirmed()
        {
            Confirmed?.Invoke(ReadValue());
        }

        void Notify(Vector3 previous, Vector3 current)
        {
            this.SendChangeEvent(previous, current);
            ValueChanged?.Invoke(current);
        }

        #endregion

        #region Internals

        Vector3 ReadValue()
        {
            return new Vector3(this.GetAxisValue(0), this.GetAxisValue(1), this.GetAxisValue(2));
        }

        // 第 4 引数は軸数 3 の時点で基底に捨てられる
        void WriteAxes(Vector3 source)
        {
            this.SetAxesWithoutNotify(source.x, source.y, source.z, 0f);
        }

        #endregion
    }
}
