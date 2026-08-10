using LibraryLoans.Domain.Common;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LibraryLoans.Api.Http;

/// <summary>
/// The single place where a domain failure becomes a status code.
///
/// The Domain layer classifies its own failures but knows nothing about HTTP, which is what
/// lets the same rules be reused by a message consumer or a background job later without
/// dragging web semantics along. Keeping the translation in one file also means the answer to
/// "what status does a suspended member get?" is read rather than inferred from a dozen
/// endpoints that might disagree.
/// </summary>
internal static class DomainErrorToHttp
{
    public static ProblemHttpResult ToProblem(DomainError error) =>
        TypedResults.Problem(
            title: TitleFor(error.Kind),
            detail: error.Message,
            statusCode: StatusCodeFor(error.Kind),
            extensions: new Dictionary<string, object?>
            {
                // The stable, machine-readable half of the contract. Clients branch on this;
                // the human-readable detail above is free to be reworded at any time.
                ["code"] = error.Code,
            });

    private static int StatusCodeFor(DomainErrorKind kind) => kind switch
    {
        // 422, not 400. The distinction is deliberate and consistent across the API: 400 means
        // the request could not be understood as a request — a missing field, a string where a
        // number belongs — and is produced by the DataAnnotations filter before a handler ever
        // runs. 422 means the request was understood perfectly and describes something the
        // domain will not accept, such as an ISBN whose check digit does not compute.
        DomainErrorKind.Validation => StatusCodes.Status422UnprocessableEntity,
        DomainErrorKind.RuleViolation => StatusCodes.Status422UnprocessableEntity,
        DomainErrorKind.Conflict => StatusCodes.Status409Conflict,
        DomainErrorKind.NotFound => StatusCodes.Status404NotFound,

        // Unreachable while the enum and this switch agree. If a new kind is added and this is
        // forgotten, a 500 is the honest answer — inventing a plausible status for an
        // unclassified failure would hide the omission.
        _ => StatusCodes.Status500InternalServerError,
    };

    private static string TitleFor(DomainErrorKind kind) => kind switch
    {
        DomainErrorKind.Validation => "The request could not be accepted.",
        DomainErrorKind.RuleViolation => "That operation is not allowed right now.",
        DomainErrorKind.Conflict => "That conflicts with existing data.",
        DomainErrorKind.NotFound => "Not found.",
        _ => "Unexpected error.",
    };
}
