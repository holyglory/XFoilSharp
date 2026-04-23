# Option B — Real Streamline-Euler MSES (Future Work)

Scope note: this document describes what a *real* MSES port would
entail. It is **not scheduled** and is deliberately high-level. The
shipped `XFoil.ThesisClosureSolver` is a hybrid — linear-vortex panel
inviscid + thesis-exact BL closure — and is sufficient for the use
cases that motivated this work. Option B is the upgrade path for the
cases where the hybrid's inviscid model breaks down (transonic
shocks, strong viscous-inviscid interaction on cambered airfoils).

## What "real MSES" means

From Drela's 1986 MIT thesis (Chapter 2) and the MSES 3.05 memo:

- **Inviscid:** streamline-based finite-volume discretization of the
  steady Euler equations on a structured grid whose upstream boundary
  is the airfoil surface and whose downstream streamlines are part of
  the solution. Not a panel method.
- **Viscous:** the same 2nd-order integral BL closure we already have.
- **Coupling:** full Newton on the combined Euler + BL block-banded
  system. Inviscid and viscous states are solved simultaneously; the
  BL's displacement thickness enters the inviscid boundary condition
  (source injection at the wall), and the inviscid's edge velocity
  enters the BL momentum/shape equations.

The piece this repo currently lacks is the **streamline-Euler grid +
finite-volume discretization + Newton coupling**. Everything viscous
is already in place.

## Why it matters

The hybrid solver's known limitation is CL overprediction on cambered
airfoils (5–15 %) because viscous displacement does not feed back
into the inviscid solve. Fixing this cleanly requires either:

1. A non-parity **two-way source-coupled panel method** where σ
   modifies the vortex-panel Jacobian (a.k.a. "Phase 5 proper").
2. A **real streamline-Euler MSES** inviscid replacing the panel
   method entirely (this document).

Option 1 is cheaper but inherits the panel method's small-
disturbance assumption — it won't help at transonic speeds. Option B
(this document) is the expensive-but-general answer.

## Rough phase outline (unscoped)

- **M1 — Streamline grid generator.** Ingest airfoil geometry, emit
  a structured grid with the airfoil surface as one family of grid
  lines and streamlines as the other. Iterative; grid is part of
  the solution.
- **M2 — Finite-volume Euler residual + Jacobian.** Cell-centered or
  vertex-centered; second-order with limiters; sonic capturing.
- **M3 — Block-banded Newton.** Couple the Euler residual with the
  existing thesis BL residual. Reuse the BL Newton infrastructure
  from `ThesisClosureGlobalNewton`.
- **M4 — Validation.** Transonic NACA 0012 shock position; cambered
  NACA 4412 CL bias elimination vs Abbott & Von Doenhoff WT; compare
  against published MSES results where available.

Size estimate: **6–12 person-months** of focused work. This is not a
side quest. It is a separate product that shares a viscous library
with the current solver.

## Reuse from the current repo

- `XFoil.ThesisClosureSolver/Closure/*` — all closure relations.
- `XFoil.ThesisClosureSolver/Newton/*` — block-banded residual/Jacobian
  scaffolding. The per-side BL marching and TE-merge logic would need
  to be generalized to the grid topology but is largely reusable.
- `XFoil.ThesisClosureSolver/BoundaryLayer/*` — marchers and
  amplification model.

The part that would be new in Option B is the inviscid side:
streamline grid + Euler residuals + coupling.

## What not to expect from this doc

This is a **placeholder** that exists so that the Option A ship
(hybrid thesis-closure solver) has a clear forward reference. No
work has been committed to Option B beyond writing it down. When
Option B is scheduled, a real plan will go in its place.
