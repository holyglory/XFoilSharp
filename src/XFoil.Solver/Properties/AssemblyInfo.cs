// Legacy audit:
// Primary legacy source: none
// Role in port: Managed-only assembly metadata that exposes internal parity helpers to the test project without widening the public solver API.
// Differences: Classic XFoil had no assembly boundary or friend-assembly concept; this file exists only for .NET test access control.
// Decision: Keep the managed-only friend-assembly declaration because parity tests need internal visibility while runtime APIs stay unchanged.
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("XFoil.Core.Tests")]
