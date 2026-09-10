using FinishGenius.Api.Domain;

namespace FinishGenius.Api.Infrastructure;

/// <summary>
/// Role lists for [Authorize(Roles = Access.X)] — the menu access matrix from the "2. Users" test document.
/// Keep in sync with frontend/src/lib/access.ts.
/// </summary>
public static class Access
{
    private const string SA = Roles.SystemAdmin, SUP = Roles.SupportAgent, GA = Roles.GroupAdmin, P = Roles.FGPro, PP = Roles.FGProPlus;

    public const string Everyone = $"{P},{PP},{GA},{SUP},{SA}";
    public const string Users = $"{GA},{SA}";
    public const string Materials = $"{P},{GA},{SA}";
    public const string Formulas = $"{P},{GA},{SA}";
    public const string Process = $"{P},{GA},{SA}";
    public const string MyWork = $"{PP},{GA},{SA}";
    public const string Dashboard = $"{PP},{GA},{SA}";
    public const string WorkInstructions = $"{P},{PP},{GA},{SA}";
    public const string MaterialCategories = $"{GA},{SA}";
    public const string SubSteps = SA;
    public const string Import = $"{GA},{SA}";
    /// <summary>Reading setup data (e.g. categories/sub steps) needed by the process builders.</summary>
    public const string ProcessRead = $"{P},{PP},{GA},{SA}";
}
