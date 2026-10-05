# Dota2ModelReconstructor

Reconstruction pipeline for Dota 2 Source 2 models.

## Current phase

Phase 1 isolates the Source 2 Viewer / ValveResourceFormat functionality required to:

- read `.vmdl_c`
- decompile the model to editable ModelDoc/VMDL content
- export the model to glTF/GLB

The project is intentionally pinned to **ValveResourceFormat 19.2.6339** / Source 2 Viewer **19.2** (source commit `c722083`) so output does not silently change with newer VRF releases.

## Attribution

Powered by Source 2 Viewer (ValveResourceFormat).

ValveResourceFormat is Copyright (c) ValveResourceFormat Contributors and is distributed under the MIT License. See `third_party/ValveResourceFormat/LICENSE`.

## Scope

Blender automation, Dota 2 Workshop Tools automation, PHYS helpers, and morph helpers are deliberately outside this first phase.
