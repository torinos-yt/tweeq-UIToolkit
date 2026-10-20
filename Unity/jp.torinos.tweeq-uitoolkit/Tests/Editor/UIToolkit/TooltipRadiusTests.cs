using NUnit.Framework;

namespace Tweeq.UIToolkit.Tests
{
    public class TooltipRadiusTests
    {
        [Test]
        public void Default_KeepsCapsuleForOneLineAndSixForWrapped()
        {
            TweeqTheme theme = TweeqTheme.Dark();

            Assert.AreEqual(9999f, TweeqTooltipRoot.ResolveRadius(theme, false));
            Assert.AreEqual(6f, TweeqTooltipRoot.ResolveRadius(theme, true));
        }

        [Test]
        public void RadiusTooltip_AppliesToBothShapes()
        {
            TweeqTheme theme = TweeqTheme.Dark();
            theme.RadiusTooltip = 4f;

            Assert.AreEqual(4f, TweeqTooltipRoot.ResolveRadius(theme, false));
            Assert.AreEqual(4f, TweeqTooltipRoot.ResolveRadius(theme, true));
        }
    }
}
