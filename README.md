# VACAC Graduate Software Engineer Task 2026: Modular Conveyor System

**Author:** Christopher Howell
**Repository:** https://github.com/ChrisHowDev/VACAC-Work-Sample

## Overview

A modular conveyor builder made in Unity out of the provided conveyor models. The player picks a conveyor from an on-screen panel, places it in the environment, and it snaps to nearby conveyors to form a continuous line. Item spawners drop products onto the belts, and the products are carried along them.

### Features

- **Conveyor selection and placement.** Pick a belt from the panel or with the number keys. A ghost preview follows the cursor and is tinted red when a belt can't be placed.
- **Snapping.** Snapping is always on and works with every provided belt length. Belts snap end to end, and at 90° turns. Sloped belts snap to the correct height, and belts placed after a slope continue at the raised height. Overlapping placement is blocked.
- **Moving products.** Item spawners drop products with a marker showing where they will land. Belts carry the products along using trigger volumes and physics.
- **Belt direction.** Each belt can be reversed in place, and the direction arrows on the belt change to match.
- **Belt speed.** A slider in the panel changes the belt speed, and the arrow animation speed follows it.
- **Arrow shader.** The scrolling direction arrows are made with a Shader Graph material.
- **Delete and reverse tools.** Hovered belts are highlighted before you click.
- **Extras.** Free camera, fullscreen toggle (F11), and a quit button.

## Controls

| Input | Action |
|---|---|
| Left mouse | Place the selected conveyor or spawner (or apply the delete / reverse tool) |
| `1`–`4` | Select an item, numbered in panel order (conveyors first, then spawners) |
| `R` | Rotate the item being placed |
| `F` | Flip the direction of the belt being placed |
| `X` | Delete tool: click a belt or spawner to remove it |
| `V` | Reverse tool: click a belt to reverse its direction |
| `Esc` | Cancel the current selection or tool |
| `F11` | Toggle fullscreen |

**Camera**

| Input | Action |
|---|---|
| `W` `A` `S` `D` | Move |
| `Shift` | Move up |
| `Ctrl` | Move down |
| Hold right mouse and move | Look around |
| Scroll wheel | Zoom |

The panel (top left) also has a belt speed slider, a "Move cursor to next snap" option, a fullscreen checkbox and a Quit button.

## Development tools

- Unity 6 (6000.3), Universal Render Pipeline
- Shader Graph for the arrow material
- Visual Studio 2026
- Git and GitHub
- Claude Sonnet 

## Project structure

| Script | Purpose |
|---|---|
| `ConveyorPlacerSimple` | Placement, ghost preview, snapping, overlap checks, delete / reverse tools and the on-screen panel |
| `ConveyorForce` | Moves items along a belt and updates the belt's arrow visuals when reversed or when the speed changes |
| `ConveyorManager` | Holds the shared belt speed settings |
| `ItemSpawner` | Spawns products at an interval and shows a drop marker |
| `FreeCam` | Free-fly camera |

## Credits and notes

- Metal texture from [Architextures](https://architextures.org/).
- `.gitignore` based on GitHub's [Unity template](https://github.com/github/gitignore/blob/main/Unity.gitignore).
- `FreeCam` is modified from [TollyH/Unity-FreeCam](https://github.com/TollyH/Unity-FreeCam), updated to use the new Input System.

- **Large models removed from the repository.** Four of the provided models are over GitHub's 100 MB file limit, so they are not in the repo. They are in the zipped build. To open the project in Unity, copy them from the original pack into the same folders (their `.meta` files are included so the prefabs reconnect):
  - `Assets/VACAC/BOUTIQUE/Conveyors/VA_incline_Conveyor_Belt.fbx`
  - `Assets/VACAC/BOUTIQUE/Conveyors/VA_Conveyor_Belt_Short.fbx`
  - `Assets/VACAC/BOUTIQUE/Conveyors/VA_Conveyor_Belt_Generic.fbx`
  - `Assets/VACAC/BOUTIQUE/Products/VA_Cardboard_Box.fbx`

## Running the build

Unzip the release and run the `.exe` from inside the unzipped folder (keep the `_Data` folder next to it). Windows, 64-bit.
