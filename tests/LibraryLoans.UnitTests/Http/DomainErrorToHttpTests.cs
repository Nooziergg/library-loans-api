using LibraryLoans.Api.Http;
using LibraryLoans.Domain.Common;
using Microsoft.AspNetCore.Http;

namespace LibraryLoans.UnitTests.Http;

/// <summary>
/// The status code a client sees is part of the API's contract, so it is asserted here rather
/// than left to whatever the endpoint happened to return on the day it was written.
/// </summary>
public sealed class DomainErrorToHttpTests
{
    [Theory]
    [InlineData(DomainErrorKind.Validation, StatusCodes.Status422UnprocessableEntity)]
    [InlineData(DomainErrorKind.RuleViolation, StatusCodes.Status422UnprocessableEntity)]
    [InlineData(DomainErrorKind.Conflict, StatusCodes.Status409Conflict)]
    [InlineData(DomainErrorKind.NotFound, StatusCodes.Status404NotFound)]
    public void Maps_each_kind_of_failure_to_its_status_code(DomainErrorKind kind, int expected)
    {
        var error = new DomainError("some.code", "Some message.", kind);

        var result = DomainErrorToHttp.ToProblem(error);

        Assert.Equal(expected, result.StatusCode);
    }

    /// <summary>
    /// Clients branch on the code, so it has to survive the trip. Leaving it out would force
    /// them to match on the human-readable message, which is the part that is free to change.
    /// </summary>
    [Fact]
    public void Carries_the_error_code_as_a_problem_details_extension()
    {
        var error = DomainError.Conflict("book.isbn.duplicate", "Already in the catalogue.");

        var result = DomainErrorToHttp.ToProblem(error);

        Assert.Equal("book.isbn.duplicate", Assert.Contains("code", result.ProblemDetails.Extensions));
    }

    [Fact]
    public void Passes_the_domain_message_through_as_the_detail()
    {
        var error = DomainError.NotFound("book.not_found", "No book exists with that id.");

        var result = DomainErrorToHttp.ToProblem(error);

        Assert.Equal("No book exists with that id.", result.ProblemDetails.Detail);
    }
}
