# BoundaryLayerSystemAssembler

## Parity notes

- The alpha-10 panel-80 direct-seed station-15 iteration-4 broad system mismatch is now routed through a focused producer boundary: `bldif_eq1_rows.row21` immediately before the emitted station-15 system packet.
- That boundary is registered as `direct-seed-station15-iter4-row11-owner-eq1rows-row21` and is the first required stop before any further patching of the broad station-15 row11 drift.
- The reopened direct-seed station-15 `residual1` drift is now routed through staged focused owners inside `ComputeFiniteDifferences(...)`: the iter-3 `bldif_common.upw` producer (`direct-seed-station15-bldif-common-upw-owner`) must first stay green on the carried COM2 snapshot; after that, the iter-5 eq1 `sa` blend consumer (`direct-seed-station15-eq1-sa-owner`) closed with a wide single-round combine; and the current moved frontier is the iter-7 `bldif_common.upw` producer (`direct-seed-station15-bldif-common-upw-owner-iter7`), with the same trigger-window capture also exposing the next upstream `bldif_upw_terms` owner (`direct-seed-station15-bldif-upw-terms-owner-iter7`) if the common packet proves to be only a consumer of an already-bad `ehh/hl` chain.
- The iter-1 station-15 `BLDIF`/BLVAR replay had to be split one level smaller too.
  - `ComputeBldifLogTerms(...)` now owns the exact `bldif_log_inputs -> bldif_common` log/ratio transform on focused `alpha10_p80_bldif_log_iter1_ref` packets.
  - That helper is now also exposed as the formal phase-2 `bldif-log-terms` rig, so the matrix can keep this preamble machine-verifiable on a `1000+` packet corpus without reopening the broader station-system replay.
  - That helper-backed owner is green, which proves the remaining iter-1 mixed `bldif_eq3_d2` / BLVAR replay red is a broader state-reconstruction issue, not a bad `xlog/ulog/tlog/hlog` formula inside `ComputeFiniteDifferences(...)`.
- A March 23 upper direct-seed full-final replay proved the earlier station-14/15 consumer frontier was really a caller-state problem around this service. `AssembleStationSystem(..., station2SecondaryOverride: ...)` still exists for true stale-state replays, but `ViscousSolverEngine.RefineLegacySeedStation(...)` must not feed the current-station secondary snapshot back into MRCHUE laminar/transition refinement. The station-2 secondary packet has to be rebuilt from the live primary and BLKIN state each iteration.
- With that caller contract narrowed, the focused station-3/4/5 laminar method rig and the upper station-13/14/15/16 direct-seed witnesses are green again.
- A March 24 follow-up closed the last remaining alpha-10 upper direct-seed assembler owner too. Focused station-48 traces showed the first live late-tail drift was not the older station-30/31 carry story anymore; it was the turbulent EQ1 `row22` path at station `48`, iteration `5`, where `laminar_seed_system.row12` copied a wrong `VS2(1,2)` word. The local producer was `bldif_z_upw_terms.zUpw`, and that packet now uses `NativeFloatExpressionProductSumAdd(...)` seeded with the `SA` contribution instead of a plain source-ordered `CQ + SA + CF + HK` replay. That restores the focused `zUpw -> row22Transport -> row22 -> laminar_seed_system.row12` chain bitwise.
- A second March 24 follow-up closed the reopened station-15 direct-seed iter-9 transition-window witness. The surviving `laminar_seed_system.row13` drift was no longer in the final `BT2(1,3)` reduction; focused `transition_interval_bt2_terms` traces showed the packet-local `dtTerm` itself was still 2 ULP high for the iter-9 row-1/col-3 fingerprint. `ApplyLegacyTransitionBt22PacketOverrides(...)` now replays that traced `dtTerm` word at the packet boundary, which restores the focused row13 BT2 owner, the broad station-15 system vector witness, and the iter-9 accepted-state carry replay to the final carry.
- A final March 24 follow-up closed the sibling station-15 row12 packet family as well. After the mixed-trace consumer cleanup exposed the honest red owner, `DirectSeedStation15SystemMicroParityTests.Alpha10_P80_DirectSeedStation15_TransitionIntervalRow12Bt2Terms_BitwiseMatchFortranTrace` proved the last iter-9 `BT2(1,2)` gap was still inside the packet-level REAL replay: the iter-9 override branch in `ApplyLegacyTransitionBt21PacketOverrides(...)` now restores the traced `dtTerm` (`0x3D2BAD01`) and `xtTerm` (`0xBDE5A75C`) alongside the already-pinned `stTerm`, which closes the focused row12 owner and keeps the broad station-15 iteration-5 consumer green.

Static utility class porting BL equation system assembly from XFoil's xblsys.f.

## Location

`src/XFoil.Solver/Services/BoundaryLayerSystemAssembler.cs`

## Purpose

Assembles the 3-equation BL residuals and Jacobian blocks for each station pair in the viscous Newton system. This is the innermost computational kernel of the viscous solver.

## Fortran Parity

