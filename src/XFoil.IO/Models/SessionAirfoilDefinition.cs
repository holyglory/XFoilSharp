// Legacy audit:
// Primary legacy source: none
// Role in port: Managed-only DTO describing the airfoil input for an analysis session manifest.
// Differences: No direct Fortran analogue exists because session manifests are a .NET automation layer, not a legacy runtime structure.
// Decision: Keep the managed DTO because it cleanly represents session inputs.
namespace XFoil.IO.Models;

public sealed class SessionAirfoilDefinition
{
    public string? Naca { get; init; }

    public string? FilePath { get; init; }
}
