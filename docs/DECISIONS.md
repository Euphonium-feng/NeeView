# Decisions

## MagpieFX compatibility instead of a fixed enum

The resize path now consumes shader files rather than a hard-coded
`ResizeInterpolation` enum. This allows new shaders to be added without editing
the UI or renderer. Existing enum values migrate to one-item chains.

## CPU fallback

The whole chain falls back to a CPU-scaled result when D3D11 or compilation
fails. A partially executed chain would make display behavior depend on which
shader failed and was harder to explain.

## Four quadrant writes for PS-style passes

Magpie PS-style entry points dispatch 16x16 blocks but each thread group is
64 threads. Each thread therefore writes four 8x8 quadrants. The initial
single-quadrant implementation produced the observed checkerboard pattern.

## Explicit RGBA transfer

The renderer uploads and processes RGBA textures, then converts the readback to
WPF BGRA. This avoids ambiguous color-channel assumptions in WPF and guarantees
consistent behavior for the Common shaders.

## Licensing

The first-release shaders include permissive sources, while CuNNy2, RAVU, and
NNEDI3 are GPLv3. Their notices are recorded under `effects/NOTICE.txt`; any
distribution that includes them must also include the GPLv3 text and source
availability information.