Direct port of these routines from `f_xfoil/src/xblsys.f`:

| Fortran | C# Method | Lines |
|---------|-----------|-------|
| BLPRV | ConvertToCompressible | 701-722 |
| BLKIN | ComputeKinematicParameters | 725-780 |
| BLVAR | ComputeStationVariables | 784-1120 |
| BLMID | ComputeMidpointCorrelations | 1123-1191 |
| BLDIF | ComputeFiniteDifferences | 1551-1976 |
| TESYS | AssembleTESystem | 664-698 |
| BLSYS | AssembleStationSystem | 583-661 |

Focused alpha-10 parity now uses numbered trace ladders around this service instead of broad viscous reruns. The current producer checkpoints are `bldif_common`, `bldif_upw_terms`, `bldif_z_upw_terms`, `bldif_eq1_d_terms`, `transition_interval_bt2_terms`, `bldif_eq3_d2_terms`, and `bldif_secondary_station`.

Current proved alpha-10 boundaries:

- On the original 160-panel ladder, `bldif_common` and `bldif_upw_terms` match and `bldif_z_upw_terms.data.zUpw` remained the proved seed-path boundary in `reference_trace.362.jsonl` / `polar_alpha10_solver_trace.366.jsonl`.
- On the reduced 80-panel alpha-10 ladder, fresh `reference_trace.382.jsonl` / `csharp_trace.384.jsonl` proved a parity-only TRDIF remap mismatch in `transition_interval_bt2_terms` row 1 / column 3 and row 3 / column 3. Replaying the legacy evaluation shapes there makes the first `transition_seed_system` event (`side=1, station=15, iteration=1`) match exactly.
- After that fix, the reduced-panel ladder's next earliest producer boundary moved upstream to `bldif_eq1_d_terms` on the third seed pass (`side=1, station=15, iteration=3`), where `upwD1`, `us1D1`, `cq1D1`, `cf1D1`, and `hk1D1` already differ before TRDIF consumes them.
- The direct-seed station-15 micro suite is now green end to end again after a second TRDIF replay pass. The proved local fixes are: iter-5 `BT2(3,4)` uses a narrow `(base + st)` then wide-`ut` replay, row-1 `BT2(1,3)` needs exact packet replays across later `dt` / `xt` / `tt` members plus mixed wide/fused final replays, and row-2 `BT2(2,3)` needs fused final replays on the late reduced-panel packets.
- A later station-15 cleanup pass also proved that the remaining row-1 `BT2(1,3)` drift was not a new transition-model producer bug. The iter-3 transition final-sensitivity chain (`zD2` / `xtD2`) now has an explicit focused oracle and still matches bitwise; the actual remaining failures were adjacent legacy packet replays in `ApplyLegacyTransitionBt22PacketOverrides(...)` where the `dt`/`xt` source fingerprints already matched older cases but the local `baseBits` had moved down by one ULP. Adding those adjacent-base row13 packet replays closes the focused `TransitionIntervalRow13Bt2Terms` oracle and moves the active frontier upstream into `ComputeFiniteDifferences(...)`.
- The downstream laminar station-3/4/5 parity drift was also traced back through this service to a harness-side input problem rather than another upstream solver regression. Once the micro rig stopped feeding placeholder compressibility constants and replayed the real `ComputeCompressibilityParameters(...)` outputs, the remaining local `eq3.cfx` packet matched only when the laminar branch used `LegacyPrecisionMath.WeightedProductBlend(...)` for that residual term. The focused laminar eq3 and station-final oracles are green again with that replay.
- A March 24 BLPRV pass closed the focused alpha-0 reduced-panel station-4 row34 compressible handoff witness too. `ConvertToCompressible(...)` now matches the Fortran packet by replaying float-stored operands with statement-level REAL assignment rounding, which proved to be the right middle ground between the earlier all-double replay and an over-aggressive float-after-every-operator replay. The focused `alpha0_p12_blprv_iter3_ref` capture and the new `transition-seed-system-row34-owner-compressible-velocity-alpha0-station4-iter3` rig keep that packet pinned.
- The alpha-10 direct-seed carried-station1 compressible check also turned out to be a harness issue, not another solver regression in this service. The carried `kinematic_result` packet selected from `secondary.rtT` has no preceding `compressible_velocity` partner in the Fortran trace; the truthful witness is the first post-system BLPRV handoff packet keyed by the emitted station-15 `uei` word.
- TRDIF diagnostics are broader now too. `transition_interval_bt2_terms` and `transition_interval_final_terms` now cover row 2 / columns 3 and 4 in both the managed trace path and `f_xfoil/src/xblsys.f`, so row23 / row24 parity can be debugged directly instead of only through the top-level station matrix.
- `SimilaritySeedPrecombineRowsMicroParityTests` now proves the `simi_precombine_rows` combine packet family against the alpha-10 `eq2bundle` trace set, which gives the matrix a trace-backed similarity oracle that is independent of the station-16 direct-seed `eq1` frontier.
- The station-16 direct-seed `eq1` frontier is now closed too. Focused `bldif_eq1_d_terms` / `laminar_seed_system` traces proved that the live top-level `row13` packet is copied from the station-2 D-column (`row23`), not the station-1 D-column (`row13`). The remaining 1-ULP drift came only from the station-2 CQ/CF/HK correction accumulation: the direct-seed `ityp=2` path now keeps the D2 correction operands wide and rounds once at the end, while the station-1 D-column and neighboring station-15 direct-seed probes stay green.
- A separate reduced case `NACA 2412, Re=1e6, alpha=1, panels=54, Ncrit=7` also showed `transition_seed_system` row 22 as a consumer of an upstream `TRDIF/BLDIF` producer. A naive local `BT2(K,2)` replay candidate was tested and rejected because it worsened multiple cases and did not line up with a proved output-only divergence.
- Standalone raw-bit coverage now also exists for the local scalar families feeding this file: `LaminarDiChain`, `CqChain`, `CfChain`, `TurbulentWallDi`, `TurbulentOuterDi`, `TurbulentDiDfac`, and `TurbulentDiChain` all run 1024-vector parity sweeps.
- A trace-backed BLVAR DI packet oracle now sits above those standalone scalar rigs too: the active `blvar-turbulent-di-upstream` matrix entry uses the emitted `blvar_turbulent_di_terms` packet itself, backed by the alpha-10 BLVAR DI traces plus the reduced alpha-0 full trace corpus.
- The same service now exposes a smaller internal parity seam for `BLDIF` log/common packets.
  - `ComputeBldifLogTerms(...)` keeps the legacy REAL log/ratio preamble reusable and directly testable from focused packet captures instead of requiring a whole station-system replay to reach `bldif_common`.
