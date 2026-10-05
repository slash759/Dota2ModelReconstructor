# Test fixtures

Real Dota 2 compiled models are intentionally not committed to this repository.

## Primary fixture

Local file:

`pa_arcana.vmdl_c`

Observed binary metadata:

- File size: 894284 bytes
- Source 2 resource header version: 12
- Resource version: 1
- Block count: 13
- Blocks: MRPH, MDAT, MBUF, MDAT, MBUF, ANIM, ASEQ, AGRP, PHYS, CTRL, RERL, RED2, DATA

This is a useful integration fixture because it contains both **MRPH** and **PHYS** data.

Expected first-phase validation:

```text
pa_arcana.vmdl_c
  -> pa_arcana.vmdl
  -> pa_arcana.gltf
```

The generated files should be compared against Source 2 Viewer / ValveResourceFormat 19.2 output before moving on to PHYS and morph reconstruction.
