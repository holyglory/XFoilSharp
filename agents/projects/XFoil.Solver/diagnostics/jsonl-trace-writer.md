# JsonlTraceWriter

- File: `src/XFoil.Solver/Diagnostics/JsonlTraceWriter.cs`
- Role: managed JSONL trace sink plus ambient `SolverTrace` routing for parity diagnostics.

## Notes

- `session_start` and `session_end` are always written, even when filtering is active, so filtered traces stay self-describing.
- Every persisted trace record now carries exact bit metadata for numeric payloads:
  - `dataBits`
  - `valuesBits`
  - `tagsBits`
- Floating values store both `f32` and `f64` hex words so parity tooling can compare the intended legacy single-precision view and the widened managed double view without relying on decimal round-trips.
- Integer-valued payloads store `i32` and/or `i64` hex words when the value fits those widths.
- Managed traces now support env-driven capture filters:
  - `XFOIL_TRACE_KIND_ALLOW`
  - `XFOIL_TRACE_SCOPE_ALLOW`
  - `XFOIL_TRACE_SIDE`
  - `XFOIL_TRACE_STATION`
  - `XFOIL_TRACE_ITERATION`
  - `XFOIL_TRACE_ITER_MIN`
  - `XFOIL_TRACE_ITER_MAX`
  - `XFOIL_TRACE_MODE`
- Managed traces also support trigger-gated capture:
  - `XFOIL_TRACE_TRIGGER_KIND`
  - `XFOIL_TRACE_TRIGGER_SCOPE`
  - `XFOIL_TRACE_TRIGGER_NAME_ALLOW`
  - `XFOIL_TRACE_TRIGGER_DATA_MATCH`
  - `XFOIL_TRACE_TRIGGER_OCCURRENCE`
  - `XFOIL_TRACE_TRIGGER_SIDE`
  - `XFOIL_TRACE_TRIGGER_STATION`
  - `XFOIL_TRACE_TRIGGER_ITERATION`
  - `XFOIL_TRACE_TRIGGER_ITER_MIN`
  - `XFOIL_TRACE_TRIGGER_ITER_MAX`
  - `XFOIL_TRACE_TRIGGER_MODE`
  - `XFOIL_TRACE_RING_BUFFER`
- `XFOIL_TRACE_RING_BUFFER` keeps the last `N` pre-trigger records in memory and flushes them once the trigger selector matches.
- `XFOIL_TRACE_TRIGGER_OCCURRENCE` arms the persisted window on the Nth selector match instead of the first one, which is now critical for tiny focused reruns through repeated paneling and spline loops.
- `JsonlTraceWriter` can also stream each serialized record to an in-process observer. The current parity harness uses that hook to live-compare managed events against a precomputed Fortran trace and abort at the first mismatch without waiting for the full JSONL file to be written.
- The Fortran debug harness mirrors the same selector names by routing every `debug_trace.jsonl` write through `tools/fortran-debug/filter_trace.py` and a FIFO override in `tools/fortran-debug/json_trace.f`, so numbered reference traces get the same bit metadata even when no selector filter is active.
- The live-compare harness now records a bounded last-match neighborhood plus the concrete mismatch records; `XFOIL_LIVE_COMPARE_CONTEXT_EVENTS` controls how many matched events are kept in that report.
- The live comparator now reuses the same trigger selector on both sides:
  - the reference stream is anchored to the trigger occurrence,
  - managed comparable events are ignored until the same managed trigger occurrence is reached,
  - full reference traces can therefore be reused safely for very narrow reruns without regenerating a second reference file.

## TODO

- Keep the C# writer and the Fortran harness filter on the same selector semantics and bit-metadata schema as new parity workflows add more targeted trace modes.
