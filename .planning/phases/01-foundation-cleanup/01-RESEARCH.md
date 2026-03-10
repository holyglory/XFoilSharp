# Phase 1: Foundation Cleanup - Research

**Researched:** 2026-03-10
**Domain:** Documentation path correction, .NET 10 TFM upgrade, build system
**Confidence:** HIGH

## Summary

Phase 1 has two distinct workstreams: (1) fixing stale `src-cs/` path references across 51+ markdown files in the `agents/` documentation tree, and (2) upgrading the .NET target framework from `net8.0` to `net10.0` with zero build warnings.

The documentation fix is mechanical -- every leaf markdown file under `agents/projects/` uses `- File: \`src-cs/...\`` on line 3, while the `agents/README.md` also references `src-cs/`. The `src-cs/` directory no longer exists; the C# source lives under `src/`. Additionally, the test project `tests/XFoil.Core.Tests/XFoil.Core.Tests.csproj` contains broken `ProjectReference` paths pointing to `..\..\src-cs\...` that must be fixed to `..\..\src\...`. The ParityAndTodos.md file is already accurate and does not reference `src-cs/`.

The framework upgrade is straightforward for the 5 main projects (which currently build with zero warnings on net8.0). Each of the 6 csproj files sets `<TargetFramework>net8.0</TargetFramework>` individually and must change to `net10.0`. The `Directory.Build.props` pins `LangVersion` to `10.0`, which should be kept or updated deliberately. The .NET 10 SDK (10.0.103) is already installed on the build machine. The test project packages (xunit 2.5.3, Microsoft.NET.Test.Sdk 17.8.0) are outdated and may need updating for .NET 10 compatibility.

**Primary recommendation:** Split into two plans -- (1) doc path fixes + ParityAndTodos audit, (2) TFM upgrade + test package updates + zero-warning verification.

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| DOC-01 | All agents/ markdown files reference correct `src/` paths (not stale `src-cs/`) | 51 files identified with `src-cs/` references under agents/; 1 file in test project csproj; exact replacement pattern documented |
| DOC-02 | Parity status in ParityAndTodos.md reflects current solver state after changes | ParityAndTodos.md already accurate -- no `src-cs/` references, subsystem parity map is correct; only needs verification, not rewrite |
| FW-01 | All projects target .NET 10 (net10.0) | 6 csproj files identified (5 src + 1 test), all currently set to net8.0; .NET 10 SDK already installed |
| FW-02 | Solution builds cleanly on .NET 10 with no warnings | Main projects build with 0 warnings on net8.0; test project currently broken due to src-cs/ refs; TreatWarningsAsErrors already enabled in Directory.Build.props; test packages need updating |
</phase_requirements>

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| .NET SDK | 10.0.103 | Build toolchain | Already installed; LTS release |
| C# | 10.0 (pinned via LangVersion) | Language version | Pinned in Directory.Build.props; avoids C# 14 overload resolution changes |

### Supporting (Test Project)
| Library | Current Version | Target Version | Purpose |
|---------|----------------|----------------|---------|
| xunit | 2.5.3 | 2.9.3 | Test framework (latest v2 stable) |
| xunit.runner.visualstudio | 2.5.3 | 2.8.2 | VSTest runner for dotnet test |
| Microsoft.NET.Test.Sdk | 17.8.0 | 18.3.0 | Test SDK |
| coverlet.collector | 6.0.0 | 6.0.0+ | Coverage (check latest) |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| xunit v2 (2.9.3) | xunit v3 (3.2.2) | v3 is the future but requires migration effort; v2 still works on net10.0 and is sufficient for Phase 1 |
| LangVersion 10.0 (pinned) | LangVersion 14 or `latest` | C# 14 introduces overload resolution changes with Span params; staying on C# 10 is safer for this phase; can upgrade later |

## Architecture Patterns

