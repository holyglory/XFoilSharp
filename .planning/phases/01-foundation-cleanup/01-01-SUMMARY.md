---
phase: 01-foundation-cleanup
plan: 01
subsystem: docs
tags: [documentation, path-references, csproj, project-structure]

# Dependency graph
requires:
  - phase: none
    provides: initial codebase with stale src-cs/ references
provides:
  - Corrected path references across 51 markdown files and 1 csproj
  - Verified ParityAndTodos.md accuracy
  - Test project can now resolve ProjectReference dependencies
affects: [01-foundation-cleanup, all-phases]

# Tech tracking
tech-stack:
  added: []
  patterns: []

key-files:
  created: []
  modified:
    - agents/README.md
    - agents/projects/**/*.md (50 files)
    - tests/XFoil.Core.Tests/XFoil.Core.Tests.csproj

key-decisions:
  - "agents/README.md Fortran source path changed from src/ to f_xfoil/ to match actual repo structure"
  - "ParityAndTodos.md confirmed accurate with no changes needed"

patterns-established:
  - "All agent docs reference src/ for C# source and f_xfoil/ for Fortran source"

requirements-completed: [DOC-01, DOC-02]

# Metrics
duration: 2min
completed: 2026-03-10
---

# Phase 1 Plan 01: Fix Stale Path References Summary

**Replaced all 51 stale src-cs/ references with src/ across agent docs and test csproj, verified ParityAndTodos.md accuracy**

## Performance

- **Duration:** 2 min
- **Started:** 2026-03-10T13:26:55Z
- **Completed:** 2026-03-10T13:28:36Z
- **Tasks:** 2
- **Files modified:** 52

## Accomplishments
- Fixed 50 agents/projects/**/*.md files: all `File:` references changed from `src-cs/` to `src/`
- Fixed agents/README.md scope sentence: C# source path corrected to `src/`, Fortran source path corrected to `f_xfoil/`
- Fixed 4 ProjectReference paths in tests/XFoil.Core.Tests/XFoil.Core.Tests.csproj from `src-cs\` to `src\`
- Verified ParityAndTodos.md accurately reflects current solver state (surrogate-based, not direct Fortran ports)

## Task Commits

Each task was committed atomically:

1. **Task 1: Fix all src-cs/ path references in agents/ markdown and test csproj** - `fb6dcc8` (fix)
2. **Task 2: Verify ParityAndTodos.md accuracy** - No commit (verification-only, no changes needed)

## Files Created/Modified
- `agents/README.md` - Corrected scope sentence (src/ for C#, f_xfoil/ for Fortran)
- `agents/projects/XFoil.Core/**/*.md` (8 files) - Fixed File: path references
- `agents/projects/XFoil.Solver/**/*.md` (18 files) - Fixed File: path references
- `agents/projects/XFoil.Design/**/*.md` (11 files) - Fixed File: path references
- `agents/projects/XFoil.IO/**/*.md` (7 files) - Fixed File: path references
- `agents/projects/XFoil.Cli/**/*.md` (6 files) - Fixed File: path references
- `tests/XFoil.Core.Tests/XFoil.Core.Tests.csproj` - Fixed 4 ProjectReference Include paths

## Decisions Made
- agents/README.md required a semantic fix: the Fortran source directory was `src/` in the old text but the actual Fortran source lives in `f_xfoil/`, so both paths were corrected in one edit
- ParityAndTodos.md was confirmed accurate after spot-checking: HessSmithInviscidSolver (not PSILIN/GGCALC port), ViscousInteractionCoupler (surrogate, not Newton-coupled), TODO items align with Phase 2-3 roadmap

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
None

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- All documentation paths now correctly reference `src/` for C# and `f_xfoil/` for Fortran
- Test project csproj can now resolve its 4 ProjectReference dependencies
- Ready for Plan 01-02 (.NET 10 upgrade) which depends on correct project references

## Self-Check: PASSED

- FOUND: 01-01-SUMMARY.md
- FOUND: commit fb6dcc8
- FOUND: agents/README.md
- FOUND: tests/XFoil.Core.Tests/XFoil.Core.Tests.csproj
- PASS: zero stale src-cs/ references in repo

---
*Phase: 01-foundation-cleanup*
*Completed: 2026-03-10*
