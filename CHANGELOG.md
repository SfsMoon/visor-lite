# Changelog

## 1.1.0

### Fixed
- **The screen turned blue in rain and under water.** To get a shader variant that is guaranteed to exist in the build, the cheap visor is cloned from a transparent `HDRP/Unlit` material that the game already uses. In 1.0.x that template was often `BlueFlameMaterial`, and its blue emission came along with it. In dry weather the condensation layer is almost invisible, so nobody noticed. When the game raises the condensation (rain, water), the layer shows up and the inherited glow covered the whole screen.
  - Every cheap layer now has the template's emission stripped: emissive colour, emissive colour map, emissive intensity and the `_EMISSIVE_COLOR_MAP` keyword.
  - Distortion (`_DistortionEnable`, `_DistortionOnly`, `_DISTORTION`) and alpha clipping (`_AlphaCutoffEnable`, `_ALPHATEST_ON`) inherited from the template are switched off as well.

### Changed
- **Template choice.** Instead of the first transparent unlit material found, the mod now picks the plainest one: no emission, no emissive map, no distortion, and materials with "flame" in their name come last. The chosen template is written to the log (`Using '...' as unlit template`).

### Added
- `SyncCondensationWithGame` (default `true`): copies the game's live visor effects (condensation in rain and water) onto the cheap layers every frame. When it is off, the cheap visor never changes.
- `GlassMode` and `CondensationMode` (default `Unlit`), one per visor layer:
  - `Unlit`: cheap copy (this is where the speed-up comes from).
  - `Original`: keep the game's expensive layer. Useful if you prefer the vanilla look of a layer. The condensation layer alone cost around 19% FPS in testing.
  - `Hidden`: don't draw the layer at all.
- Layer modes can be changed in game (for example with LethalConfig). The cheap layers are rebuilt immediately.

### Tested
- v56, in rain.

## 1.0.1
- Added the GitHub link to the Thunderstore page.
- New icon.

## 1.0.0
- First release. The full-screen helmet visor (glass and condensation) is rendered with cheap `HDRP/Unlit` copies instead of two `HDRP/Lit` layers that run the full light loop for every pixel. Measured +41% / +22% FPS outside / inside on v56 and +22% on v80 (Intel UHD).
- The copies keep the original texture, tint and blend setup (premultiplied glass, additive condensation).
- Condensation follows the game's own animation (rain, water).
- The visor is found again after scene changes, death and spectating. If the game reassigns the visor materials, the copies are rebuilt.
- F1 toggles between the cheap and the original visor. Brightness of both layers is configurable.
- Builds from NuGet (`LethalCompany.GameLibs.Steam`), no game install needed.
