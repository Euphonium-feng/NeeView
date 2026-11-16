# TODO

## Implemented

- Ordered shader chain configuration with legacy interpolation migration.
- MagpieFX v4 parser and D3DCompile pipeline.
- D3D11 chain renderer with CPU fallback.
- Effects directory discovery and first-release 12-shader catalog.
- Shader selector, ordered list, drag reorder, delete, parameter flyout, and
  parameter reset.
- Offline compile validation of all 12 first-release shaders.
- Offline pixel validation of Bilinear, Lanczos, SSimDownscaler (5 passes), and
  Anime4K Upscale S at a non-integer target size.
- WPF build succeeds locally with zero errors (0 errors; existing warnings from
  third-party projects remain).
- Local `git init`, `.github/workflows/windows-build.yml`, GPLv3 text, and
  third-party shader notice are in place.

## Open

- **Next action:** create the public `Euphonium-feng/NeeView` repository, make
  the initial commit, push, and run
  `gh run watch --exit-status` until the Windows workflow is green. The previous
  `gh repo create` failed only because there was no commit yet.
- Live acceptance of the resize panel and displayed image is user-owned; do
  not launch NeeView for acceptance.
- Confirm the remaining UI details during acceptance: parameter flyout
  anchoring, drag reorder hit-testing, and list appearance in the narrow side
  panel.
- Decide whether GPLv3 shaders are distributed or only supplied as external
  extensions before producing a release package.
- Performance profiling on large images and lower-end GPUs is not yet done.
