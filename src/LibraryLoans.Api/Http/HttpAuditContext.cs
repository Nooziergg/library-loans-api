using LibraryLoans.Application.Abstractions;

namespace LibraryLoans.Api.Http;

/// <summary>
/// Answers "who is changing this, and under which request" from the ambient HTTP request — or says
/// plainly that there is no request, which is the case during startup migration and seeding.
///
/// <para>This is the Api's half of the audit trail, and it is the only half that knows HTTP exists.
/// Infrastructure writes the rows and has no idea where the actor came from; a background worker or
/// a console tool would satisfy the same port differently and the trail would be none the wiser.</para>
/// </summary>
internal sealed class HttpAuditContext(IHttpContextAccessor httpContextAccessor) : IAuditContext
{
    /// <summary>Changes made with no request in flight: the startup migration and the seeder.</summary>
    internal const string SystemActor = "system";

    /// <summary>
    /// A caller we served but did not identify. **Today this is every caller**, because nothing in
    /// this service authenticates anyone.
    /// </summary>
    internal const string AnonymousActor = "anonymous";

    public string Actor => httpContextAccessor.HttpContext switch
    {
        null => SystemActor,

        // Correct now and correct later, which is why the branch is here rather than a hardcoded
        // string with a comment promising to fix it. It reads whatever principal the pipeline
        // established; today no middleware establishes one, so it falls through to anonymous. On the
        // day authentication is added — see docs/AUTHORIZATION.md — the audit trail starts naming
        // real subjects without this file being touched, because that is where the name was always
        // going to come from.
        { User.Identity: { IsAuthenticated: true, Name: { Length: > 0 } name } } => name,

        _ => AnonymousActor,
    };

    /// <summary>
    /// The same string the response header carries, the same one in any RFC 7807 body, and the same
    /// one on every log line for this request — <c>CorrelationMiddleware</c> assigns it to
    /// <c>TraceIdentifier</c>. That is what lets an audit row and the log of the request that wrote
    /// it be joined by a single grep, which is most of the value of recording it here at all.
    /// </summary>
    public string? CorrelationId => httpContextAccessor.HttpContext?.TraceIdentifier;
}
