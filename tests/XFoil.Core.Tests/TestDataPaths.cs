// Legacy audit:
// Primary legacy source: none
// Role in port: Managed-only test helper that locates repository fixture files used to validate legacy-compatible import and parity behavior.
// Differences: This file has no direct Fortran analogue because it exists solely to support the .NET test harness filesystem layout.
// Decision: Keep the managed helper and document it as test infrastructure rather than legacy-derived solver logic.
namespace XFoil.Core.Tests;

internal static class TestDataPaths
{
    // Legacy mapping: none.
    // Difference from legacy: Fixture path discovery is a managed test-harness concern with no counterpart in the legacy runtime.
    // Decision: Keep the managed helper because the test suite needs a stable way to locate compatibility fixtures.
    public static string GetRunsFixturePath(string fileName)
    {
        string repoRoot = FindRepoRoot();
        string[] candidates =
        {
            Path.Combine(repoRoot, "runs", fileName),
            Path.Combine(repoRoot, "f_xfoil", "runs", fileName),
        };

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            $"Unable to locate fixture '{fileName}'. Checked: {string.Join(", ", candidates)}");
    }

    // Legacy mapping: none.
    // Difference from legacy: Repository root discovery is specific to the .NET test workspace and does not correspond to a Fortran algorithm.
    // Decision: Keep the managed-only implementation because it is infrastructure for tests, not a ported runtime behavior.
    private static string FindRepoRoot()
    {
        string[] startPoints =
        {
            Environment.CurrentDirectory,
            AppContext.BaseDirectory,
        };

        foreach (string start in startPoints)
        {
            string? dir = Path.GetFullPath(start);
            while (dir is not null)
            {
                if (Directory.Exists(Path.Combine(dir, "src")) &&
                    Directory.Exists(Path.Combine(dir, "tests")) &&
                    Directory.Exists(Path.Combine(dir, "f_xfoil")))
                {
                    return dir;
                }

                dir = Path.GetDirectoryName(dir);
            }
        }

        throw new DirectoryNotFoundException("Unable to locate the repository root for test fixtures.");
    }
}
