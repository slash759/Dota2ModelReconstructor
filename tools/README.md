# External reconstruction tools

Black-box helper tools used by Dota2ModelReconstructor live under:

- `tools/legacy/phys/`
- `tools/legacy/morph/`

Keep each original bundle together, including DLLs and other dependencies. The application source remains under `src/`; these legacy binaries are isolated here so they can later be wrapped by Infrastructure runners.
