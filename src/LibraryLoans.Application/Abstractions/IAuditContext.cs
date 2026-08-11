namespace LibraryLoans.Application.Abstractions;

/// <summary>
/// Who is making the current change, and under which request. Answered per scope.
///
/// <para>This port sits in the Application layer for the ordinary reason — it is an interface the
/// inner layers own and an outer layer satisfies — but the direction is worth noticing, because it
/// is the opposite of the others here. <c>IUnitOfWork</c> is declared here and implemented by
/// Infrastructure, which Application calls. This one is declared here, implemented by the Api, and
/// consumed by Infrastructure. Application never calls it at all.</para>
///
/// <para>It still belongs here rather than in Infrastructure, for a reason that is easy to state:
/// Infrastructure must not know that an HTTP request exists, and the Api must not have to reference
/// Infrastructure's internals to answer a question about the caller. Application is the layer both
/// can see, so it is where the contract lives.</para>
/// </summary>
public interface IAuditContext
{
    /// <summary>
    /// The principal responsible for the change, as it will be written to the audit trail.
    ///
    /// <para><b>This is not an authenticated identity today, and the audit trail should not pretend
    /// otherwise.</b> Nothing in this service authenticates anyone (see docs/AUTHORIZATION.md), so
    /// the honest answer over HTTP is that the caller is anonymous, and off the HTTP path — startup
    /// migration and seeding — it is the system itself. An audit row saying <c>anonymous</c> is
    /// worth strictly more than one naming a user the service never verified: the first records what
    /// is known, the second records a guess in a table people are meant to trust.</para>
    /// </summary>
    string Actor { get; }

    /// <summary>
    /// Ties an audited change to the request that caused it, and through that to every log line
    /// written while serving it. This is what makes an audit row answer "what else happened in the
    /// same breath" rather than only "this row changed at 14:02".
    /// </summary>
    string? CorrelationId { get; }
}
