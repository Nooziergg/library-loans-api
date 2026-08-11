using System.ComponentModel.DataAnnotations;

namespace LibraryLoans.Api.Http;

/// <summary>
/// Runs DataAnnotations over a request DTO before the endpoint sees it, so shape errors are
/// rejected once, in one place, with one response format.
///
/// This handles the outer half of validation: is this a well-formed request. The inner half,
/// is this a legal thing to ask for, lives in the domain, where a value object refuses to
/// exist in an invalid state. Splitting them this way is what keeps request DTOs from growing
/// business rules and domain types from growing presentation concerns.
/// </summary>
internal sealed class ValidationFilter<TRequest> : IEndpointFilter
    where TRequest : class
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        foreach (var argument in context.Arguments)
        {
            if (argument is TRequest request)
            {
                return Validate(request) ?? await next(context);
            }
        }

        // No argument of the expected type. Two very different causes, and conflating them
        // would be a bug either way.
        if (context.Arguments.Any(argument => argument is null))
        {
            // The caller sent nothing where a body was expected. That is their mistake, and it
            // gets the same 400 shape as any other malformed request.
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [string.Empty] = ["A request body is required."],
            });
        }

        // The filter is attached to an endpoint that has no TRequest parameter at all: a
        // wiring mistake. Failing loudly is the entire point: the alternative is calling next()
        // and running the endpoint with no validation whatsoever, which is a security control
        // that silently switches itself off. This surfaces on the first request in the first
        // test run, which is exactly when it should.
        throw new InvalidOperationException(
            $"{nameof(ValidationFilter<TRequest>)}<{typeof(TRequest).Name}> is attached to an " +
            $"endpoint that takes no {typeof(TRequest).Name} parameter, so it would validate " +
            "nothing. Fix the endpoint registration.");
    }

    private static IResult? Validate(TRequest request)
    {
        var results = new List<ValidationResult>();

        if (Validator.TryValidateObject(
                request,
                new ValidationContext(request),
                results,
                validateAllProperties: true))
        {
            return null;
        }

        var errors = results
            .SelectMany(
                result => result.MemberNames.DefaultIfEmpty(string.Empty),
                (result, memberName) => (memberName, message: result.ErrorMessage ?? "Invalid value."))
            .GroupBy(entry => entry.memberName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(entry => entry.message).ToArray(),
                StringComparer.Ordinal);

        return TypedResults.ValidationProblem(errors);
    }
}
