# Shader directories

- `Common/` contains resampling shaders usable for both upscaling and
  downscaling.
- `Downscale/` contains perceptual downscalers.
- `Upscale/` contains fixed-scale and neural upscalers and their dependencies.
- `CuNNy2/`, `RAVU/`, and `NNEDI3/` are retained for later extension work and
  are GPLv3.

The first-release catalog is defined in
`NeeView/Effects/Shaders/ShaderEffectCatalog.cs`.
