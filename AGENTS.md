## graphify

This project has a local knowledge graph in `graphify-out/` with cross-file relationships and detected communities. The project-local CLI is `./.tools/bin/graphify`.

Rules:

- For codebase questions, first run `./.tools/bin/graphify query "<question>"` when `graphify-out/graph.json` exists.
- Use `./.tools/bin/graphify path "<A>" "<B>"` for relationship questions and `./.tools/bin/graphify explain "<concept>"` for a focused concept.
- Read `graphify-out/GRAPH_REPORT.md` for broad architecture review or when a scoped query does not provide enough context.
- After modifying source code, run `./.tools/bin/graphify update .` to keep the graph current. This update uses local AST analysis and does not require an API key.

## Project skills

- Use `$build-check` after source changes, before handoff or deployment, and whenever build validation is requested. It must run both the backend and frontend builds independently.
- Use `$run-and-test-app` when a change must be verified in the live Angular UI, through Playwright, or through the Extra/history/main-run workflows.
- Use `$sync-unload-docs` whenever behavior, architecture, API contracts, configuration, background processing, persistence, or user workflows change. Documentation affected by the change must be verified and updated in the same task; do not describe intended behavior as current behavior.
- Preserve `output/` and `output/_state`; they may contain real user runs. Test cleanup must only affect processes and scratch files created by the current test.
