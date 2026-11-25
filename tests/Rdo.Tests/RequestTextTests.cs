using Rdo.Core.Intake;
using Xunit;

namespace Rdo.Tests;

public class RequestTextTests
{
    private static readonly DateOnly Today = new(2027, 3, 1);

    [Theory]
    [InlineData("bis KW 14", "2027-04-09")]
    [InlineData("by week 14", "2027-04-09")]
    [InlineData("bis 05.05.2027 fertig", "2027-05-05")]
    [InlineData("finish by 2027-06-30", "2027-06-30")]
    [InlineData("bis KW 2", "2028-01-14")]
    public void Deadlines_are_read_by_code(string text, string expected) =>
        Assert.Equal(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), RequestText.Deadline(text, Today));

    [Theory]
    [InlineData("auf Compact C1 VA", "front")]
    [InlineData("for the Van V2 rear axle", "rear")]
    [InlineData("auf Midsize M3 Hinterachse", "rear")]
    [InlineData("for the SUV U1", null)]
    [InlineData("front and rear", null)]
    public void Axle_is_only_what_the_words_say(string text, string? expected) => Assert.Equal(expected, RequestText.Axle(text));

    [Fact]
    public void Formulation_codes_in_any_spelling() =>
        Assert.Equal(["FM-2231", "FM-1999"], RequestText.FormulationCodes("FM 2231 statt fm-1999"));

    [Fact]
    public void Samples_in_german_and_english()
    {
        Assert.Equal(3, RequestText.Samples("3 Muster liegen bereit"));
        Assert.Equal(2, RequestText.Samples("2 pad sets are ready"));
        Assert.Null(RequestText.Samples("no count here"));
    }
}
