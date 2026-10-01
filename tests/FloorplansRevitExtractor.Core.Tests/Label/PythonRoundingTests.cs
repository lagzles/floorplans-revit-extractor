using FloorplansRevitExtractor.Core.Label;
using FluentAssertions;
using Xunit;

namespace FloorplansRevitExtractor.Core.Tests.Label
{
    public class PythonRoundingTests
    {
        [Theory]
        [InlineData(137.65, 1, 137.7)] // binary value is 137.6500000000000056..., so it rounds up (found against the generator)
        [InlineData(2.675, 2, 2.67)] // binary value is 2.67499999999999982..., so it rounds down
        [InlineData(0.125, 2, 0.12)] // exact tie in binary: half to even
        [InlineData(0.375, 2, 0.38)]
        [InlineData(-137.65, 1, -137.7)]
        [InlineData(0.67354, 5, 0.67354)]
        [InlineData(1234567.891, 0, 1234568.0)]
        [InlineData(2.5, 0, 2.0)]
        [InlineData(0.0, 3, 0.0)]
        public void Round_MatchesPythonRoundOnTheExactBinaryValue(double value, int digits, double expected)
        {
            PythonRounding.Round(value, digits).Should().Be(expected);
        }

        [Theory]
        [InlineData(39.5, 40)]
        [InlineData(49.5, 50)]
        [InlineData(14.5, 14)]
        [InlineData(-0.5, 0)]
        public void RoundToInt_IsHalfToEven(double value, int expected)
        {
            PythonRounding.RoundToInt(value).Should().Be(expected);
        }
    }
}