### File Layout (No Changes Needed)
```
src/
  XFoil.CSharp.slnx           # Solution file (SLNX format)
  XFoil.Core/                  # Domain models, parsing, normalization
  XFoil.Solver/                # Inviscid + viscous solver
  XFoil.Design/                # Geometry editing, inverse design
  XFoil.IO/                    # Import/export, session execution
  XFoil.Cli/                   # Headless CLI
tests/
  XFoil.Core.Tests/            # All test classes
agents/
  README.md                    # Documentation root
  architecture/                # Overview, flows, parity
  projects/                    # Per-project leaf docs
Directory.Build.props          # Shared build settings
```

### Pattern: Centralized TFM via Directory.Build.props
**What:** Move `<TargetFramework>` from individual csproj files into Directory.Build.props so all projects share one TFM declaration.
**When to use:** When all projects in a solution target the same framework (which they do here -- all net8.0).
**Trade-off:** Reduces duplication but hides TFM from individual csproj. Given that all 6 projects use net8.0 and will all move to net10.0, centralizing is cleaner.
**Alternative:** Update each csproj individually (6 files) if centralization feels premature.

**Recommendation:** Centralize. The project already centralizes LangVersion, Nullable, ImplicitUsings, and TreatWarningsAsErrors in Directory.Build.props. Adding TargetFramework there is consistent.

### Pattern: Markdown Path Fix (sed-style bulk replacement)
**What:** The `src-cs/` to `src/` replacement is a simple string substitution across all 51 markdown files.
**Exact pattern in leaf docs (line 3):**
```
- File: `src-cs/XFoil.Core/Services/AirfoilParser.cs`
```
**Replacement:**
```
- File: `src/XFoil.Core/Services/AirfoilParser.cs`
```

**Special case in agents/README.md (line 34):**
```
This map covers `src-cs/` as it exists today, with discrepancy notes against the original Fortran tree in `src/`.
```
This sentence needs rewriting since the C# source IS now in `src/` and the Fortran source is in `f_xfoil/`.

### Anti-Patterns to Avoid
- **Partial path fix:** Do not fix some files and miss others. The grep count is 51 occurrences across 51 files under agents/. Every one must be fixed.
- **Blind sed without verification:** After replacement, verify that zero `src-cs/` references remain in the entire repo (except possibly git history or planning docs that describe the old state).
- **Upgrading LangVersion alongside TFM:** These are independent concerns. C# 14 introduces new overload resolution for Span parameters. Keep LangVersion pinned at 10.0 for this phase.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Bulk path replacement | Manual editor edits on 51 files | `sed -i 's/src-cs\//src\//g'` on agents/ tree | Mechanical, error-prone to do manually |
| TFM centralization | Copy-paste net10.0 into each csproj | Single line in Directory.Build.props + remove from each csproj | Already centralizing other properties |
| Package version updates | Manual NuGet search per package | `dotnet add package <name>` or edit csproj directly | Resolves latest compatible version |

## Common Pitfalls

