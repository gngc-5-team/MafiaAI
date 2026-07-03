# MafiaAI Claude Code Handoff

## Project
- Unity project path: `/Users/kang-yumin/Documents/UnityWorks/MafiaAI`
- Scene: `Assets/Scenes/SampleScene.unity`
- Main runtime object: `GameController`
- Local LLM target: Ollama `gemma3:4b` via `http://localhost:11434`

## Current Game Direction
- AI mafia game with spatial conversation.
- Day phase lasts about 60 seconds.
- Night phase lasts about 10 seconds.
- NPCs and player exist in a top-down 2D mansion.
- Only characters in the same room can hear and participate in room conversation.
- AI-to-AI or AI-to-player dialogue should be paced for readability, roughly 3-5 seconds between lines.
- Player can speak during day and can target/press same-room characters.

## Important User Preference
- Do not generate the mansion/background/room sprites at runtime.
- The user wants to place and edit 2D Object Sprite objects directly in the Unity scene before Play Mode.
- Gameplay code must bind to those pre-placed scene objects.
- The generated map/background logic that created `SpriteMansion` was intentionally removed.

## Scene-Authored Map Structure
The user placed the map manually in `SampleScene`.

Expected object names:
- Rooms: `room1`, `room2`, `room3`, `room4`, `room5`
- Corridors: `corridor1`, `corridor2`, `corridor3`, `corridor4`
- Visual wall children:
  - Rooms have children named like `roomline`, `roomline (1)`, `roomline (2)`
  - Corridors have children named like `corridorline`, `corridorline (1)`

Interpretation:
- The white/blue room and corridor sprites define walkable floor.
- The black `roomline` / `corridorline` sprites are visual wall borders.
- Player and AI must not cross the black wall borders.
- Movement should be constrained by the walkable floor bounds, not by generated code geometry.

## Work Already Done

### `Assets/MafiaAI/Scripts/UI/SpriteMansionView.cs`
- Replaced runtime-generated mansion map with scene-object binding.
- On Awake:
  - Removes old generated child `SpriteMansion` if present.
  - Finds `room1~room5` and `corridor1~corridor4`.
  - Reads their `SpriteRenderer.bounds`.
  - Uses those bounds as walkable areas.
  - Uses room bounds as logical room regions: `방1~방5`.
- Creates runtime actor tokens only under `RuntimeActors`.
- Supports WASD/arrow-key player movement using the Unity Input System.
- Blocks player movement if the next position is not inside any walkable room/corridor rect.
- Updates logical room when the player enters a room rect.
- NPC tokens move toward their current room target positions.
- Camera currently needs follow behavior added/refined.

### `Assets/MafiaAI/Scripts/LLM/GameController.cs`
- Room list changed to `방1`, `방2`, `방3`, `방4`, `방5`.
- Initial character rooms use those five rooms.
- Same-room chat/hearing now uses those room ids.
- NPC logical room graph was set roughly as:
  - `방1 <-> 방2`
  - `방2 <-> 방3`
  - `방3 <-> 방4`
  - `방3 <-> 방5`

### `Assets/MafiaAI/Scripts/UI/MafiaUI.cs`
- Conversation log can be hidden/shown.
- `Tab` toggles log panel.
- `Enter` focuses chat input.
- `Escape` unfocuses chat input.
- Default movement mode leaves WASD available for player movement.

## Current User Request To Continue From Here
The latest user instruction:
- Player and AI must be able to move from room to room through corridors.
- Currently they cannot properly move through corridors.
- Fix corridor traversal.
- Make the camera follow the player.
- The camera should not show the whole map.
- The user already adjusted the camera size to show about one room, so do not force a full-map camera size.
- Before doing that work, create this handoff markdown file so Claude Code can continue if needed.

## Latest Fix Applied After This Handoff Was Created
- Created this handoff file first, as requested.
- Fixed the corridor traversal issue in `SpriteMansionView.cs`.
  - Previous issue: room and corridor bounds touched exactly, but code shrank every walkable rect by actor radius. That created invisible gaps between rooms and corridors.
  - Fix: walkable areas now use the original floor sprite bounds for `room1~room5` and `corridor1~corridor4`.
  - Logical room detection still uses a lightly padded room rect so conversation room changes happen inside rooms, not at the outer edge.
- Added camera follow in `SpriteMansionView.cs`.
  - Camera now follows the human actor smoothly.
  - Code no longer zooms out to show the whole map.
  - Existing `Camera.orthographicSize` is preserved. At verification time it remained `5.5`.
- Fixed actual room graph in `GameController.cs`.
  - `방1 <-> 방2`
  - `방2 <-> 방3`
  - `방3 <-> 방4`
  - `방4 <-> 방5`
- Verified after changes:
  - Unity compile console: 0 errors.
  - Play Mode starts.
  - Runtime camera position follows the player.
  - No generated `SpriteMansion` root object is present.

## Recommended Next Implementation
1. Inspect runtime bounds of `room1~room5` and `corridor1~corridor4`.
2. Fix walkability:
   - Treat walkable areas as the union of all room/corridor sprite bounds.
   - Use a small movement radius so actors do not visually overlap black wall lines.
   - If shrinking corridor rects blocks narrow doorways, shrink less or use point-sampling around actor radius instead of shrinking the whole corridor.
3. Fix player room detection:
   - While in a corridor, keep the previous logical room or set a transient corridor state only if needed.
   - Only same-room conversation should trigger in actual room regions.
4. Fix AI traversal:
   - AI should not teleport between rooms.
   - It should move through corridor waypoints or at least move continuously along walkable rects.
   - If full pathfinding is too much for now, use deterministic waypoints based on scene corridor centers.
5. Camera follow:
   - Follow the human token smoothly.
   - Preserve the camera's current orthographic size instead of setting it to full-map size.
   - Clamp optional, but do not zoom out to the whole map.

## Verification Checklist
- Unity compile console has 0 errors.
- Play Mode starts.
- No `SpriteMansion` generated map is created.
- Runtime actors appear on top of the user's scene sprites.
- Player can walk from one room into corridor and into another room.
- Player cannot cross black wall borders.
- Camera follows player with current zoom.
- Same-room chat still filters by logical room.
