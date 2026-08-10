using LibraryLoans.Domain.Members;

namespace LibraryLoans.UnitTests.Members;

public sealed class MembershipNumberTests
{
    [Theory]
    [InlineData("M00000001", "M00000001")]
    [InlineData("m00000001", "M00000001")]
    [InlineData(" M12345678 ", "M12345678")]
    public void Accepts_and_canonicalises_a_membership_number(string input, string expected)
    {
        var result = MembershipNumber.Create(input);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value.Value);
    }

    [Theory]
    [InlineData("00000001")]      // no prefix
    [InlineData("M0000001")]      // seven digits
    [InlineData("M000000012")]    // nine digits
    [InlineData("X00000001")]     // wrong prefix
    [InlineData("M0000000A")]     // not all digits
    [InlineData("MM0000001")]
    public void Rejects_anything_that_is_not_the_format(string input)
    {
        var result = MembershipNumber.Create(input);

        Assert.False(result.IsSuccess);
        Assert.Equal("member.membership_number.malformed", result.Error.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_missing_input(string? input)
    {
        var result = MembershipNumber.Create(input);

        Assert.False(result.IsSuccess);
        Assert.Equal("member.membership_number.required", result.Error.Code);
    }

    /// <summary>
    /// Bounded before anything is allocated from the input's length. The bound is generous enough
    /// for surrounding whitespace but cannot let an over-long value reach the column, because the
    /// format check pins the trimmed length exactly.
    /// </summary>
    [Fact]
    public void Rejects_input_far_longer_than_any_membership_number()
    {
        var result = MembershipNumber.Create(new string('9', 100_000));

        Assert.False(result.IsSuccess);
        Assert.Equal("member.membership_number.malformed", result.Error.Code);
    }

    [Fact]
    public void Stores_exactly_the_column_width_whatever_the_input_padding()
    {
        var result = MembershipNumber.Create("  M12345678  ");

        Assert.True(result.IsSuccess);
        Assert.Equal(MembershipNumber.Length, result.Value.Value.Length);
    }

    [Fact]
    public void Round_trips_through_the_persistence_constructor()
    {
        var created = MembershipNumber.Create("m12345678");
        Assert.True(created.IsSuccess);

        Assert.Equal(created.Value, MembershipNumber.FromPersistedValue(created.Value.Value));
    }
}
