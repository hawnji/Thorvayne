# Thorvayne random world (5 x 5 km)

A one-click generator for a random 5 x 5 km island world, with towns, cities, roads, rivers,
lakes, placeholder trees and cave entrances. Each world comes from a **seed** number: the same
seed always gives the same world, and a new seed gives a new world.

## Use it in Unity (Unity 6 or 2022.3, URP, Built-in or HDRP)
1. Unzip and drag the `Thorvayne` folder into your project's `Assets` folder.
   If you already have the earlier `Thorvayne` folder, let it merge (replace files if asked).
2. Wait for Unity to finish importing.
3. Menu: **Thorvayne > Random World Generator**.
4. Press **Generate world**. It takes roughly 15-40 seconds.
5. To try a different world, press **New seed**, then **Generate world** again.
   (Or use **Thorvayne > Generate Random World (new seed)** for a one-click re-roll.)

The scene camera jumps to the starting town. In the Hierarchy, double-click any town, city
or cave under `Thorvayne Random World` to fly to it.

## Walk around it (test player)
Every generated world gets a **Test Player** (a blue capsule) at the starter town. Press **Play**.
- **WASD** move, **mouse** turns the camera, **Shift** sprint, **Space** jump, **mouse wheel** zoom.
- **F** fly mode (Space up, Ctrl down, Shift to fly faster): handy for crossing 5 km quickly.
- **T** teleports to the next town, city or cave. **R** goes back to the spawn point.
- **Esc** frees the mouse; click in the Game view to grab it again.
- In the sea you swim. The top-left box shows the nearest town or cave, your position and height.
To add the player to a world you already generated, use **Thorvayne > Add Test Player**
(it also works on the world built from the hand-drawn map). It works with both the new Input System
and the old Input Manager, so no setup is needed.

## Animated character
`Characters/ThorvayneHero.fbx` is your Mixamo-rigged character, lightened from 700k to 40k triangles so it runs
well in game. It has six looping animations: **Idle, Walk, Run, Jump, Swim, Fly**. **Run** is your Mixamo running
animation (made to run in place); the others were made to match it. The Test Player uses this character
automatically, and the animations follow the controls (walk or run by speed, Jump in the air, Swim in the sea, Fly in fly mode).
- Already have a Test Player? Run **Thorvayne > Add Test Player** again to swap in the character.
- The rig is set to **Humanoid**, so free humanoid animations from the Unity Asset Store or Mixamo also work on it.
  To use one, drag the clip onto the matching state
  (Idle, Walk, Run...) in `Assets/Thorvayne/Characters/ThorvayneHero.controller` (Motion field).
  Note: Add Test Player rebuilds that controller, so redo the drag afterwards.
- Colour: change the material `Assets/Thorvayne/Generated/Mat_HeroSkin` (the Mixamo file came without a texture).
- Animation speed matching is in the `ThorvayneCharacterAnimator` component on the Test Player.
- The Blender source (`Extras/ThorvayneHero_source.blend` in the zip, outside the Thorvayne folder) has the rig and
  animations. Keep it out of `Assets` unless Blender is installed.

## What you get
- **Terrain**: 25 tiles of 1 x 1 km, heights up to 600 m, sea level at 40 m. The sea floor drops
  well below the water at the coast, so there are no flat shelves.
- **Climate**: colder in the north (pine forests, snow), warmer in the south (savanna, desert,
  palm beaches), snowy peaks on high mountains, swamps in low wet spots.
- **Rivers and lakes**: rivers follow the real downhill flow and are carved into the ground; the
  water surface is its own mesh that slopes with the river. The sea is a flat plane.
- **Towns and cities**: 3 cities, 6 towns, 10 villages by default. They are placed on flat ground,
  preferably by a river, lake or coast, with the ground levelled under them. One town near the
  middle is the starter town (with a `PlayerSpawn` next to it). Each has a `WorldMarker`
  (type, danger tier 1-5, starter flag). Danger rises with distance from the start and in harsh
  biomes. Names are random placeholders.
- **Roads** link every town (painted on the ground and smoothed into the slope), with simple
  wooden **bridges** where they cross rivers.
- **Trees**: around 100k low-poly placeholders picked by biome: broadleaf, conifer, snowy pine,
  palm, shrub and swamp tree. They keep clear of roads, towns and water.
- **Cave entrances**: about 18, at the foot of cliffs and mountain sides (and some sea cliffs),
  with a flat landing in front. Each is a stone arch with a dark opening, a `WorldMarker`
  (type `cave`) and an `EnterTrigger` box where a cave or dungeon scene can be loaded later.
- **Map picture and place list**: `Assets/Thorvayne/Generated/RandomWorld/WorldMap_seed<N>.png`
  and `Places_seed<N>.json` (names, positions, danger tiers of all towns and caves).

Generating again replaces the previous random world (Ctrl+Z brings it back). It does not touch the
world built from the hand-drawn map, which lives under its own object.

## Settings in the window
- **Land vs sea**, **Mountains**, **Rivers**, **Max lakes**, **Forest density**: shape of the world.
- **Cities / Towns / Villages / Cave entrances**: how many to place (fewer may fit on small islands).
- **Erosion**: carves natural valleys; turn off for a slightly faster, rougher world.

Other tweakables (texture tile size, water mesh detail, see-through water) are constants at the top of
`Editor/RandomWorld/ThorvayneRandomWorldBuilder.cs`. Water is solid (opaque) by default because that
always renders correctly in URP.

## Files
- `Editor/RandomWorld/ThorvayneWorldGen.cs`: the generator itself (plain C#, no Unity code).
- `Editor/RandomWorld/ThorvayneRandomWorldBuilder.cs`: the window and the code that builds the scene.
- `Editor/RandomWorld/ThorvayneTestPlayerSetup.cs`: adds the Test Player and camera.
- `Scripts/ThorvaynePlayer.cs`: the test character's movement, camera and on-screen info.
- `Scripts/ThorvayneCharacterAnimator.cs`: picks the character animation to play.
- `Characters/ThorvayneHero.fbx`: the rigged, animated character (Mixamo skeleton, Humanoid).
- `Scripts/WorldMarker.cs`: tag on towns, cities and caves (shared with the map-based world).
- `MapData/Textures/`: simple ground textures (swap in real ones any time; keep the file names).
