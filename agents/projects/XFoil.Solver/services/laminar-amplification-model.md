# LaminarAmplificationModel

- File: `src/XFoil.Solver/Services/LaminarAmplificationModel.cs`
- Role: managed transition/amplification surrogate.

## Public methods

- `ComputeGrowthRate(...)`
- `ComputeGrowthIncrement(...)`
- `Advance(...)`

## Parity

- Closest thing in the managed solver to transition transport.
- Still not full e^n logic.

## TODO

- Replace with a formulation closer to original transition handling in `xblsys.f`.
