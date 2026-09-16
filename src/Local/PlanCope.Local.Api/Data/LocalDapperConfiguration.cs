using System.Runtime.CompilerServices;
using Dapper;

namespace PlanCope.Local.Api.Data;

/// <summary>
/// Dapper is configured through process-wide static state, so the configuration cannot live in a
/// method someone has to remember to call. A repository constructed directly — in a test, a tool,
/// or a background service — must map <c>value_json</c> to <c>ValueJson</c> exactly like one
/// resolved from the container.
/// </summary>
internal static class LocalDapperConfiguration
{
    [ModuleInitializer]
    internal static void Configure()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }
}
