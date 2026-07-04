# MafiaAI Claude Code Handoff

Last updated: 2026-07-04

Latest Codex update: 2026-07-04 evening

## Project Context

- Unity project path: `/Users/kang-yumin/Documents/GitHub/MafiaAI`
- Primary working scene: `Assets/Scenes/YuminScene.unity`
- Reference scene: `Assets/Scenes/JongHoonScene.unity`
- Rule from user: work in `YuminScene`, but when setup/components/serialized fields overlap with `JongHoonScene`, use `JongHoonScene` as the reference baseline.
- Main runtime object: `GameController`
- Local LLM target: Ollama through `OllamaClient`
- Current style target: 2D top-down mansion, Among Us-like movement, AI mafia discussion in rooms.

## Important User Preferences

- Do not generate the mansion/background/room sprites only at runtime.
- The user wants room/corridor/map sprites visible and editable in the Unity scene before Play Mode.
- If adding visual objects such as lights, put them in the scene hierarchy so the user can inspect and move them.
- Avoid hiding important setup inside a controller that only creates visuals after pressing Play.
- Player should move with WASD through rooms and corridors.
- Same-room conversation matters: characters should only hear/talk with others in the same room.
- AI dialogue should remain readable, roughly 3-5 seconds between lines where possible.

## Scene-Authored Map Structure (2026-07-04 오후 갱신)

Scene objects in `YuminScene` (정리 완료):

- Templates only: `room1`, `corridor1` (자식 없음, SpriteRenderer 알파 0으로 숨김)
- `room2..N` / `corridor2..N-1` are cloned **at runtime** from the templates by `SpriteMansionView.EnsureSlots()` (GameConfig.RoomCount 기준)
- Old `roomline`/`corridorline` black-line children and root `Line` Text remnants were **deleted** — walls are now drawn by the tilemap autotiler (below)
- `Grid/Tilemap`: visual floor/walls. Repainted each round by `SpriteMansionView.PaintTiles()` to match the randomly generated mansion layout

Interpretation:

- room/corridor sprite **bounds** still drive all logic: walkable area, room membership for conversation filtering (`BindSceneMap`). They are invisible but must not be deleted (especially room1/corridor1 templates).
- Visuals come from the tilemap autotiler: walkable cells = floor tiles (`tile11_4`), walls ring the outside. 비스듬한 탑뷰 — 뒷벽(위)만 면(top/down_half_of_the_frontwall), 앞/좌/우는 선벽(`Straight_side_wall` 회전), 볼록코너 `Point_Shape_wall2`, 개구부 오목코너 `L_shape_wall`, 배경 `Black_background`. Tile assets are wired as `[SerializeField]` on `SpriteMansionView`.
- Runtime repaint here is intentional & user-approved: the room layout itself is random each round, so tiles must follow. (An editor preview of the last painted layout stays saved in the scene.)

## Main Files Touched

- `Assets/Scenes/YuminScene.unity`
- `Assets/MafiaAI/Scripts/LLM/GameController.cs`
- `Assets/MafiaAI/Scripts/UI/SpriteMansionView.cs`
- `Assets/MafiaAI/Scripts/UI/MansionLightingController.cs`
- `Assets/MafiaAI/Scripts/UI/ChatLogUI.cs`
- `Assets/MafiaAI/Scripts/UI/TopBarUI.cs`
- `Assets/MafiaAI/Scripts/UI/AvatarBarUI.cs`
- `Assets/MafiaAI/Scripts/UI/RolePanelUI.cs`
- `Assets/MafiaAI/Scripts/UI/InputBarUI.cs`
- `Assets/MafiaAI/Scripts/UI/ChoiceOverlayUI.cs`
- `Assets/MafiaAI/Scripts/UI/MapUI.cs`
- `Assets/MafiaAI/Scripts/UI/NightMovementUI.cs`
- `Assets/MafiaAI/Lighting/MansionMoodProfile.asset`
- `Assets/MafiaAI/Materials/MansionSpriteLit.mat`
- `Assets/MafiaAI/Scripts/Core/Persona.cs`
- `Assets/MafiaAI/Scripts/LLM/Actors.cs`
- `Assets/MafiaAI/Scripts/LLM/PromptBuilder.cs`
- `Assets/Prefabs/GameController.prefab`
- `Assets/sprites/char/cha_*.aseprite`

## Work Already Completed

### Character Skin Fixed Mapping (2026-07-04 evening, Codex)

User replaced/added Aseprite character files:

- Deleted old `Assets/sprites/char/cha_2.aseprite`
- Added `Assets/sprites/char/cha_2-2.aseprite`
- Added `Assets/sprites/char/cha_3.aseprite`
- Added `Assets/sprites/char/cha_5.aseprite`
- Existing: `cha_1`, `cha_4`, `cha_6`

