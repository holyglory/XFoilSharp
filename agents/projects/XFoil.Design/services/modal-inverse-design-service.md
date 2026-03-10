# ModalInverseDesignService

- File: `src-cs/XFoil.Design/Services/ModalInverseDesignService.cs`
- Role: managed modal/full-inverse surrogate.

## Public methods

- `CreateSpectrum`
- `Execute`
- `PerturbMode`
- `ExecuteFromSpectrum`

## Important helpers

- `BuildSpectrum`
- `ComputeSineCoefficient`
- `ApplyFilter`
- `ReconstructField`
- `EnsureCompatibleProfiles`

## Parity

- Covers `SPEC`, `PERT`, and a surrogate `EXEC`.
- Not yet legacy `MDES` fidelity.

## TODO

- Tighten modal execution toward original inverse-design behavior.
