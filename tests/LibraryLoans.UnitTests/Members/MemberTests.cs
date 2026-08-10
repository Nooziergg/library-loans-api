using LibraryLoans.Domain.Members;

namespace LibraryLoans.UnitTests.Members;

public sealed class MemberTests
{
    private static MembershipNumber ANumber()
    {
        var number = MembershipNumber.Create("M00000001");
        Assert.True(number.IsSuccess);
        return number.Value;
    }

    [Fact]
    public void Registers_an_active_member()
    {
        var result = Member.Register(ANumber(), "A Borrower", "borrower@example.test");

        Assert.True(result.IsSuccess);
        Assert.Equal(MemberStatus.Active, result.Value.Status);
        Assert.True(result.Value.CanBorrow);
        Assert.NotEqual(Guid.Empty, result.Value.Id);
    }

    [Fact]
    public void Trims_surrounding_whitespace()
    {
        var result = Member.Register(ANumber(), "  A Borrower  ", " borrower@example.test ");

        Assert.True(result.IsSuccess);
        Assert.Equal("A Borrower", result.Value.Name);
        Assert.Equal("borrower@example.test", result.Value.Email);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Requires_a_name(string? name)
    {
        var result = Member.Register(ANumber(), name, "borrower@example.test");

        Assert.False(result.IsSuccess);
        Assert.Equal("member.name.required", result.Error.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Requires_an_email(string? email)
    {
        var result = Member.Register(ANumber(), "A Borrower", email);

        Assert.False(result.IsSuccess);
        Assert.Equal("member.email.required", result.Error.Code);
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("@example.test")]
    [InlineData("borrower@")]
    [InlineData("borrower@example")]
    [InlineData("two@at@example.test")]
    [InlineData("has space@example.test")]
    [InlineData("borrower@example.")]
    public void Rejects_something_that_is_not_an_email_address(string email)
    {
        var result = Member.Register(ANumber(), "A Borrower", email);

        Assert.False(result.IsSuccess);
        Assert.Equal("member.email.malformed", result.Error.Code);
    }

    [Fact]
    public void Rejects_a_name_longer_than_the_column_allows()
    {
        var result = Member.Register(ANumber(), new string('n', Member.NameMaxLength + 1), "b@example.test");

        Assert.False(result.IsSuccess);
        Assert.Equal("member.name.too_long", result.Error.Code);
    }

    [Fact]
    public void Suspends_an_active_member()
    {
        var member = ARegisteredMember();

        var result = member.Suspend();

        Assert.True(result.IsSuccess);
        Assert.Equal(MemberStatus.Suspended, member.Status);
        Assert.False(member.CanBorrow);
    }

    /// <summary>
    /// Consistent with refusing a second return: an operation that quietly does nothing is
    /// indistinguishable, from the caller's side, from one that worked.
    /// </summary>
    [Fact]
    public void Refuses_to_suspend_an_already_suspended_member()
    {
        var member = ARegisteredMember();
        Assert.True(member.Suspend().IsSuccess);

        var second = member.Suspend();

        Assert.False(second.IsSuccess);
        Assert.Equal("member.already_suspended", second.Error.Code);
    }

    private static Member ARegisteredMember()
    {
        var member = Member.Register(ANumber(), "A Borrower", "borrower@example.test");
        Assert.True(member.IsSuccess);

        return member.Value;
    }
}