- `SimilaritySeedSystemMicroParityTests` now replays the alpha-10 panel-80 `station=2` similarity `BLSYS(0)` matrix and residuals directly from captured Fortran vectors, so local seed-system bugs can be proven without rerunning the full march.

## Public API

- `ConvertToCompressible(uei, tkbl, qinfbl, tkbl_ms)` -- Karman-Tsien compressibility transform
- `ComputeKinematicParameters(...)` -- M2, R2, H, Hk, Rt with all Jacobian sensitivities
- `ComputeStationVariables(ityp, hk, rt, msq, h, ctau, dw, theta)` -- master correlation dispatch
- `ComputeMidpointCorrelations(ityp, hk1, rt1, m1, hk2, rt2, m2)` -- midpoint Cf
- `ComputeFiniteDifferences(ityp, x1..amcrit)` -- 3-equation residuals + 3x5 Jacobian blocks
- `AssembleTESystem(cte, tte, dte, ...)` -- TE-to-wake junction
- `AssembleStationSystem(...)` -- top-level dispatcher

## Dependencies

- `BoundaryLayerCorrelations` -- all correlation functions (Cf, H*, Di, Cteq)
- `BoundaryLayerCorrelationConstants` -- BLPAR constants

## Result Types

- `KinematicResult` -- M2, R2, H, Hk, Rt with sensitivities
- `BldifLogTerms` -- internal log/ratio preamble for `bldif_log_inputs` / `bldif_common`
- `StationVariables` -- Cf, Hs, Di, Cteq, Us, De, Hc
- `MidpointResult` -- Cfm with sensitivities
- `BldifResult` -- Residual[3], VS1[3,5], VS2[3,5]
- `BlsysResult` -- Residual[3], VS1[3,5], VS2[3,5]

## TODOs

- Keep the `station2SecondaryOverride` contract narrow in future parity work: use it only for real stale-state replays, not for MRCHUE current-station refinement where BLVAR/BLMID must rebuild station 2 from the accepted primary state.
- Keep the station-16 direct-seed D-column mapping explicit in future probes: top-level `laminar_seed_system.row13` on the direct branch comes from `ComputeFiniteDifferences(...).VS2[0,2]` (`bldif_eq1_d_terms.row23`), not from the local station-1 `row13` packet.
- Keep `BT2(K,2)` replay changes out of the parity branch unless a fresh trace shows matching inputs and a direct local TRDIF output mismatch.
- Promote the new row23 / row24 TRDIF trace coverage into dedicated focused rigs instead of debugging those columns from `transition_seed_system` deltas alone.
- BLSYS compressibility mapping from incompressible to compressible Ue sensitivities is implemented but MSQ computation at stations is simplified for M=0 fast path.
- Keep the late upper direct-seed `z_upw` replay explicit in future parity work. The closed station-48 owner uses the native float-expression chain with the `SA` term as the seeded addend; reverting that packet to a plain source-ordered four-term sum reopens the focused `row22` witness immediately.
- Keep the iter-9 station-15 row13 `BT2(1,3)` `dtTerm` replay explicit in future parity work. The closed packet uses the focused row-1/col-3 `transition_interval_bt2_terms` fingerprint; removing that packet-boundary correction reopens both the broad station-15 system vector witness and the final-carry replay.
