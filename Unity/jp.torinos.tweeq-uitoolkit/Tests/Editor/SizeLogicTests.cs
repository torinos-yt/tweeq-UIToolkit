using NUnit.Framework;
using Tweeq.Core;

namespace Tweeq.Core.Tests
{
    /// <summary>
    /// SizeLogic の契約（m6-wave2-spec.md「テスト契約」の SizeLogic 項目）。
    /// 片軸変更の比率追従・0 を含む比率・両軸同時変更での自動解除判定を見る。
    /// </summary>
    public class SizeLogicTests
    {
        const double EPSILON = 1e-9;

        static void AssertResult(double x, double y, bool keepRatio, SizeApplyResult actual)
        {
            Assert.AreEqual(x, actual.X, EPSILON, "X");
            Assert.AreEqual(y, actual.Y, EPSILON, "Y");
            Assert.AreEqual(keepRatio, actual.KeepRatio, "KeepRatio");
        }

        #region 比率追従

        [Test]
        public void Apply_LockedXChangeScalesY()
        {
            SizeApplyResult result = SizeLogic.Apply(100.0, 50.0, 200.0, 50.0, true);

            AssertResult(200.0, 100.0, true, result);
        }

        [Test]
        public void Apply_LockedYChangeScalesX()
        {
            SizeApplyResult result = SizeLogic.Apply(100.0, 50.0, 100.0, 25.0, true);

            AssertResult(50.0, 25.0, true, result);
        }

        [Test]
        public void Apply_UnlockedPassesBothAxesThrough()
        {
            SizeApplyResult result = SizeLogic.Apply(100.0, 50.0, 200.0, 50.0, false);

            AssertResult(200.0, 50.0, false, result);
        }

        [Test]
        public void Apply_BaselineIsUsedInsteadOfPreviousValue()
        {
            // ジェスチャ途中（previous は既に追従済み）でも、倍率は開始値から数える
            SizeApplyResult result = SizeLogic.Apply(200.0, 100.0, 300.0, 100.0, 100.0, 50.0, true);

            AssertResult(300.0, 150.0, true, result);
        }

        [Test]
        public void Apply_NoChangeKeepsValue()
        {
            SizeApplyResult result = SizeLogic.Apply(100.0, 50.0, 100.0, 50.0, true);

            AssertResult(100.0, 50.0, true, result);
        }

        #endregion

        #region 0 を含む比率

        [Test]
        public void Apply_ZeroBaselineOnTheDrivingAxisPassesThrough()
        {
            // 0 からは倍率を作れない。他軸は据え置きで素通しする
            SizeApplyResult result = SizeLogic.Apply(0.0, 50.0, 10.0, 50.0, true);

            AssertResult(10.0, 50.0, true, result);
        }

        [Test]
        public void Apply_ZeroOnTheFollowingAxisStaysZero()
        {
            SizeApplyResult result = SizeLogic.Apply(100.0, 0.0, 200.0, 0.0, true);

            AssertResult(200.0, 0.0, true, result);
        }

        [Test]
        public void Apply_BothZeroPassesThrough()
        {
            SizeApplyResult result = SizeLogic.Apply(0.0, 0.0, 10.0, 0.0, true);

            AssertResult(10.0, 0.0, true, result);
        }

        #endregion

        #region 自動解除

        [Test]
        public void Apply_BothAxesChangedWithNewRatioReleasesTheLock()
        {
            // 2:1 → 1:1。ユーザーが比率を崩しに来た入力なのでロックを外す
            SizeApplyResult result = SizeLogic.Apply(100.0, 50.0, 200.0, 200.0, true);

            AssertResult(200.0, 200.0, false, result);
        }

        [Test]
        public void Apply_BothAxesChangedWithSameRatioKeepsTheLock()
        {
            SizeApplyResult result = SizeLogic.Apply(100.0, 50.0, 200.0, 100.0, true);

            AssertResult(200.0, 100.0, true, result);
        }

        [Test]
        public void Apply_SingleAxisChangeNeverReleasesTheLock()
        {
            SizeApplyResult result = SizeLogic.Apply(100.0, 50.0, 999.0, 50.0, true);

            Assert.IsTrue(result.KeepRatio);
        }

        [Test]
        public void Apply_BothAxesChangedWhileUnlockedStaysUnlocked()
        {
            SizeApplyResult result = SizeLogic.Apply(100.0, 50.0, 1.0, 2.0, false);

            AssertResult(1.0, 2.0, false, result);
        }

        [Test]
        public void Apply_RatioComparisonIsToleratedForRoundingNoise()
        {
            // 200/100.0000001 は 2 とみなす。ここで解除するとドラッグ中に勝手に外れる
            SizeApplyResult result = SizeLogic.Apply(100.0, 50.0, 200.0, 100.0000001, true);

            Assert.IsTrue(result.KeepRatio);
        }

        [Test]
        public void Apply_DegenerateRatioOnBothSidesDoesNotReleaseTheLock()
        {
            // 高さ 0 のまま両軸に値が入ると比率は ±∞ どうし。同値なら「変わっていない」扱いにする
            SizeApplyResult result = SizeLogic.Apply(100.0, 0.0, 200.0, 0.0, 100.0, 0.0, true);

            Assert.IsTrue(result.KeepRatio);
        }

        [Test]
        public void Apply_LeavingADegenerateRatioReleasesTheLock()
        {
            // 高さ 0（比率 ∞）から実寸へ抜けるのは比率変更そのもの
            SizeApplyResult result = SizeLogic.Apply(100.0, 0.0, 200.0, 5.0, true);

            AssertResult(200.0, 5.0, false, result);
        }

        #endregion
    }
}
