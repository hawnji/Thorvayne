# Thorvayne prototype world (built from the world map)

## Use it in Unity (2021.3 LTS or newer, Built-in, URP or HDRP)
1. Unzip and drag the `Thorvayne` folder into your project's `Assets` folder.
2. Wait for Unity to finish importing.
3. Menu: **Thorvayne > Build World From Map**.

You get a `Thorvayne World` object in the open scene with:
- **Terrain**: 2048 x 1536 m (1 map pixel = 1 m), heights up to 320 m, sea level at 30 m.
  Snow mountains NW, dark plateau with cliff walls N, hills NE, rolling west woods,
  soft starter grassland in the middle, dunes S, carved rivers and lakes, three islands.
- **10 painted ground layers**: grass, meadow, dryland, sand, snow, cliff (auto on steep slopes),
  darkrock, dirt (forest floor), path (roads traced from the dotted lines), scorched (desert war camp).
- **Roughly 10-15k low-poly trees**: conifers, autumn broadleaf (west wood), snowy pines (NW).
- **Sea plane** covering ocean, rivers and lakes.
- **32 placeholder settlements/landmarks** (villages, towns, the SE city, castles, fortresses,
  towers, elven spire, war camps). Each has a `WorldMarker` (type, danger tier 1-5, starter flag);
  placeholder colours get darker as danger tier rises. Names are placeholders until the world is named.
- **PlayerSpawn** next to the Starter Village in the centre.

Running the menu again replaces the previous build. Tweakables (tree spacing/density, texture tile size)
are constants at the top of `Editor/ThorvayneWorldBuilder.cs`.

## Re-generating the data from an edited map
`Tools/map_to_terrain.py` turns the map picture into the files in `MapData`:
```
pip install numpy scipy pillow
python Tools/map_to_terrain.py Tools/thorvayne_map.jpg MapData
```
World size, height scale, sea level and the marker list (positions, types, danger tiers, starter flag)
are at the top / middle of the script.

## Files in MapData
- `thorvayne_height.r16`: 1025x1025 16-bit heightmap (little endian, row 0 = south). Also loads in
  Unity's own Import Raw (Windows byte order) if you prefer.
- `thorvayne_splat.bytes`: 1024x1024 x 10 layer weights.
- `thorvayne_trees.bytes`: 512x512 x 3 tree densities (conifer, broadleaf, snow pine).
- `thorvayne_world.json`: sizes, layer list, markers.
- `Textures/`: simple tileable ground textures (swap in real ones any time; keep the file names).
