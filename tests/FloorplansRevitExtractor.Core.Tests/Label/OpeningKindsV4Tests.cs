using FloorplansRevitExtractor.Core.Label;
using FluentAssertions;
using Xunit;

namespace FloorplansRevitExtractor.Core.Tests.Label
{
    public class OpeningKindsV4Tests
    {
        [Theory]
        [InlineData("single", 2.99, false)]
        [InlineData("single", 3.0, true)] // spec v4: a 3.0 m window is 15
        [InlineData("curtain", 1.2, true)]
        [InlineData("high", 4.0, true)]
        public void IsCurtainWall_FollowsTheGeneratorRule(string kind, double widthM, bool expected)
        {
            OpeningKinds.IsCurtainWall(kind, widthM).Should().Be(expected);
        }

        [Theory]
        [InlineData("passage", true, "passage")]
        [InlineData("single", false, "passage")] // no leaf drawn: a passage, with no swing
        [InlineData("single", true, "single")]
        [InlineData("double", false, "passage")]
        [InlineData("sliding", false, "sliding")] // a sliding door has a leaf
        [InlineData("gate", false, "gate")]
        public void DoorKindForMask_ALeaflessOpeningIsAPassage(string kind, bool drawsLeaf, string expected)
        {
            OpeningKinds.DoorKindForMask(kind, drawsLeaf).Should().Be(expected);
        }

        [Fact]
        public void WindowKind_StorefrontIsCurtain_EvenWithAHighSill()
        {
            OpeningKinds.WindowKind("Storefront Window", "Type A", 2.0).Should().Be("curtain");
            OpeningKinds.WindowKind("Fixed", "Type A", 2.0).Should().Be("high");
        }
    }
}
