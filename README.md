# TV Patrol: Licence to Watch

A Unity game: patrol a small neighbourhood as a TV-licence inspector. Listen for TVs, inspect houses, and keep away from dogs and hostile residents.

## Setup

- Unity **6000.3.27f1**, URP 3D template
- Packages: AI Navigation, Input System
- Main scene: `Assets/Scenes/Neighbourhood.unity`

### Rebuilding the greybox

1. Open `Neighbourhood.unity`.
2. **TVPatrol → Build Greybox Neighbourhood**: rebuilds the map and bakes the NavMesh.
3. **TVPatrol → Create Player**: spawns the player at `PlayerStart`, connects the camera, and creates `GameSystems` (inspection, shift timer and HUD).
4. **TVPatrol → Create Dog**: spawns the guard dog on its patrol route at No. 3.
5. **TVPatrol → Create Resident**: spawns the hostile resident at No. 4.
6. Save the scene.

## Controls

| Action | Keyboard & mouse | Gamepad |
|---|---|---|
| Move | W A S D / arrow keys | Left stick |
| Look / orbit camera | Mouse | Right stick |
| Sprint (loud: the dog and resident can hear it) | Left Shift | Left stick press |
| Inspect house | Hold E (4 s, makes noise, cancels if you let go, walk away or get hit) | Hold Button North |
| Release / capture cursor | Esc / left click | – |
| Restart after the shift ends | R | – |
