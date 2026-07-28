using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// 4 軸固定の数値タプル（仕様 §2）。既定の軸ラベルは X / Y / Z / W。
    /// </summary>
    /// <remarks>設計意図は <see cref="Vec3Input"/> と同じ（通知経路でアロケーションしない）。</remarks>
    [UxmlElement]
    public partial class Vec4Input : VecInputBase, INotifyValueChanged<Vector4>
    {
        #region Constants

        const int DIMENSIONS = 4;

        #endregion

        #region Public API

        /// <summary>
        /// 値が変わるたびに発火する。1 ジェスチャで動くのは 1 軸だけなので、
        /// 仕様 §2 の「1 フレーム 1 回」はコアレスなしで満たされる。
        /// </summary>
        public event Action<Vector4> ValueChanged;

        /// <summary>ドラッグ確定・Enter・blur で 1 回だけ発火する（軸数ぶんは発火しない）。</summary>
        public event Action<Vector4> Confirmed;

        /// <summary>
        /// 現在値。<c>INotifyValueChanged</c> の規約に合わせて名前だけ小文字にしている。
        /// </summary>
        [UxmlAttribute]
        public Vector4 value
        {
            get => ReadValue();
            set
            {
                Vector4 previous = ReadValue();
                WriteAxes(value);
                Vector4 current = ReadValue();

                // 比較は軸から読み直した値どうしで行う（軸の保持値が唯一の正）。
                // Vector4.Equals は成分ごとの厳密比較なので、== の近似判定で潰されない
                if (previous.Equals(current))
                {
                    return;
                }

                Notify(previous, current);
            }
        }

        /// <summary>イベントを発火せずに値を設定する。</summary>
        public void SetValueWithoutNotify(Vector4 newValue)
        {
            WriteAxes(newValue);
        }

        #endregion

        #region Construction

        public Vec4Input() : base(DIMENSIONS)
        {
        }

        #endregion

        #region Notification

        protected override void OnAxesChanged(int changedAxis, float previousAxisValue)
        {
            Vector4 current = ReadValue();

            // 動いたのは 1 軸だけなので、その成分を旧値へ差し戻せば変更前の値になる
            Vector4 previous = current;
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

        void Notify(Vector4 previous, Vector4 current)
        {
            this.SendChangeEvent(previous, current);
            ValueChanged?.Invoke(current);
        }

        #endregion

        #region Internals

        Vector4 ReadValue()
        {
            return new Vector4(
                this.GetAxisValue(0),
                this.GetAxisValue(1),
                this.GetAxisValue(2),
                this.GetAxisValue(3));
        }

        void WriteAxes(Vector4 source)
        {
            this.SetAxesWithoutNotify(source.x, source.y, source.z, source.w);
        }

        #endregion
    }
}
