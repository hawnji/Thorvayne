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
- **WASD** move, **mouse** turns the camera, **X** switches walk/run (you run by default), **Shift** sprint,
  **Space** jump, **mouse wheel** zoom.
- **F** fly mode (Space up, Ctrl down, Shift to fly faster): handy for crossing 5 km quickly.
- **T** teleports to the next town, city or cave. **R** goes back to the spawn point.
- **Esc** frees the mouse; click in the Game view to grab it again.
- In the sea you swim. The top-left box shows the nearest town or cave, your position and height.
To add the player to a world you already generated, use **Thorvayne > Add Test Player**
(it also works on the world built from the hand-drawn map). It works with both the new Input System
and the old Input Manager, so no setup is needed.

## Animated character
`Characters/ThorvayneHero.fbx` is your Mixamo-rigged character, lightened from 700k to 40k triangles so it runs
well in game. It has seven looping animations: **Idle, Walk, Run, Jump, Swim, Fly, Chop**. **Run** is your Mixamo running
animation (made to run in place); the others were made to match it. The Test Player uses this character
automatically, and the animations follow the controls (walk or run by speed, Jump in the air, Swim in the sea, Fly in fly mode).
- **Idle** and **Walk** are your Mixamo animations ("player Idle", "Standard Walk"). To swap in other Mixamo
  animations, download them "In Place" and run `Extras/import_mixamo_anim.py` (Blender), e.g.
  `Run=run.fbx`; it copies them onto the character and puts the feet on the ground.
- Already have a Test Player? Run **Thorvayne > Add Test Player** again to swap in the character.
- The rig is set to **Humanoid**, so free humanoid animations from the Unity Asset Store or Mixamo also work on it.
  To use one, drag the clip onto the matching state
  (Idle, Walk, Run...) in `Assets/Thorvayne/Characters/ThorvayneHero.controller` (Motion field).
  Note: Add Test Player rebuilds that controller, so redo the drag afterwards.
- Skin: `Characters/ThorvayneHero_Skin.png` is the character's colour texture (from your ComfyUI model, projected onto
  the rigged character with `Extras/bake_skin.py`). Add Test Player puts it on the material `Generated/Mat_HeroSkin`.
  To repaint, edit the PNG in any paint program, or replace it with another file of the same name.
- Animation speed matching is in the `ThorvayneCharacterAnimator` component on the Test Player.
- The Blender source (`Extras/ThorvayneHero_source.blend` in the zip, outside the Thorvayne folder) has the rig and
  animations. Keep it out of `Assets` unless Blender is installed.

## Woodcutting, inventory and skills
Walk up to a tree and press **E**. The character swings an axe; each swing can give a log and Woodcutting XP.
When a tree runs out of logs it falls, leaves a stump, and grows back after a while. Moving or pressing E again stops.

| Tree | Where | Woodcutting level | XP per log | Log |
|---|---|---|---|---|
| Tree (broadleaf) | middle of the map | 1 | 25 | Logs (tree always falls after one) |
| Pine tree | north and hills | 15 | 37.5 | Pine logs |
| Palm tree | southern beaches | 30 | 67.5 | Palm logs |
| Swamp tree | low wet spots | 45 | 100 | Swampwood logs |
| Frost pine | snowy north | 60 | 175 | Frost pine logs |

Shrubs can't be chopped. XP and levels work like RuneScape (level 2 at 83 XP, 99 at 13 million).
- You need an axe, wielded or in the inventory; the best one you have the level for is used. New characters start
  with a bronze axe in hand. Axes: Bronze (1), Iron (1), Steel (6), Mithril (21), Adamant (31), Rune (41) Woodcutting;
  better axes chop faster. The axe shows in the character's hand (its head takes the metal's colour). The model is
  your ComfyUI axe, cut down to 3k triangles by `Extras/make_axe.py` and put in the hand by `Extras/add_axe.py`
  (textures `Characters/ThorvayneAxe_Albedo.png` and `ThorvayneAxe_Metal.png`). While the axe is in hand, the right hand
  closes around it (a "Grip" layer in the animator that only moves the right fingers; clip from `Extras/add_grip.py`).
- **I** (or **Tab**) opens the 28-slot inventory, **G** the equipment screen, **K** the skills list. The mouse is
  free while a panel is open. In the inventory, click gear to wear or wield it, click other items (or right-click
  anything) to drop, Shift-click to drop all of that kind. In the equipment screen, click an item to take it off.
- Equipment slots: Head, Cape, Neck, Ammo, Weapon, Body, Shield, Legs, Hands, Feet, Ring. Only axes exist so far;
  new gear is one line in `ThorvayneItems` (in `Scripts/ThorvayneInventory.cs`).
- Messages appear bottom left, XP drops on the right.
- Levels and inventory are kept between plays. **Thorvayne > Reset Test Player Progress** starts over.
- To reach the higher trees quickly while testing, select **Test Player** and raise **Xp Multiplier** on
  **Thorvayne Woodcutting** (e.g. 20). The tree list (levels, XP, regrow times) is on the same component.
- Already have a Test Player? Run **Thorvayne > Add Test Player** again so the character gets the Chop animation
  and the axe.
- Felled trees are put back when you stop Play, so the world isn't changed.

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

## Tree models
Real tree models replace the placeholders when they are in `Assets/Thorvayne/Trees/`. Right now the
**broadleaf tree** is your ComfyUI model: `Broadleaf.fbx` holds a 20k-triangle version for up close (`LOD0`) and two
crossed picture cards for far away (`LOD1`), with `Broadleaf_Albedo.png` and `Broadleaf_Impostor.png`.
Generate the world again to use it (with the same seed you get the same world, only with the new trees).
To make another one from a .glb, run `Extras/make_tree.py` (Blender) with the name of the tree it replaces
(`Conifer`, `SnowPine`, `Palm`, `Shrub`, `SwampTree`); the generator would then need one line to use it.

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
- `Scripts/ThorvayneWoodcutting.cs`: chopping trees, the axe, falling trees, stumps and regrowing.
- `Scripts/ThorvayneSkills.cs` / `ThorvayneInventory.cs` / `ThorvayneEquipment.cs`: skill XP and levels, the 28-slot
  inventory and the item list, and the equipment slots.
- `Scripts/ThorvayneGameUI.cs`: messages, XP drops, inventory and skills panels.
- `Scripts/WorldMarker.cs`: tag on towns, cities and caves (shared with the map-based world).
- `MapData/Textures/`: simple ground textures (swap in real ones any time; keep the file names).
