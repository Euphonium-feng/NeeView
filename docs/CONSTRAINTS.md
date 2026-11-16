# Constraints

- Target framework is `net10.0-windows` with WPF and x64.
- This checkout was initialized locally with `git init`; the public remote and
  first push are not yet complete.
- MagpieFX v4 is the shader metadata format. `STYLE PS` and compute-style
  passes are both supported.
- MagpieFX `OUTPUT` size expressions use `INPUT_WIDTH`, `INPUT_HEIGHT`,
  `OUTPUT_WIDTH`, and `OUTPUT_HEIGHT`.
- PS-style passes write four 8x8 quadrants per 16x16 block. All four writes
  are required; omitting three produces the repeated checkerboard artifact.
- Constant buffers must match HLSL packing. Integer size fields are written as
  their bit patterns, not as floating-point values.
- Image pixels are transferred as RGBA and converted back to BGRA only when
  producing a WPF `BitmapSource`.
- Shader compilation is cached by source, generated source, macros, and
  compiler version. Changing generated HLSL requires a compiler cache-version
  bump.
- Do not launch the application for acceptance; the user performs live
  acceptance checks. Offline D3D11 rendering tests are allowed.