Required fixed mapping:

- `cha_1` → `카이`
- `cha_2-2` → `미로`
- `cha_3` → `노아`
- `cha_4` → `세이`
- `cha_5` → `제로`
- `cha_6` → `하루`

Implemented:

- `SpriteMansionView.CharacterSkin` now has `playerId`.
- `SpriteMansionView.FindSkinForPlayer(playerId, seatIndex)` now selects by:
  1. explicit `CharacterSkin.playerId`
  2. default label mapping from Korean player name
  3. old seat-index fallback
- `Assets/Prefabs/GameController.prefab` and the `YuminScene` `GameController` instance were both configured with 6 skins.
- Verified serialized mapping through Unity:
  - Prefab: `카이=cha_1; 미로=cha_2-2; 노아=cha_3; 세이=cha_4; 제로=cha_5; 하루=cha_6`
  - Scene: same mapping
- Verified frame counts:
  - `카이=cha_1 idle:10 walk:8`
  - `미로=cha_2-2 idle:9 walk:7`
  - `노아=cha_3 idle:9 walk:7`
  - `세이=cha_4 idle:9 walk:8`
  - `제로=cha_5 idle:9 walk:7`
  - `하루=cha_6 idle:9 walk:7`

Important:

- Do not go back to seat-order skin assignment. The user's intent is fixed identity-to-skin mapping.
- If new characters are added later, update `DefaultSkinLabel()` and the prefab/scene `_charSkins` list.

### Name Label Editor Controls (2026-07-04 evening, Codex)

User wanted character name text position editable from Unity Editor.

Implemented in `SpriteMansionView`:

- Added enum `NameLabelAnchor`:
  - `AboveHead`
  - `BelowFeet`
  - `Custom`
- Added serialized fields under header `캐릭터 이름표`:
  - `_nameLabelAnchor`
  - `_nameLabelCustomOffset`
  - `_nameLabelAboveHeadY`
  - `_nameLabelBelowFeetY`
  - `_nameLabelFontSize`
  - `_nameLabelCharacterSize`
  - `_nameLabelSortingOrder`
- `AddLabel()` now uses `NameLabelOffset()` instead of hard-coded offsets.
- `ApplyNameLabelSettings()` runs during `Update()` so values changed in Inspector during Play Mode immediately apply to runtime labels.

Where to adjust:

- Select `GameController` in `YuminScene`
- Open `Sprite Mansion View`
- Adjust `캐릭터 이름표` fields

### AI Dialogue Prompt / Harness Work (2026-07-04 evening, Codex)

User complained NPCs were too "stupid" and did not play mafia well:

- Repeated empty polite phrases:
  - `무슨 의미죠?`
  - `걱정스럽습니다`
  - `다행입니다`
  - `명확히 답변드리겠습니다`
  - `필요성을 느낍니다`
- NPCs treated `직업 뭐임?` as a normal request and kept saying everyone should reveal roles.
- User wanted real mafia-game reasoning: suspicion, alibi pressure, vote-line reading, role reveal risk, reverse pressure.

Implemented:

- `Persona.cs`
  - Strengthened each persona's Korean speech style:
    - 카이: aggressive 반말
    - 제로: cold analytic 존댓말
    - 미로: playful mixed speech
    - 하루: emotional mixed speech
    - 노아: conspiracy-style 존댓말
    - 세이: short blunt 반말
- `PromptBuilder.cs`
  - Reframed daytime talk away from rigid "role verification" into actual mafia-game incentives.
  - Added explicit principle:
    - early, baseless role reveal requests usually benefit mafia because they expose police/doctor.
  - NPCs should treat role-fishing as suspicious rather than complying.
  - Better question targets:
    - alibi
    - movement route
    - changed statements
    - silence
    - vote intention
    - why someone is pushing role reveal
  - Removed/softened overly rigid wording that caused NPCs to mechanically ask for "직업".
- `Actors.cs`
  - Added output harness `EnsureUseful()`.
  - Detects weak/non-productive output and replaces it with persona-specific mafia-game lines.
  - Weak output now includes:
    - `무슨 의미`
    - `무슨 소리`
    - `걱정스럽`
    - `다행`
    - `명확하게 대답`
    - `필요성을 느끼`
    - `요청드립니다`
    - `직업을 밝`
    - `각자의 직업`
    - etc.
  - Added `ContainsRoleFishing()` and role-fishing fallback lines.
  - Added `ContainsAlibiTopic()` and alibi fallback lines.
