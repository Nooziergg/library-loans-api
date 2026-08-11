using System.Runtime.CompilerServices;

namespace LibraryLoans.IntegrationTests.Infrastructure;

/// <summary>
/// Test-run configuration that must be in place before any container is created.
/// </summary>
internal static class TestRunConfiguration
{
    /// <summary>
    /// Turns off Ryuk, the reaper sidecar Testcontainers starts by default.
    ///
    /// Ryuk exists to delete containers left behind when a test run dies before its cleanup
    /// code runs, and it does that by mounting the Docker socket into a container. That is a
    /// genuine privilege, a container able to control the daemon, and it is granted for the
    /// entire duration of every test run, to cover a case that only occurs when a run crashes.
    ///
    /// It is not needed here. <see cref="PostgresFixture"/> owns exactly one container and
    /// disposes it in <c>DisposeAsync</c>, which xUnit invokes even when tests fail. The cost of
    /// doing without is that a hard kill (a power loss, a terminated process) can leave one
    /// postgres container behind, removable with <c>docker rm</c>. That is a better trade than
    /// standing socket access.
    ///
    /// Set through the environment variable rather than <c>TestcontainersSettings</c> because the
    /// variable is Testcontainers' documented public contract and has survived major versions,
    /// whereas the settings class has moved namespaces between them.
    ///
    /// A module initializer runs before any test class is constructed, which is what guarantees
    /// this lands before the first container is built.
    /// </summary>
    [ModuleInitializer]
    internal static void DisableResourceReaper() =>
        Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", "true");
}
