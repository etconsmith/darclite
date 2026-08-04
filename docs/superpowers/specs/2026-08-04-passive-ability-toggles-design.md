# Passive Ability Toggles — Design

## Problem

The Abilities page currently only supports one kind of equip: drag an ability into one of 10 hotbar slots, cast it with a number key, respect its cooldown (`AbilityLoadout` + `AbilityLoadout.Activated`). There's no way to have an ability that's simply *on* while equipped, with no cast and no cooldown.

The first ability that needs this is enemy health bar visibility. It exists today as "Power Sense 1" — a hotbar-castable ability whose `EnemyHealthUI` listens for `AbilityLoadout.Activated` and flips a `_revealed` bool each time it's cast, toggling a world-space canvas. That's a workaround, not the real mechanic: the player wants a passive, permanently-on-while-equipped toggle instead.

## Goal

Add a second equip mechanism — a 5-slot "Passive" row on the Abilities page, separate from the hotbar. Abilities flagged `isPassive` can only be dropped there (never the hotbar). Equipping turns the effect on immediately; unequipping turns it off. No cast, no cooldown, no key press. State persists across sessions via `PlayerPrefs`.

Power Sense 1 becomes the first (and, for now, only) passive-flagged ability, and is removed from the hotbar-castable system entirely — one mechanism per ability, no overlap.

## Architecture

A new system, `PassiveAbilityLoadout`, sits alongside the existing `AbilityLoadout` rather than extending it. They share a shape (fixed-size slot array, `SetSlot`/`ClearSlot`, a change event) but not code — `AbilityLoadout` is about *casting*, `PassiveAbilityLoadout` is about *equipped state*. Keeping them separate means nothing in the existing cast/cooldown pipeline (`AbilityHotbarHudUI`, the five `*Ability.cs` cast handlers) needs to change at all.

```
AbilityIconUI (isPassive flag)
      |
      | drag/drop
      v
AbilitiesPageUI.HandleDrop  --routes by target slot type-->  AbilityLoadout (hotbar, cast+cooldown)
                                                          \-> PassiveAbilityLoadout (passive, immediate on/off)
                                                                     |
                                                                     v
                                                          PlayerPrefs (Darclite.PassiveSlot.N)
                                                                     |
                                                                     v
                                                          SlotChanged event -> EnemyHealthUI
```

## Components

### `PassiveAbilityLoadout` (new — `Assets/_Project/Scripts/Core/PassiveAbilityLoadout.cs`)

Static class, mirrors `AbilityLoadout`'s public shape:

- `const int SlotCount = 5`
- `GetSlotAbilityName(int index)`
- `SetSlot(int index, Sprite icon, string abilityName)` — writes to PlayerPrefs (`Darclite.PassiveSlot.{index}` = ability name, or key deleted when cleared) and fires `SlotChanged`
- `ClearSlot(int index)`
- `event Action<int, Sprite> SlotChanged` — same shape as `AbilityLoadout.SlotChanged`, so `AbilitiesPageUI` can reuse the same icon-restore pattern
- `IsAbilityActive(string abilityName)` — true if any slot currently holds that name
- Loads persisted state from PlayerPrefs on first access (static initializer), so `IsAbilityActive` is correct even before `AbilitiesPageUI` has run.

### `AbilityIconUI` (`Core/AbilityIconUI.cs`)

- New `[SerializeField] private bool isPassive;` + `public bool IsPassive => isPassive;`
- Set at build time via `BuildAbilityIcon`'s `SerializedObject` wiring, same pattern as every other field.

### `AbilityHotbarSlotUI` (`Core/AbilityHotbarSlotUI.cs`)

- New `[SerializeField] private bool isPassiveSlot;` + `public bool IsPassiveSlot => isPassiveSlot;`
- No drop-handling logic changes here — `OnDrop` still just forwards to `AbilitiesPageUI.HandleDrop`; the type check lives in one place (`HandleDrop`), not duplicated per-slot.

### `AbilitiesPageUI` (`Core/AbilitiesPageUI.cs`)

- New `[SerializeField] private AbilityHotbarSlotUI[] passiveSlots = new AbilityHotbarSlotUI[5];`
- `HandleDrop(icon, targetSlot)`:
  - If `targetSlot != null && icon.IsPassive != targetSlot.IsPassiveSlot`, treat as an invalid drop (same path as `targetSlot == null` today — animate back home). No error, no special messaging.
  - If `targetSlot.IsPassiveSlot`, call `PassiveAbilityLoadout.SetSlot`/`ClearSlot` instead of `AbilityLoadout`.
  - Un-equip path (dragging *out* of a passive slot) mirrors the existing hotbar un-equip path, just against the other loadout.