- `GameController.cs`
  - `LocalTranscript()` now slices recent lines per room correctly instead of using total `_roomLines.Count`.
  - `OneSentence()` now keeps up to two sentence-ending marks and raises length cap to 150 chars so useful reasoning is not cut off too early.

Design note for next agent:

- The goal is not "stricter prompt rules". The user explicitly noticed that overly strict prompts make NPCs robotic.
- Prefer fewer rules plus stronger mafia-game heuristics.
- Real mafia behavior to preserve:
  - Do not reveal police/doctor early without a reason.
  - If someone asks roles too early, suspect them.
  - Ask for route + witness, not just "where were you".
  - Force symmetry: if you ask my alibi, give yours too.
  - Vote intent matters near voting.
  - Mafia may fake helpful analysis or redirect suspicion.

Verification so far:

- Unity compile refresh passed.
- Unity console errors/warnings: 0 after latest script changes.
- A short sample before final refinements still showed some weak phrases; the final refinements were added after that sample, but a longer gameplay test is still recommended.

### Tilemap Autotiler + Scene Cleanup (2026-07-04, 다른 Claude 세션)

- `SpriteMansionView.PaintTiles()` autotiler v6.1: 매판 랜덤 생성되는 방/복도 레이아웃에 맞춰 타일 자동 배치.
  - walkable 칸 집합(방 10x10 + 복도 10x4)을 바닥으로 꽉 채우고(이동공간=시각 일치), 벽은 그 바깥 링에만(`PaintRoomRing`/`PaintCorridorRing`) — 복도가 방을 관통하지 않고 개구부 자동.
  - 세로 복도 측벽은 순수 gap 구간에만 세워 방 뒷벽과의 검정 틈 제거.
  - TilemapRenderer sortingOrder = 1 (`_tilemapSortingOrder` 필드).
- 머지 후 컴파일 에러 21개 복구: `SpriteMansionView`에 밤 필드(`_nightMask` 등 5개) 재선언 + `using TMPro;` 추가.
- 씬 정리: room2~5, corridor2~4(런타임 복제로 대체), roomline/corridorline 자식, 루트 Line Text 잔재 삭제. room1/corridor1 템플릿만 유지.
- 상세 히스토리: 이 세션 메모리 `mansion-autotiler.md` 참조 (~/.claude/projects/.../memory/).

### Scene Sync / UI Controller Fix

- `YuminScene` was synchronized toward `JongHoonScene` where overlapping UI/controller setup existed.
- A duplicate stray `GameController` on `Canvas/TopBar` was removed.
- Several UI scripts previously had `[RequireComponent(typeof(GameController))]`, which caused Unity to add extra `GameController` components onto UI objects.
- That pattern was removed from UI view scripts.
- UI scripts now fall back to finding the main scene `GameController` instead of requiring one on their own GameObject.
- Verified at runtime that there is only one `GameController`.

### Conversation Display Fix

Problem observed:

- Console logs showed characters talking, but game screen felt silent.
- Some displayed lines duplicated the speaker, like `노아 [방] 노아 -> ...`.
- Questions felt duplicated because duplicate game loops were running.

Fixes:

- `ChatLogUI` no longer prepends speaker name to speech text because `GameController.EmitRoomSpeech` already formats the room/speaker/target.
- `GameController.Emit` no longer duplicates the speaker in debug logs.
- Added world-space speech bubbles in `SpriteMansionView` above speaking actors.
- Speech bubbles use a black translucent background and `TextMesh`, shown for about 4.5 seconds.
- Runtime verified:
  - `GameController=1`
  - Speech bubble `TextMesh` objects are created.
  - Unity console errors: 0.

### Movement / Camera / Map

- `SpriteMansionView` binds to scene-authored `room1~room5` and `corridor1~corridor4`.
- It creates actor tokens under `RuntimeActors`.
- Player moves with WASD/arrow keys.
- Camera follows the human token while preserving the user's chosen orthographic size.
- It should not zoom out to show the full map.
- The old generated `SpriteMansion` root is cleaned if found.

### 2D Lighting / Mood Work

User asked to improve depth and immersion with Unity 2D lights, inspired by:

- `https://unity.com/kr/how-to/use-2d-lights-unity-set-mood#polishing-the-scene`

Current project already uses URP with 2D renderer:

- Pipeline asset: `Assets/Settings/UniversalRP.asset`
- Renderer: `Assets/Settings/Renderer2D.asset`
- Existing scene had `Global Light 2D`.

Implemented:

- Created scene hierarchy root: `Lighting`
- Under it:
  - `Room Lamps`
  - `Corridor Fill Lights`
  - `Atmosphere`
