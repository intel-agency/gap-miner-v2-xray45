using GapMiner.Domain.ValueObjects;
using Xunit;

namespace GapMiner.Domain.Tests;

public class SeverityScoreTests
{
    [Theory]
    [InlineData(1.0)]
    [InlineData(5.5)]
    [InlineData(10.0)]
    public void Create_AcceptsValuesWithinInclusiveRange(double value)
    {
        var score = SeverityScore.Create(value);

        Assert.Equal(value, score.Value);
    }

    [Theory]
    [InlineData(0.99)]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(10.01)]
    [InlineData(double.MaxValue)]
    public void Create_RejectsValuesOutsideRange(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SeverityScore.Create(value));
    }

    [Fact]
    public void Create_RejectsNaN()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SeverityScore.Create(double.NaN));
    }

    [Fact]
    public void Create_RejectsInfinity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SeverityScore.Create(double.PositiveInfinity));
    }

    [Fact]
    public void Create_ExceptionNamesTheParameterAndRange()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => SeverityScore.Create(42.0));

        Assert.Equal("value", ex.ParamName);
        Assert.Contains("1", ex.Message, StringComparison.Ordinal);
        Assert.Contains("10", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EqualValues_AreEqual_ValueObjectSemantics()
    {
        Assert.Equal(SeverityScore.Create(7.2), SeverityScore.Create(7.2));
        Assert.NotEqual(SeverityScore.Create(7.2), SeverityScore.Create(7.3));
        Assert.True(SeverityScore.Create(7.2) == SeverityScore.Create(7.2));
    }
}