- `Start()`: after existing default-info-panel setup, walk `GetComponentsInChildren<AbilityIconUI>(true)` looking for any whose `AbilityName` matches a currently-active passive slot (per `PassiveAbilityLoadout`), and snap each into its saved slot visually (reusing the existing `SnapIntoSlot` helper), so the menu's visual state matches the persisted gameplay state after a restart.
- `ReplaceEquippedAbility` (tier-supersede-on-unlock) is left hotbar-only. Power Sense 1 has a single tier today, so this doesn't apply yet.
  > ponytail: passive slots aren't covered by tier-supersede; extend `ReplaceEquippedAbility` to also loop `passiveSlots` if a passive ability ever gets a second tier.

### `EnemyHealthUI` (`Combat/EnemyHealthUI.cs`)

- Removes the `AbilityLoadout.Activated` subscription and the `_revealed = !_revealed` flip in `HandleActivated`.
- `OnEnable`: subscribes to `PassiveAbilityLoadout.SlotChanged`, and immediately sets `_revealed = PassiveAbilityLoadout.IsAbilityActive(AbilityName)`, applying it to `_canvas.enabled` right away (covers both "menu not opened yet this session" and "was equipped last session, PlayerPrefs already loaded").
- `HandleSlotChanged(int, Sprite)`: re-evaluates `IsAbilityActive` and sets `_canvas.enabled` directly (not a flip) whenever any passive slot changes.

### `StatMenuBootstrapper` (editor-only — `Editor/StatMenuBootstrapper.cs`)

- `LiteTrees` tuple type gains a trailing `bool isPassive` field; every existing tier entry gets `false` except Power Sense 1, which gets `true`.
- `BuildAbilityIcon` takes the flag through and sets `AbilityIconUI.isPassive` via `SerializedObject`.
- New `BuildPassiveSlot(parent, x, y, size, backgroundSprite)`: same visual construction as `BuildAbilityHotbarSlot` minus the number label (nothing to press), `isPassiveSlot = true`, indexed 0–4 independent of the hotbar's 0–9.
- `BuildAbilitiesPageContent`: lays out a 5-slot passive row directly below the existing hotbar grid (smaller slot size, e.g. 80px vs. the hotbar's 150px, left-aligned under the same margin), with a small "PASSIVE" header label above it in the same style as the category box headers. The category boxes' `boxTopY` calculation shifts down to account for the new row's height.
- `BuildAbilitiesPageContent` wires the new `passiveSlots` array onto `AbilitiesPageUI` via `SerializedObject`, same pattern as `hotbarSlots`.

## Data Flow

1. Player drags the Power Sense 1 icon from the LITE category box onto passive slot 2.
2. `AbilityIconUI.OnEndDrag` → `AbilitiesPageUI.HandleDrop(icon, slot2)`.
3. `slot2.IsPassiveSlot == true` and `icon.IsPassive == true` → match. `PassiveAbilityLoadout.SetSlot(1, icon.IconSprite, "Power Sense 1")`.
4. `PassiveAbilityLoadout` writes `Darclite.PassiveSlot.1 = "Power Sense 1"` to PlayerPrefs, fires `SlotChanged(1, sprite)`.
5. `EnemyHealthUI.HandleSlotChanged` re-checks `IsAbilityActive("Power Sense 1")` → true → `_canvas.enabled = true`. Health bars appear immediately, no key press.
6. Dragging the icon back out (or to an empty slot) clears slot 1, fires `SlotChanged`, `EnemyHealthUI` turns the canvas back off.
7. On next launch, `PassiveAbilityLoadout`'s static initializer reads `Darclite.PassiveSlot.1` back from PlayerPrefs before anything else runs, so `EnemyHealthUI.OnEnable`'s initial `IsAbilityActive` check is already correct — health bars are visible from the start of the session if they were left equipped. When the Abilities menu is opened, `AbilitiesPageUI.Start()` finds the Power Sense 1 icon and snaps it back into slot 2 visually.

## Edge Cases

- **Empty passive slots**: default off, nothing equipped, no PlayerPrefs key.
- **Type-mismatched drop** (passive icon → hotbar slot, or vice versa): rejected silently, icon animates back to its category box — same visual outcome as dropping on empty page space today.
- **Power Sense 1 removed from hotbar system**: no other file references it by name (`AbilityHotbarHudUI`, the five cast-handler scripts) — confirmed via search — so no other cast-path code needs to change.
- **Menu never opened this session**: gameplay effect is still correct, since `PassiveAbilityLoadout` loads from PlayerPrefs on first static access, independent of any UI being built.

## Testing

Manual, in Play mode (no automated test harness exists for this UI today):

1. Equip Power Sense 1 into a passive slot → enemy health bars appear immediately, no key press, no cooldown.
2. Unequip it → health bars hide.
3. Stop and re-enter Play mode with it equipped → health bars are visible immediately on start, and the icon is back in its saved passive slot when the Abilities menu is opened.
4. Attempt to drag Power Sense 1 onto a regular hotbar slot → rejected, icon returns home. Attempt to drag a hotbar-only ability (e.g. Forceful Strike) onto a passive slot → rejected, icon returns home.