- Created `Assets/MafiaAI/Materials/MansionSpriteLit.mat` using `Universal Render Pipeline/2D/Sprite-Lit-Default`.
- Applied lit sprite material to room/corridor/tilemap renderers so 2D lights affect them.
- Added `Assets/MafiaAI/Lighting/MansionMoodProfile.asset` with:
  - Bloom
  - Vignette
  - Color Adjustments
- Camera has post-processing enabled.
- Added `MansionLightingController`:
  - Adjusts global light between day/night.
  - Applies subtle flicker to room lights.
  - Does not create the room lights at runtime.

Latest user correction:

- Player should not carry a light.
- Room lights should be mounted in the wall, not on the floor or center.
- ~~Each room light should shine diagonally into the room.~~ → (2026-07-04 오후 갱신) user now wants each room light to **softly illuminate the whole room** ("방 전체를 은은하게").
- Corridors should have one light each.

2026-07-04 오후 구현 (다른 Claude 세션):

- `MansionLightingController.SnapLightsToLayout()`: rooms move every round (`LayoutMansion` random layout), so on `OnGameSetup` (+1 frame) the controller snaps existing scene lamps to the new layout — no runtime light creation.
  - `Lamp_Room{i}_WallDiagonal` → room i back wall center (roomCenter + (0, 4.3)), rotZ 180 (cone points down into room), wide cone 110°/160°, outer radius 11.5 (covers whole 10x10 room), warm amber #FFB86B, intensity 0.85. Sconce markers follow.
  - `Corridor{i}_WallLight` (+ markers) → corridor i center.
  - Tunables exposed as serialized fields on `MansionLightingController` (intensity/radius/angles/color).
  - Flicker base intensities re-cached after snap.

Current lighting state:

- `Player Sight Light` removed.
- `MansionLightingController` no longer has player-following light fields or logic.
- Room lights are named like `Lamp_Room*_WallDiagonal`.
- Room light markers are named like `Lamp_Room* WallSconce`.
- Room lights are placed very close to wall bounds and rotated inward diagonally.
- Odd rooms use a vertical wall mount, even rooms use a horizontal wall mount, so they do not all look identical.
- Corridors use `Corridor*_WallLight`, one per corridor.
- Runtime verification result:
  - wall diagonal lights: 5
  - corridor lights: 4
  - player light: false

Preview screenshots created:

- `Assets/Screenshots/mansion_lighting_preview.png`
- `Assets/Screenshots/mansion_lighting_preview_bright.png`
- `Assets/Screenshots/mansion_lighting_diagonal.png`
- `Assets/Screenshots/mansion_lighting_wall_diagonal.png`

## Current Visual Issue To Continue

The latest screenshot shows wall-mounted lights, but the scene was captured during night phase and a large black night mask covers much of the screen. This makes it hard to judge the new wall lights.

Likely next work:

- Inspect `SpriteMansionView` night mask logic.
- The night mask currently behaves like a very strong black overlay.
- It may be fighting the 2D lighting and making the screen too black.
- Convert night darkness into a more lighting-friendly effect:
  - lower alpha,
  - avoid covering lit room interiors completely,
  - or use a semi-transparent darkness overlay that still allows 2D light read.
- Do not reintroduce a player-carried light unless the user explicitly asks.

## Strong Implementation Notes

- Keep visual objects scene-authored when possible.
- If adding or adjusting lights, modify the actual `Lighting` hierarchy in `YuminScene`.
- Do not spawn a fresh lighting setup every Play Mode.
- If a script needs runtime behavior, it should control existing scene objects, not hide the scene composition.
- Avoid adding another `GameController` through `[RequireComponent]`.
- After script edits, refresh/compile and check Unity console.
- After scene edits, save `YuminScene`.

## Verification Checklist

- Unity console has 0 actual errors.
- Play Mode starts.
- There is exactly one `GameController`.
- No `Player Sight Light` exists unless intentionally added back.
- `Lighting/Room Lamps` contains 5 wall-mounted room lights.
- `Lighting/Corridor Fill Lights` contains 4 corridor lights.
- Room lights are near wall edges, not room centers.
- Room lights point diagonally into rooms.
- Player movement still works with WASD.
- Camera still follows player and does not zoom out to full map.
- Same-room chat and speech bubbles still work.

## Known Non-Critical Console Messages

Unity may log:

- `Ignoring depth surface load action as it is memoryless`
- `Ignoring depth surface store action as it is memoryless`

These appear to be URP/rendering informational warnings, not gameplay errors.

## Git / Dirty Files Note

There may be `.DS_Store` changes from macOS/Unity. Ignore those unless the user specifically asks to clean them.