### Pitfall 1: Test Project Has Broken ProjectReferences
**What goes wrong:** The test project `tests/XFoil.Core.Tests/XFoil.Core.Tests.csproj` references `..\..\src-cs\XFoil.Core\XFoil.Core.csproj` (and 3 other projects) using the OLD `src-cs\` path. This is a build-breaking bug independent of the TFM upgrade.
**Why it happens:** When `src-cs/` was renamed to `src/`, the test project references were not updated.
**How to avoid:** Fix these 4 ProjectReference paths from `..\..\src-cs\` to `..\..\src\` as part of the documentation path fix work.
**Current impact:** `dotnet build src/XFoil.CSharp.slnx` fails with 137 errors because the test project cannot resolve its dependencies.

### Pitfall 2: Outdated Test Packages on .NET 10
**What goes wrong:** xunit 2.5.3 and Microsoft.NET.Test.Sdk 17.8.0 are old. While xunit v2 generally supports forward TFMs, the test SDK may not fully support .NET 10 at version 17.8.0.
**Why it happens:** Test packages were installed for net8.0 and never updated.
**How to avoid:** Update to xunit 2.9.3, xunit.runner.visualstudio 2.8.2, and Microsoft.NET.Test.Sdk 18.3.0 as part of the TFM upgrade.
**Warning signs:** `dotnet test` fails or produces warnings about unsupported TFM.

### Pitfall 3: TreatWarningsAsErrors Amplifies New Analyzer Warnings
**What goes wrong:** Directory.Build.props already has `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`. If .NET 10 SDK introduces new default analyzers or warnings, they will become build errors.
**Why it happens:** New SDK versions sometimes ship with new default analyzers that produce warnings on code that compiled cleanly before.
**How to avoid:** After TFM change, run `dotnet build` and address any new warnings/errors. The main projects build cleanly on net8.0 with the .NET 10 SDK already (verified), so this risk is LOW.
**Warning signs:** Build errors on previously clean code after changing only the TFM.

### Pitfall 4: NuGet Audit Warnings on .NET 10
**What goes wrong:** .NET 10 SDK runs `dotnet restore` with transitive package auditing by default (breaking change in SDK). This may surface new NU1903/NU1904 warnings for packages with known vulnerabilities.
**Why it happens:** New default behavior in .NET 10 SDK.
**How to avoid:** If audit warnings appear, either update the flagged packages or add `<NuGetAuditMode>direct</NuGetAuditMode>` to Directory.Build.props to restore old behavior.

### Pitfall 5: agents/README.md Needs Semantic Fix, Not Just String Replacement
**What goes wrong:** The README says "This map covers `src-cs/` as it exists today, with discrepancy notes against the original Fortran tree in `src/`." A simple `src-cs/` to `src/` replacement would produce nonsensical text: "covers `src/` ... with discrepancy notes against the original Fortran tree in `src/`."
**Why it happens:** The sentence assumed `src-cs/` was the C# code and `src/` was the Fortran code. Now C# is in `src/` and Fortran is in `f_xfoil/`.
**How to avoid:** Rewrite this sentence to: "This map covers `src/` as it exists today, with discrepancy notes against the original Fortran source in `f_xfoil/`."

### Pitfall 6: Duplicate Properties After Centralization
**What goes wrong:** If TargetFramework is added to Directory.Build.props but not removed from individual csproj files, MSBuild uses the last-wins rule. The csproj value overrides the props value.
**Why it happens:** Forgetting to clean up individual csproj files after centralizing.
**How to avoid:** Remove `<TargetFramework>` from all 6 csproj files after adding it to Directory.Build.props. Also remove redundant `<ImplicitUsings>` and `<Nullable>` from individual csproj files since they are already in Directory.Build.props.

## Code Examples

### Current Directory.Build.props
```xml
<Project>
  <PropertyGroup>
    <LangVersion>10.0</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

### Target Directory.Build.props (after adding TargetFramework)
```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>10.0</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

### Cleaned-Up Library csproj (e.g., XFoil.Core)
```xml
<!-- Before -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>

<!-- After (remove all properties now in Directory.Build.props) -->
<Project Sdk="Microsoft.NET.Sdk">
</Project>
```

### Fixed Test Project ProjectReferences
```xml
<!-- Before -->
<ProjectReference Include="..\..\src-cs\XFoil.Core\XFoil.Core.csproj" />
<ProjectReference Include="..\..\src-cs\XFoil.Design\XFoil.Design.csproj" />
<ProjectReference Include="..\..\src-cs\XFoil.IO\XFoil.IO.csproj" />
<ProjectReference Include="..\..\src-cs\XFoil.Solver\XFoil.Solver.csproj" />

<!-- After -->
<ProjectReference Include="..\..\src\XFoil.Core\XFoil.Core.csproj" />
<ProjectReference Include="..\..\src\XFoil.Design\XFoil.Design.csproj" />
<ProjectReference Include="..\..\src\XFoil.IO\XFoil.IO.csproj" />
<ProjectReference Include="..\..\src\XFoil.Solver\XFoil.Solver.csproj" />
```

### Markdown Leaf Doc Path Fix Pattern
```markdown
<!-- Before (line 3 in every leaf doc) -->
- File: `src-cs/XFoil.Core/Services/AirfoilParser.cs`

