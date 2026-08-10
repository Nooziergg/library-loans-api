using System.Reflection;
using LibraryLoans.Application;
using LibraryLoans.Domain;

namespace LibraryLoans.UnitTests.Architecture;

/// <summary>
/// The Clean Architecture dependency rule, enforced by the build instead of by convention.
///
/// This exists because "the domain must not depend on infrastructure" is the first thing a
/// reviewer greps for and the easiest thing to lose silently over time — a single
/// convenience reference added under deadline pressure inverts the whole design, and
/// nothing else in the toolchain complains.
/// </summary>
public sealed class DependencyRuleTests
{
    private static readonly string[] ForbiddenInDomain =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "Microsoft.Extensions.DependencyInjection",
        "Npgsql",
        "Dapper",
        "Newtonsoft.Json",
    ];

    /// <summary>
    /// Deliberately not the same list as <see cref="ForbiddenInDomain"/>.
    ///
    /// Application is allowed the two first-party abstraction packages it uses to describe its
    /// own composition and to log — and one of them,
    /// <c>Microsoft.Extensions.DependencyInjection.Abstractions</c>, begins with a string that
    /// appears in Domain's list. Reusing that list here would fail on a reference that is
    /// deliberately present, and the natural next step would be loosening the Domain rule to
    /// make the Application test pass. Two lists, two intentions.
    ///
    /// What Application must never see is persistence, the web, or the Infrastructure assembly
    /// that implements the ports it owns.
    /// </summary>
    private static readonly string[] ForbiddenInApplication =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "Npgsql",
        "LibraryLoans.Infrastructure",
    ];

    [Fact]
    public void Domain_does_not_reference_infrastructure_or_web_frameworks()
    {
        var offenders = TransitiveReferencesOf(typeof(DomainAssemblyMarker).Assembly)
            .Where(name => ForbiddenInDomain.Any(forbidden =>
                name.StartsWith(forbidden, StringComparison.Ordinal)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Domain must stay free of infrastructure concerns. Found: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Application_does_not_reference_persistence_or_the_web()
    {
        var offenders = TransitiveReferencesOf(typeof(ApplicationAssemblyMarker).Assembly)
            .Where(name => ForbiddenInApplication.Any(forbidden =>
                name.StartsWith(forbidden, StringComparison.Ordinal)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "Application owns its port interfaces; Infrastructure implements them. " +
            $"The arrow must not point outward. Found: {string.Join(", ", offenders)}");
    }

    /// <summary>
    /// Walks the reference graph breadth-first rather than checking direct references only.
    /// The failure this guards against is indirect: Domain -> some shared project -> a web
    /// framework. A direct-reference check misses that entirely.
    /// </summary>
    private static IEnumerable<string> TransitiveReferencesOf(Assembly root)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<Assembly>();
        pending.Enqueue(root);

        while (pending.Count > 0)
        {
            foreach (var reference in pending.Dequeue().GetReferencedAssemblies())
            {
                if (!seen.Add(reference.FullName))
                {
                    continue;
                }

                yield return reference.Name ?? string.Empty;

                Assembly? loaded = null;
                try
                {
                    loaded = Assembly.Load(reference);
                }
                catch (Exception)
                {
                    // Not resolvable in the test host; there is nothing deeper to walk.
                    // The reference itself has already been reported above.
                }

                if (loaded is not null)
                {
                    pending.Enqueue(loaded);
                }
            }
        }
    }
}
