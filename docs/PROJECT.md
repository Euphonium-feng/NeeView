# NeeView Shader Chain

## Goal

Replace the single resize interpolation selector with an ordered, MagpieFX-compatible
shader chain. The chain is evaluated on D3D11 and falls back to the existing
PhotoSauce CPU path when GPU processing is unavailable.

## Requirements

- The resize panel shows an ordered list of shaders. Each item can be removed,
  reordered, and has a parameter flyout when the shader declares parameters.
- Parameters are saved immediately and can be reset to shader defaults.
- The first release exposes 12 shaders: Nearest, Bilinear, Bicubic, Lanczos,
  Jinc, SSimDownscaler, SGSR, FSR EASU, FSR RCAS, NIS, Anime4K Upscale S, and
  Anime4K Upscale L.
- Shader discovery is directory-based. Adding a compatible `.hlsl` under
  `effects/` does not require a C# mapping.
- The existing single `ResizeInterpolation` value migrates to an equivalent
  one-item chain on load. New installs default to `Common/Lanczos`.
- A failed GPU chain must not break image display. The whole chain falls back
  to CPU Lanczos and keeps the saved chain for the next attempt.

## Acceptance

- Existing interpolation choices load as the corresponding chain item.
- Basic sampling, multi-pass downscaling, fixed-2x/3x upscaling, and parameter
  editing produce a complete image at the requested display size.
- The application builds successfully in the Windows GitHub Actions workflow.
- Shader and third-party notices ship with the effects directory.

## Out of scope

- Arbitrary user-authored parameter types beyond MagpieFX float/int parameters.
- GPU execution of GPL-only shaders without the distribution obligations below.
