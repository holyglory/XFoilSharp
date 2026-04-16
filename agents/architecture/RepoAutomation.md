# Repository Automation

This repository now carries repo-local autonomous-loop wiring for Codex-driven work.

## Repo-local files

- `.codex/autoloop.project.json`
  - Declares the verification commands the loop is allowed to run in this repo.
  - Current commands are `dotnet build src/XFoil.CSharp.slnx -c Release --no-restore` and `dotnet test src/XFoil.CSharp.slnx -c Release --no-build`.
  - `fast` runs build only; `default` and `final` run build plus test.
- `.codex/hooks.json`
  - Registers Codex `SessionStart` and `Stop` hooks that call the machine-installed `autonomous-loop` CLI.
  - The current payload uses the absolute CLI path written by machine bootstrap.
- `.agents/skills/autonomous-loop/SKILL.md`
  - Repo-local skill that tells agents how to enable, pause, resume, inspect, and disable the loop for this checkout.

## Machine prerequisite

Repo-local files are not enough by themselves. The workstation must also have:

- a machine bootstrap under `~/.codex/`,
- the `autonomous-loop` CLI installed, and
- a passing `autonomous-loop doctor`.

The current install in this environment uses a local CLI path instead of a packaged pip install because `python3 -m pip` was unavailable at install time.

## Validation

The expected repo validation command is:

```bash
autonomous-loop doctor --cwd /path/to/repo
```

For this checkout the validated command path is:

```bash
/home/holyglory/.local/bin/autonomous-loop doctor --cwd /home/holyglory/XFoilSharp
```

## Scope

- This wiring is development infrastructure only.
- It does not change solver numerics, CLI product behavior, or managed-vs-Fortran parity.
- If the machine CLI path changes, `.codex/hooks.json` must be updated to match the new bootstrap output.
