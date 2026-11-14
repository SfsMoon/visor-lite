# VisorLite

**+20–40% FPS on weak GPUs, and the helmet visor looks almost the same.**

The helmet visor is drawn as two see-through layers that cover the **whole screen**: the glass and the condensation on it. Both use HDRP's full `Lit` shader. That means for every pixel on screen, twice, the game works out lighting from every nearby lamp, including its shadows. On integrated graphics and older GPUs this one effect can take a third of the frame.

VisorLite swaps both layers for cheap `Unlit` copies that keep the same texture, tint and transparency. It also keeps the condensation in sync with the game, so rain and water still fog up your visor.

## Measurements

These are automatic A/B tests. The mod is switched on and off every 10 seconds while you stand still, so heat and scene changes affect both sides equally.
Hardware: Intel Core i3-1115G4 with integrated Intel UHD Graphics, 8 GB RAM, render resolution 860×520 (vanilla).

| Game version | Location | FPS without | FPS with | Gain | 1% low |
|---|---|---|---|---|---|
| v56 | Outside (Experimentation) | 26.3 | **36.9** | **+41%** | 16.2 → 23.9 |
| v56 | Inside the facility | 49.7 | **60.7** | **+22%** | 26.8 → 31.1 |
| v80 | Outside (Experimentation) | 19.1 | **23.4** | **+22%** | 12.2 → 15.0 |
| v80 | Inside the facility | 29.5 | **35.9** | **+22%** | 18.9 → 22.6 |

The gain comes from the GPU: on these runs the main thread spent 5–10 ms less per frame waiting for the GPU. On a strong dedicated GPU you will probably gain much less, because the visor was never your bottleneck there.

## What you lose

Lit glass reflects nearby lights. With VisorLite, the faint highlights on the drops and scratches of the visor are gone. The tint, the cracks texture and the condensation are still there. Press **F1** in game to compare.

## Configuration

`BepInEx/config/visorlite.cfg`

| Setting | Default | Description |
|---|---|---|
| `Enabled` | `true` | Master switch |
| `ToggleKey` | `F1` | Switch between the cheap and the original visor in game (`None` to disable) |
| `GlassBrightness` | `0.15` | Brightness of the unlit glass tint |
| `CondensationBrightness` | `0.03` | Brightness of the condensation layer |
| `SyncCondensationWithGame` | `true` | Copy the game's live condensation (rain, water) onto the cheap visor. Turn off if your visor ever looks wrong in rain |
| `GlassMode` / `CondensationMode` | `Unlit` | `Unlit` = cheap copy, `Original` = keep the game's expensive layer, `Hidden` = don't draw it |

## Compatibility

- **Client-side only.** Other players don't need it. Works for host and clients.
- Tested on **v56** and **v80**.
- Does not touch any game logic, networking or saves.
- Mods that replace or remove the helmet visor themselves may conflict with it, or simply make it useless.

## How it works (for modders)

- The mod finds the renderers under `PlayerHUDHelmetModel` that use transparent `HDRP/Lit` materials.
- To make the copies, it clones a transparent `HDRP/Unlit` material that the game already ships (for example a fire or fog material). A brand-new `HDRP/Unlit` material set to transparent would render opaque, because Unity strips shader variants the game never uses.
- It copies the texture and the blend, depth and cull settings from the original material. The glass uses premultiplied alpha and the condensation uses additive blending.
- Every frame it copies `_BaseColor` from the original materials to the copies, because the game animates condensation (`increaseHelmetCondensation`) on the original.
- If the game reassigns the visor materials, the mod rebuilds the copies.

## Building from source

You only need the [.NET SDK](https://dotnet.microsoft.com/download) (6.0 or newer). The game is not required: game and Unity references come from NuGet (`LethalCompany.GameLibs.Steam`, `UnityEngine.Modules`, `BepInEx.Core`).

```
dotnet build -c Release                     # -> bin/Release/VisorLite.dll
dotnet build -c Release -p:Package=true     # -> release/VisorLite-<version>.zip (Thunderstore package)
```

To copy the dll straight into your game after every build, create `VisorLite.csproj.user` next to the project:

```xml
<Project>
  <PropertyGroup>
    <PluginsDir>C:\path\to\Lethal Company\BepInEx\plugins\VisorLite</PluginsDir>
  </PropertyGroup>
</Project>
```

## License

MIT, see [LICENSE](LICENSE).
