using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// 任意の要素にツールチップを付けるための入口（Vue 版 v-tooltip ディレクティブ相当）。
    /// 実体はパネルごとの <see cref="TweeqTooltipRoot"/> 1 個を全員で使い回す。
    /// </summary>
    public static class TweeqTooltip
    {
        #region Fields

        // 要素 → 購読の対応表。Detach で確実に取り除くので、ここが唯一の保持元になる
        static readonly Dictionary<VisualElement, TooltipBinding> Bindings =
            new Dictionary<VisualElement, TooltipBinding>();

        #endregion

        #region Public API

        /// <summary>
        /// target にツールチップを付ける。既に付いていれば文言の差し替えとして働く
        /// （表示中ならその場で反映される）。
        /// </summary>
        public static void Attach(VisualElement target, string text)
        {
            if (target == null)
            {
                return;
            }

            if (Bindings.TryGetValue(target, out TooltipBinding existing))
            {
                existing.SetText(text);
                return;
            }

            Bindings.Add(target, new TooltipBinding(target, text));
        }

        /// <summary>
        /// target からツールチップを外す。表示中なら即座に閉じ、購読も全て解除するので
        /// 参照が残らない。
        /// </summary>
        public static void Detach(VisualElement target)
        {
            if (target == null)
            {
                return;
            }

            if (!Bindings.TryGetValue(target, out TooltipBinding binding))
            {
                return;
            }

            Bindings.Remove(target);
            binding.Dispose();
        }

        /// <summary>target にツールチップが付いているか。</summary>
        public static bool IsAttached(VisualElement target)
        {
            return target != null && Bindings.ContainsKey(target);
        }

        /// <summary>
        /// context のパネルで共有しているツールチップの配色を差し替える。
        /// 実体が 1 個なので、アプリ起動時に一度呼べば全要素へ効く。
        /// </summary>
        public static void SetTheme(VisualElement context, TweeqTheme theme)
        {
            TweeqTooltipRoot root = TweeqTooltipRoot.GetOrCreate(context);
            if (root == null)
            {
                return;
            }

            root.Theme = theme;
        }

        #endregion

        #region Binding

        /// <summary>1 要素ぶんの購読。デリゲートは生成時に確保して登録／解除で使い回す。</summary>
        sealed class TooltipBinding
        {
            #region Fields

            readonly VisualElement _target;
            readonly EventCallback<PointerEnterEvent> _onPointerEnter;
            readonly EventCallback<PointerLeaveEvent> _onPointerLeave;
            readonly EventCallback<FocusInEvent> _onFocusIn;
            readonly EventCallback<FocusOutEvent> _onFocusOut;
            readonly EventCallback<DetachFromPanelEvent> _onDetachFromPanel;

            string _text;

            #endregion

            #region Construction

            public TooltipBinding(VisualElement target, string text)
            {
                _target = target;
                _text = text;

                _onPointerEnter = OnPointerEnter;
                _onPointerLeave = OnPointerLeave;
                _onFocusIn = OnFocusIn;
                _onFocusOut = OnFocusOut;
                _onDetachFromPanel = OnDetachFromPanel;

                _target.RegisterCallback(_onPointerEnter);
                _target.RegisterCallback(_onPointerLeave);
                _target.RegisterCallback(_onFocusIn);
                _target.RegisterCallback(_onFocusOut);
                _target.RegisterCallback(_onDetachFromPanel);
            }

            #endregion

            #region API

            public void SetText(string text)
            {
                _text = text;
                TweeqTooltipRoot.GetOrCreate(_target)?.SetText(_target, _text);
            }

            public void Dispose()
            {
                _target.UnregisterCallback(_onPointerEnter);
                _target.UnregisterCallback(_onPointerLeave);
                _target.UnregisterCallback(_onFocusIn);
                _target.UnregisterCallback(_onFocusOut);
                _target.UnregisterCallback(_onDetachFromPanel);

                TweeqTooltipRoot.CloseAnyFor(_target);
            }

            #endregion

            #region Events

            void OnPointerEnter(PointerEnterEvent evt)
            {
                RequestShow();
            }

            void OnPointerLeave(PointerLeaveEvent evt)
            {
                RequestHide();
            }

            void OnFocusIn(FocusInEvent evt)
            {
                RequestShow();
            }

            void OnFocusOut(FocusOutEvent evt)
            {
                RequestHide();
            }

            // パネルから外れた要素は leave を受け取れないので、ここで取り残しを断つ
            void OnDetachFromPanel(DetachFromPanelEvent evt)
            {
                TweeqTooltipRoot.CloseAnyFor(_target);
            }

            void RequestShow()
            {
                if (string.IsNullOrEmpty(_text))
                {
                    return;
                }

                TweeqTooltipRoot.GetOrCreate(_target)?.Show(_target, _text);
            }

            void RequestHide()
            {
                TweeqTooltipRoot.GetOrCreate(_target)?.Hide(_target);
            }

            #endregion
        }

        #endregion
    }
}
