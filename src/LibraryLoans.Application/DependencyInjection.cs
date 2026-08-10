using LibraryLoans.Application.Books;
using LibraryLoans.Application.Copies;
using LibraryLoans.Application.Loans;
using LibraryLoans.Application.Members;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryLoans.Application;

/// <summary>
/// The Application layer registers its own use cases.
///
/// Registration is explicit — every handler named on its own line, no assembly scanning. The
/// list is longer than a one-line convention would be, and that is the trade being made: what
/// is in the container is legible from the source, a typo is a build error rather than a
/// missing registration discovered at runtime, and startup does no reflection.
/// </summary>
public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<CreateBookHandler>();
        services.AddScoped<GetBookByIdHandler>();
        services.AddScoped<RegisterMemberHandler>();
        services.AddScoped<AddBookCopyHandler>();
        services.AddScoped<BorrowCopyHandler>();
        services.AddScoped<ReturnLoanHandler>();
        services.AddScoped<GetLoanByIdHandler>();

        return services;
    }
}
