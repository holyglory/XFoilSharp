# XFoil C# Code Map

This directory maps the managed C# implementation from system architecture down to project, class, and method level.

## Reading order

1. [DocumentationPolicy.md](DocumentationPolicy.md)
2. [architecture/Overview.md](architecture/Overview.md)
3. [architecture/ExecutionFlows.md](architecture/ExecutionFlows.md)
4. [architecture/ParityAndTodos.md](architecture/ParityAndTodos.md)
5. [architecture/RepoAutomation.md](architecture/RepoAutomation.md)
6. Project-level documents under `projects/`

## Tree

- `architecture/Overview.md`
  - System shape, project dependencies, runtime boundaries, test strategy.
- `architecture/ExecutionFlows.md`
  - Main end-to-end flows: geometry import, inviscid solve, viscous solve, design workflows, IO/session workflows, CLI orchestration.
- `architecture/ParityAndTodos.md`
  - Managed-vs-legacy XFoil discrepancy map with prioritized TODOs.
- `architecture/RepoAutomation.md`
  - Repo-local Codex/autonomous-loop hook wiring and validation expectations.
- `architecture/MicroRigMatrixHarness.md`
  - Registry-driven micro-rig parity harness, matrix outputs, and coverage backlog.
- `architecture/ThesisClosurePlan.md`
  - Clean-room implementation plan for the thesis-closure hybrid: linear-vortex panel inviscid + Drela 1986 thesis BL closure. Parallel assembly; does not replace the XFoil parity path.
- `architecture/ThesisClosureValidation.md`
  - F1-finalization validation snapshot: Xtr pins, stall-robustness showcase, attached-flow WT cross-check, known non-convergent cells.
- `architecture/ThesisClosureReadme.md`
  - User guide for the `XFoil.ThesisClosureSolver` assembly: quickstart, CLI flags, ctor knobs, what works, known limitations.
- `architecture/FourSolverValidation.md`
  - Consolidated Parity/Double/Modern/ThesisClosure vs Abbott & Von Doenhoff WT comparison across NACA 0012/2412/4412 — the evidence behind shipping ThesisClosure as the production viscous path.
- `architecture/OptionB-FutureMsesPlan.md`
  - Future-work plan for a real streamline-Euler MSES inviscid (Drela thesis Chapter 2). Not in scope for the current port.
- `projects/XFoil.Core/00-index.md`
  - Domain model and geometry-service entry point.
- `projects/XFoil.Solver/00-index.md`
  - Solver entry point plus links to inviscid, viscous, and numeric leaves.
- `projects/XFoil.Design/00-index.md`
  - Design entry point plus geometry and inverse leaves.
- `projects/XFoil.IO/00-index.md`
  - IO entry point plus import/export/session leaves.
- `projects/XFoil.Cli/00-index.md`
  - CLI entry point plus command-family leaves.

## Scope

This map covers `src/` as it exists today, with discrepancy notes against the original Fortran source in `f_xfoil/`.

## Ground rules used in these docs

- "Parity" means behavioral or workflow equivalence with original XFoil, not just name matching.
- "Managed" means the current C# implementation.
- TODO items are intentionally written into these files so missing features remain visible next to the mapped code.
- Future implementation work must update these docs under the policy in [DocumentationPolicy.md](DocumentationPolicy.md).
