using System.Collections.Generic;
using NUnit.Framework;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// ShuffleInput の契約（Vue InputShuffle 相当）。クリックが Generate を通ること・
    /// 通知が 1 クリック 1 組であること・サイコロの演出が値と独立であることを見る。
    /// </summary>
    /// <remarks>
    /// クリックは panel 非依存の <c>PerformClick</c> で駆動する。実ポインタ操作と描画は
    /// Play Mode 側の担当。
    /// </remarks>
    public class ShuffleInputTests
    {
        static ShuffleInput<string> Create(string initial, System.Func<string, string> generate)
        {
            ShuffleInput<string> input = new ShuffleInput<string> { Generate = generate };
            input.SetValueWithoutNotify(initial);
            return input;
        }

        #region クリック

        [Test]
        public void Click_TakesTheNextValueFromGenerate()
        {
            ShuffleInput<string> input = Create("a", previous => previous + "!");

            input.PerformClick();

            Assert.AreEqual("a!", input.value);
        }

        [Test]
        public void Click_PassesTheCurrentValueToGenerate()
        {
            List<string> seeds = new List<string>();
            ShuffleInput<string> input = Create("a", previous =>
            {
                seeds.Add(previous);
                return previous + "!";
            });

            input.PerformClick();
            input.PerformClick();

            Assert.AreEqual(2, seeds.Count);
            Assert.AreEqual("a", seeds[0]);
            Assert.AreEqual("a!", seeds[1]);
        }

        [Test]
        public void Click_RaisesValueChangedAndConfirmedOnceEach()
        {
            ShuffleInput<string> input = Create("a", _ => "b");
            int changed = 0;
            int confirmed = 0;
            string changedValue = null;
            string confirmedValue = null;

            input.ValueChanged += value =>
            {
                changed++;
                changedValue = value;
            };
            input.Confirmed += value =>
            {
                confirmed++;
                confirmedValue = value;
            };

            input.PerformClick();

            Assert.AreEqual(1, changed);
            Assert.AreEqual(1, confirmed);
            Assert.AreEqual("b", changedValue);
            Assert.AreEqual("b", confirmedValue);
        }

        [Test]
        public void Click_NotifiesEvenWhenGenerateReturnsTheSameValue()
        {
            // 「同じ値を引いた」ことも 1 回の操作なので、確定は飛ぶ
            ShuffleInput<string> input = Create("a", previous => previous);
            int confirmed = 0;
            input.Confirmed += _ => confirmed++;

            input.PerformClick();

            Assert.AreEqual(1, confirmed);
        }

        [Test]
        public void Click_WithoutGenerate_DoesNothing()
        {
            ShuffleInput<string> input = new ShuffleInput<string>();
            input.SetValueWithoutNotify("a");

            int changed = 0;
            int confirmed = 0;
            input.ValueChanged += _ => changed++;
            input.Confirmed += _ => confirmed++;

            float rotation = input.IconRotation;
            input.PerformClick();

            Assert.AreEqual(0, changed);
            Assert.AreEqual(0, confirmed);
            Assert.AreEqual("a", input.value);
            Assert.AreEqual(rotation, input.IconRotation);
        }

        [Test]
        public void Click_WhenDisabled_DoesNothing()
        {
            ShuffleInput<string> input = Create("a", _ => "b");
            input.Disabled = true;

            int confirmed = 0;
            input.Confirmed += _ => confirmed++;

            input.PerformClick();

            Assert.AreEqual(0, confirmed);
            Assert.AreEqual("a", input.value);
        }

        #endregion

        #region 値

        [Test]
        public void SetValueWithoutNotify_IsSilent()
        {
            ShuffleInput<string> input = Create("a", _ => "b");
            int changed = 0;
            input.ValueChanged += _ => changed++;

            input.SetValueWithoutNotify("z");

            Assert.AreEqual(0, changed);
            Assert.AreEqual("z", input.value);
        }

        [Test]
        public void ValueSetter_RaisesValueChangedOnce()
        {
            ShuffleInput<string> input = Create("a", _ => "b");
            int changed = 0;
            input.ValueChanged += _ => changed++;

            input.value = "z";

            Assert.AreEqual(1, changed);
            Assert.AreEqual("z", input.value);
        }

        [Test]
        public void ValueSetter_SameValueIsSilent()
        {
            ShuffleInput<string> input = Create("a", _ => "b");
            int changed = 0;
            input.ValueChanged += _ => changed++;

            input.value = "a";

            Assert.AreEqual(0, changed);
        }

        #endregion

        #region サイコロの演出

        [Test]
        public void Icon_TurnsNinetyDegreesPerClick()
        {
            ShuffleInput<string> input = Create("a", previous => previous);

            input.PerformClick();
            input.PerformClick();

            Assert.AreEqual(180f, input.IconRotation, 1e-4f);
        }

        [Test]
        public void Icon_FaceStaysWithinOneToSix()
        {
            ShuffleInput<string> input = Create("a", previous => previous);

            for (int i = 0; i < 32; i++)
            {
                input.PerformClick();

                Assert.GreaterOrEqual(input.IconFace, 1);
                Assert.LessOrEqual(input.IconFace, 6);
            }
        }

        #endregion

        #region グループ融合

        [Test]
        public void InlinePosition_RoundTrips()
        {
            ShuffleInput<string> input = Create("a", previous => previous);

            input.InlinePosition = TweeqBoxPosition.End;

            Assert.AreEqual(TweeqBoxPosition.End, input.InlinePosition);
        }

        [Test]
        public void Theme_FallsBackToDarkWhenNull()
        {
            ShuffleInput<string> input = Create("a", previous => previous);

            input.Theme = null;

            Assert.IsNotNull(input.Theme);
        }

        #endregion
    }
}