<!-- After -->
- File: `src/XFoil.Core/Services/AirfoilParser.cs`
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| net8.0 TFM | net10.0 TFM | .NET 10 GA Nov 2025 | LTS release, minimal breaking changes for console/library projects |
| xunit 2.5.3 | xunit 2.9.3 (latest v2) | 2024 | Security and compatibility fixes |
| Microsoft.NET.Test.Sdk 17.8.0 | 18.3.0 | 2025-2026 | .NET 10 support |
| `src-cs/` directory | `src/` directory | Already renamed | Documentation still references old path |

## Quantified Scope

### Documentation Path Fixes
- **51 files** under `agents/` contain `src-cs/` references (1 occurrence each)
- **50 files** are leaf docs with `- File: \`src-cs/...\`` on line 3 (simple replacement)
- **1 file** is `agents/README.md` with a sentence needing semantic rewrite
- **1 file** is `tests/XFoil.Core.Tests/XFoil.Core.Tests.csproj` with 4 broken ProjectReference paths
- **0 files** in `agents/architecture/ParityAndTodos.md` reference `src-cs/` (already clean)

### TFM Upgrade
- **6 csproj files** need TFM change (5 in src/ + 1 in tests/)
- **1 Directory.Build.props** to add TargetFramework (optional centralization)
- **3 NuGet packages** in test project need version bumps
- **133 C# source files** in src/, **36 test files** -- all may need to compile cleanly
- **0 warnings** currently on net8.0 main build (verified)
- **137 errors** on full solution build due to broken test project references

### ParityAndTodos.md Audit
- File is already accurate for current state
- Subsystem parity map correctly identifies: surrogate viscous coupling, surrogate transition model, surrogate inviscid kernel, missing drag decomposition
- No content changes expected; verification pass sufficient

## Open Questions

1. **Centralize TargetFramework or update individually?**
   - What we know: Directory.Build.props already centralizes 4 other properties. All 6 projects use the same TFM.
   - Recommendation: Centralize. It is consistent with existing pattern and reduces future upgrade effort.

2. **Update LangVersion from 10.0 to something higher?**
   - What we know: C# 14 (default for net10.0) adds Span overload resolution changes. The codebase uses LangVersion 10.0.
   - Recommendation: Keep pinned at 10.0 for this phase. Can evaluate C# 12/13/14 upgrade in a later phase.

3. **Upgrade to xunit v3 or stay on v2?**
   - What we know: xunit v2 is deprecated but 2.9.3 still works. v3 requires migration effort. Phase 1 goal is "builds cleanly," not "modernize test framework."
   - Recommendation: Stay on v2 (update to 2.9.3). Evaluate v3 migration separately if needed.

## Sources

### Primary (HIGH confidence)
- Direct file inspection of all csproj files, Directory.Build.props, agents/ markdown tree
- `dotnet build` output on current codebase (verified zero warnings for main projects, 137 errors for test project)
- `dotnet --version` confirming .NET 10 SDK 10.0.103 installed
- `grep` results confirming exact count of `src-cs/` references

### Secondary (MEDIUM confidence)
- [Microsoft .NET 10 Breaking Changes](https://learn.microsoft.com/en-us/dotnet/core/compatibility/10) - comprehensive list of breaking changes
- [NuGet: xunit 2.9.3](https://www.nuget.org/packages/xunit) - latest v2 stable release
- [NuGet: Microsoft.NET.Test.Sdk 18.3.0](https://www.nuget.org/packages/microsoft.net.test.sdk) - latest test SDK
- [NuGet: xunit.runner.visualstudio 2.8.2](https://www.nuget.org/packages/xunit.runner.visualstudio/2.5.8) - latest v2 VSTest runner
- [Microsoft C# Language Versioning](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-versioning) - LangVersion defaults per TFM

### Tertiary (LOW confidence)
- [xunit v2 .NET 10 compatibility reports](https://github.com/xunit/visualstudio.xunit/issues/449) - community reports suggest v2 works but v3 is recommended

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - directly verified SDK version, build output, file contents
- Architecture: HIGH - no architecture changes needed, just configuration
- Pitfalls: HIGH - all pitfalls verified against actual project state
- Documentation scope: HIGH - exact file counts from grep

**Research date:** 2026-03-10
**Valid until:** 2026-04-10 (stable domain, no rapidly changing dependencies)
