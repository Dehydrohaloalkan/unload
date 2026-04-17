# /docs — document recent code changes

## Goal
Produce or update concise developer documentation for the most recent local changes in this repo.

## Instructions
Start **exactly one** subagent to do the work.

### Subagent requirements
- **Name**: `code-docs`
- **Type**: `generalPurpose`
- **Scope**:
  - Inspect `git status` and `git diff` to understand what changed.
  - Write or update docs under `docs/ai/` (create the folder if missing).
  - If public APIs/interfaces were changed, ensure their docstrings / README sections are updated accordingly.
- **Constraints**:
  - Do **not** change production code unless documentation is missing/incorrect and the smallest safe fix is to add/update docstrings or README-style docs.
  - Keep docs short and actionable: what changed, why, and how to use/test.

## Output
- A new or updated file in `docs/ai/` describing the change set.
