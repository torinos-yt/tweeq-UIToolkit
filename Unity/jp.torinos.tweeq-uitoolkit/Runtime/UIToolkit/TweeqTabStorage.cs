using System;
using UnityEngine;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// <see cref="TweeqTabs"/> がアクティブタブ id を読み書きする先（M8 仕様 §D「永続化」）。
    /// </summary>
    /// <remarks>
    /// 既定実装は <see cref="TweeqTabPlayerPrefsStorage"/>。テストや、設定ファイルを自前で
    /// 持つアプリケーションは <see cref="TweeqTabs.Storage"/> に差し替える。
    /// 実装は例外を投げないこと（公演現場でのランタイム例外＝事故）。
    /// </remarks>
    public interface ITweeqTabStorage
    {
        /// <summary>保存値を読む。未保存なら <paramref name="defaultValue"/> をそのまま返す。</summary>
        string Get(string key, string defaultValue);

        /// <summary>値を保存する。</summary>
        void Set(string key, string value);

        /// <summary>保存値を消す（＝既定へ戻す）。</summary>
        void Delete(string key);
    }

    /// <summary>
    /// <see cref="ITweeqTabStorage"/> の既定実装。Vue 版の localStorage に対応する位置づけ。
    /// </summary>
    /// <remarks>
    /// バッチモードやサンドボックスでは PlayerPrefs が使えないことがある。タブの選択状態の
    /// 保存で例外を投げて上位を止めるのは割に合わないので、握って警告だけ出す
    /// （<see cref="ParameterGroup"/> の開閉状態の保存と同じ方針）。
    /// </remarks>
    public sealed class TweeqTabPlayerPrefsStorage : ITweeqTabStorage
    {
        /// <summary>
        /// 共有インスタンス。<see cref="TweeqTabs.Storage"/> に null を代入したときの戻り先なので、
        /// テストは「差し替えを解除できたか」をこの参照で確かめられる。
        /// </summary>
        public static readonly TweeqTabPlayerPrefsStorage Instance = new TweeqTabPlayerPrefsStorage();

        /// <inheritdoc />
        public string Get(string key, string defaultValue)
        {
            if (string.IsNullOrEmpty(key))
            {
                return defaultValue;
            }

            try
            {
                return PlayerPrefs.GetString(key, defaultValue);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"{nameof(TweeqTabPlayerPrefsStorage)}: 読み込めない（{key}）: {exception.Message}");
                return defaultValue;
            }
        }

        /// <inheritdoc />
        public void Set(string key, string value)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            try
            {
                PlayerPrefs.SetString(key, value ?? string.Empty);
                PlayerPrefs.Save();
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"{nameof(TweeqTabPlayerPrefsStorage)}: 保存できない（{key}）: {exception.Message}");
            }
        }

        /// <inheritdoc />
        public void Delete(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            try
            {
                PlayerPrefs.DeleteKey(key);
                PlayerPrefs.Save();
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"{nameof(TweeqTabPlayerPrefsStorage)}: 削除できない（{key}）: {exception.Message}");
            }
        }
    }
}
