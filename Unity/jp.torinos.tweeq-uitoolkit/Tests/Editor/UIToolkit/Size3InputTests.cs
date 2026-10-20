using NUnit.Framework;
using UnityEngine;

namespace Tweeq.UIToolkit.Tests
{
    public class Size3InputTests
    {
        const float EPSILON = 1e-3f;

        static Size3Input Create(Vector3 initial)
        {
            Size3Input input = new Size3Input();
            input.SetValueWithoutNotify(initial);
            return input;
        }

        [Test]
        public void KeepRatio_DefaultsToOn()
        {
            Size3Input input = Create(Vector3.one);

            Assert.IsTrue(input.KeepRatio);
            Assert.IsTrue(input.Chain.value);
        }

        [Test]
        public void Locked_OneAxisChangeScalesTheOthersFromTheGestureBaseline()
        {
            Size3Input input = Create(new Vector3(1f, 2f, 1f));

            input.Field.value = new Vector3(2f, 2f, 1f);
            Assert.AreEqual(new Vector3(2f, 4f, 2f).x, input.value.x, EPSILON);
            Assert.AreEqual(4f, input.value.y, EPSILON);
            Assert.AreEqual(2f, input.value.z, EPSILON);

            // Same gesture: still relative to the starting value (1,2,1), so no compounding
            input.Field.value = new Vector3(3f, input.value.y, input.value.z);
            Assert.AreEqual(3f, input.value.x, EPSILON);
            Assert.AreEqual(6f, input.value.y, EPSILON);
            Assert.AreEqual(3f, input.value.z, EPSILON);
        }

        [Test]
        public void Unlocked_OnlyTheEditedAxisChanges()
        {
            Size3Input input = Create(new Vector3(1f, 2f, 1f));
            input.KeepRatio = false;

            input.Field.value = new Vector3(2f, 2f, 1f);

            Assert.AreEqual(new Vector3(2f, 2f, 1f), input.value);
        }

        [Test]
        public void ZeroAxisStaysZero_AndZeroBaselineLeavesOthersAlone()
        {
            Size3Input input = Create(new Vector3(1f, 0f, 2f));
            input.Field.value = new Vector3(2f, 0f, 2f);
            Assert.AreEqual(new Vector3(2f, 0f, 4f), input.value);

            Size3Input fromZero = Create(new Vector3(0f, 1f, 1f));
            fromZero.Field.value = new Vector3(3f, 1f, 1f);
            Assert.AreEqual(new Vector3(3f, 1f, 1f), fromZero.value);
        }
    }
}
