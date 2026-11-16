# Architecture

## Configuration

`ImageResizeFilterConfig` owns `ShaderChainConfig`. A `ShaderChainItem` stores
its relative shader path, enabled state, and parameter map. Legacy fields are
read-only migration inputs and are cleared by `MigrateLegacy()`.

`EffectProfile` merges the same configuration by reference so switching
profiles keeps chain items and parameter dictionaries intact.

## Shader pipeline

- `ShaderEffectCatalog` discovers the enabled first-release shaders under
  `effects/Common`, `effects/Downscale`, and `effects/Upscale`.
- `MagpieEffectParser` splits MagpieFX v4 metadata and code blocks, validates
  passes, and records parameter/texture/sampler/pass descriptions.
- `MagpieEffectSourceBuilder` emits HLSL, including constant buffers, SRV/UAV
  declarations, common blocks, pass entry points, and PS-style quadrant writes.
- `MagpieEffectCompiler` expands includes, strips ordinary comments, compiles
  with Vortice/D3DCompile, and stores CSO cache files.
- `ShaderChainRenderer` owns one D3D11 device, uploads RGBA pixels, executes
  each effect, adapts fixed-size output to the requested display size with
  Lanczos, reads back staging data, and returns a WPF bitmap.
- `ShaderChainService` builds the chain from the current profile and owns the
  process-wide renderer/catalog. Failures fall back to a CPU-scaled bitmap.

## UI

`ImageEffectView` replaces the interpolation combo box with a shader selector
and an ordered list. The list supports drag reordering, per-item removal, and a
parameter flyout anchored to the parameter button. The existing reset action
restores a one-item `Common/Lanczos` chain.
