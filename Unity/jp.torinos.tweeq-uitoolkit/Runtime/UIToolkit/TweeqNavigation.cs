using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// キーボードナビゲーションをパネル単位で調整するヘルパー（feedback-fixes-01.md C-3）。
    /// tweeq のコントロールは矢印キーを値操作に使うため、パネル全体でフォーカス移動を
    /// 止めたいことが多い。ただしライブラリ既定では強制せず、呼び出し側のオプトインにする。
    /// </summary>
    public static class TweeqNavigation
    {
        /// <summary>
        /// root 配下で ↑↓←→ によるフォーカス移動を止める。Tab（Next / Previous）は素通しする。
        /// 既に無効化済みの root へ再度呼んでも二重登録にはならない。
        /// </summary>
        /// <param name="root">対象のルート要素。null なら何もしない。</param>
        public static void DisableArrowFocusNavigation(VisualElement root)
        {
            if (root == null)
            {
                return;
            }

            // 個々のコントロール（NumberInput / RadioInput）より先に食い止めたいので TrickleDown。
            // panel が未接続でも登録自体は可能なので、ここでは panel を要求しない
            root.RegisterCallback<NavigationMoveEvent>(OnNavigationMove, TrickleDown.TrickleDown);
        }

        /// <summary>
        /// <see cref="DisableArrowFocusNavigation" /> を取り消して既定の挙動へ戻す。
        /// 登録していない root へ呼んでも無害。
        /// </summary>
        /// <param name="root">対象のルート要素。null なら何もしない。</param>
        public static void EnableArrowFocusNavigation(VisualElement root)
        {
            if (root == null)
            {
                return;
            }

            root.UnregisterCallback<NavigationMoveEvent>(OnNavigationMove, TrickleDown.TrickleDown);
        }

        // コールバックを static メソッドにしてあるので、root → デリゲートの対応表
        // （ConditionalWeakTable や static Dictionary）を一切持たない。
        // 対応表を持つと static が root を強参照してツリーごとリークする（ConditionalWeakTable なら
        // 避けられるが、そもそも不要）。static メソッド由来のデリゲートは target が null で
        // メソッドも同一なので、Register / Unregister が UI Toolkit の等価判定で必ず一致する。
        // ＝「状態を持たない」が最も単純でリークしない解になる
        static void OnNavigationMove(NavigationMoveEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            switch (evt.direction)
            {
                case NavigationMoveEvent.Direction.Up:
                case NavigationMoveEvent.Direction.Down:
                case NavigationMoveEvent.Direction.Left:
                case NavigationMoveEvent.Direction.Right:
                    break;

                default:
                    // Next / Previous（Tab）はフォーカス移動そのものが目的なので触らない
                    return;
            }

            evt.StopPropagation();

            // Unity 6 で「フォーカス移動そのもの」を止められるのは IgnoreEvent（PreventDefault は非推奨）。
            // 登録時点では panel が無いこともあるので、focusController は毎回 currentTarget から引く
            VisualElement target = evt.currentTarget as VisualElement;
            target?.focusController?.IgnoreEvent(evt);
        }
    }
}
