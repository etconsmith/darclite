# Passive Ability Toggles Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a 5-slot "Passive" row to the Abilities page where drag-equipping an ability turns its effect permanently on (no cast, no cooldown) for as long as it stays equipped, starting with Power Sense 1 (enemy health bar visibility), and make that state survive a restart via PlayerPrefs.

**Architecture:** A new static class, `PassiveAbilityLoadout`, sits alongside the existing `AbilityLoadout` rather than extending it — one tracks what's castable, the other tracks what's equipped/active. `AbilitiesPageUI.HandleDrop` routes a drop to whichever loadout owns the target slot type. `EnemyHealthUI` switches from listening for a hotbar cast event to reading `PassiveAbilityLoadout` directly.

**Tech Stack:** Unity 6000.5.0f1, C#, UGUI. `StatMenuBootstrapper.cs` is an Editor-only script that procedurally generates the Abilities page's GameObjects (via `[MenuItem("Darclite/Stat Menu/Setup Stat Menu UI")]`) — it is not hand-authored scene data, and it destroys + fully rebuilds the `StatMenuCanvas` GameObject tree every time it runs.

## Global Constraints

- No Unity Editor, `dotnet`, or C# compiler is available in this working environment. Every task's verification step is static (re-reading the exact diff, `grep` sanity checks for stale references) rather than a compiler or test run. Final, real verification is a manual Play Mode checklist in Task 7, to be run by whoever next opens this project in the Unity Editor.
- Power Sense 1 is removed from the castable hotbar system entirely — after this plan, it can only be equipped into a passive slot. Confirmed via search: no file other than `EnemyHealthUI.cs` and `StatMenuBootstrapper.cs` references it by name.
- Passive slot count is 5 (`PassiveAbilityLoadout.SlotCount`), matching the approved spec.
- PlayerPrefs key format: `Darclite.PassiveSlot.{index}` (string value = equipped ability name, key deleted when a slot is cleared).
- After any `StatMenuBootstrapper.cs` change (Tasks 6–7), the generated Abilities page does not update until someone re-runs `Darclite > Stat Menu > Setup Stat Menu UI` inside the Unity Editor — that menu item destroys and rebuilds `StatMenuCanvas` from scratch, so re-running it is always safe and requires no manual cleanup first.

---

### Task 1: `PassiveAbilityLoadout` data model

**Files:**
- Create: `Assets/_Project/Scripts/Core/PassiveAbilityLoadout.cs`

**Interfaces:**
- Produces: `PassiveAbilityLoadout.SlotCount` (`const int`, `5`), `GetSlotAbilityName(int index) : string`, `GetSlotIcon(int index) : Sprite`, `SetSlot(int index, Sprite icon, string abilityName) : void`, `ClearSlot(int index) : void`, `IsAbilityActive(string abilityName) : bool`, `event Action<int, Sprite> SlotChanged`.

- [ ] **Step 1: Write the file**

```csharp
using System;
using UnityEngine;

namespace Darclite.Core
{
    // Sibling to AbilityLoadout for abilities that are simply on/off while equipped, with no
    // cast and no cooldown — Power Sense 1 (enemy health bar visibility) is the first one. Kept
    // separate from AbilityLoadout rather than folded into it because the two loadouts mean
    // different things: AbilityLoadout tracks what's castable, this tracks what's currently
    // active. State persists to PlayerPrefs so a passive ability stays on across sessions
    // without the player needing to reopen the Abilities page and re-equip it.
    public static class PassiveAbilityLoadout
    {
        public const int SlotCount = 5;

        private const string PlayerPrefsKeyPrefix = "Darclite.PassiveSlot.";

        private static readonly Sprite[] _slotIcons = new Sprite[SlotCount];
        private static readonly string[] _slotAbilityNames = new string[SlotCount];
        private static bool _loaded;

        public static event Action<int, Sprite> SlotChanged;

        // Lazy rather than a static constructor — guarantees the load happens on first real
        // use regardless of exactly when/how this type is first touched (a static cctor only
        // runs before the first member access too, but an explicit flag makes that guarantee
        // obvious at the call site instead of relying on C#'s cctor timing rules).
        private static void EnsureLoaded()
        {
            if (_loaded)
            {
                return;
            }
            _loaded = true;

            for (int i = 0; i < SlotCount; i++)
            {
                string abilityName = PlayerPrefs.GetString(PlayerPrefsKeyPrefix + i, string.Empty);
                _slotAbilityNames[i] = string.IsNullOrEmpty(abilityName) ? null : abilityName;
            }
        }

        public static string GetSlotAbilityName(int index)
        {
            EnsureLoaded();
            return index >= 0 && index < SlotCount ? _slotAbilityNames[index] : null;
        }

        public static Sprite GetSlotIcon(int index)
        {
            EnsureLoaded();
            return index >= 0 && index < SlotCount ? _slotIcons[index] : null;
        }

        public static void SetSlot(int index, Sprite icon, string abilityName)
        {
            EnsureLoaded();
            if (index < 0 || index >= SlotCount)
            {
                return;
            }

            _slotIcons[index] = icon;
            _slotAbilityNames[index] = abilityName;

            if (string.IsNullOrEmpty(abilityName))
            {
                PlayerPrefs.DeleteKey(PlayerPrefsKeyPrefix + index);
            }
            else
            {
                PlayerPrefs.SetString(PlayerPrefsKeyPrefix + index, abilityName);
            }
            PlayerPrefs.Save();

            SlotChanged?.Invoke(index, icon);
        }

        public static void ClearSlot(int index)
        {
            SetSlot(index, null, null);
        }

        public static bool IsAbilityActive(string abilityName)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(abilityName))
            {
                return false;
            }

            for (int i = 0; i < SlotCount; i++)
            {
                if (_slotAbilityNames[i] == abilityName)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
```

- [ ] **Step 2: Verify**

Run: `grep -c "public static" Assets/_Project/Scripts/Core/PassiveAbilityLoadout.cs`
Expected: `6` (SlotCount is a const not counted by this grep pattern's method check, but `GetSlotAbilityName`, `GetSlotIcon`, `SetSlot`, `ClearSlot`, `IsAbilityActive`, and the `SlotChanged` event declaration each contain `public static` — confirms nothing was dropped while typing the file out).

- [ ] **Step 3: Commit**

```bash
git add Assets/_Project/Scripts/Core/PassiveAbilityLoadout.cs Assets/_Project/Scripts/Core/PassiveAbilityLoadout.cs.meta
git commit -m "Add PassiveAbilityLoadout for permanent no-cooldown ability toggles"
```

Note: Unity auto-generates the `.meta` file the next time the Editor regains focus with this new script present. If the `.meta` file doesn't exist yet when you run this step (i.e. you're committing outside the Editor), commit just the `.cs` file — `git add Assets/_Project/Scripts/Core/PassiveAbilityLoadout.cs` — and add the `.meta` in Task 7's commit once the Editor has generated it.

---

### Task 2: Flag `AbilityIconUI` as passive-capable

**Files:**
- Modify: `Assets/_Project/Scripts/Core/AbilityIconUI.cs:24-41`

**Interfaces:**
- Produces: `AbilityIconUI.IsPassive : bool` (read-only property backed by a new serialized field).

- [ ] **Step 1: Add the field**

In `Assets/_Project/Scripts/Core/AbilityIconUI.cs`, change:

```csharp
        [SerializeField] private string abilityName;
        [TextArea(2, 4)]
        [SerializeField] private string abilityDescription;
        [SerializeField] private string treeTitle;
        [SerializeField] private int cost = 1;
        [SerializeField] private Sprite iconSprite;
```

to:

```csharp
        [SerializeField] private string abilityName;
        [TextArea(2, 4)]
        [SerializeField] private string abilityDescription;
        [SerializeField] private string treeTitle;
        [SerializeField] private int cost = 1;
        [SerializeField] private Sprite iconSprite;
        // True for abilities that can only be equipped into a passive slot (permanent effect
        // while equipped, no cast, no cooldown) rather than the castable hotbar — set by
        // StatMenuBootstrapper.BuildAbilityIcon at generation time.
        [SerializeField] private bool isPassive;
```

- [ ] **Step 2: Add the property**

Change:

```csharp
        public string AbilityName => abilityName;
        public string AbilityDescription => abilityDescription;
        public string TreeTitle => treeTitle;
        public int Cost => cost;
        public Sprite IconSprite => iconSprite;
```

to:

```csharp
        public string AbilityName => abilityName;
        public string AbilityDescription => abilityDescription;
        public string TreeTitle => treeTitle;
        public int Cost => cost;
        public Sprite IconSprite => iconSprite;
        public bool IsPassive => isPassive;
```

- [ ] **Step 3: Verify**

Run: `grep -n "isPassive\|IsPassive" Assets/_Project/Scripts/Core/AbilityIconUI.cs`
Expected: two lines — the `[SerializeField] private bool isPassive;` field and the `public bool IsPassive => isPassive;` property.

- [ ] **Step 4: Commit**

```bash
git add Assets/_Project/Scripts/Core/AbilityIconUI.cs
git commit -m "Add IsPassive flag to AbilityIconUI"
```

---

### Task 3: Flag `AbilityHotbarSlotUI` as passive-only

**Files:**
- Modify: `Assets/_Project/Scripts/Core/AbilityHotbarSlotUI.cs:12-19`

**Interfaces:**
- Produces: `AbilityHotbarSlotUI.IsPassiveSlot : bool` (read-only property backed by a new serialized field).

- [ ] **Step 1: Add the field and property, and generalize the existing slotIndex comment**

In `Assets/_Project/Scripts/Core/AbilityHotbarSlotUI.cs`, change:

```csharp
        [SerializeField] private RectTransform slotRect;
        // 0-based index into AbilityLoadout — lets AbilitiesPageUI report equip/unequip changes
        // against the shared loadout data without needing to search the slot array for "which one".
        [SerializeField] private int slotIndex;

        public RectTransform SlotRect => slotRect;
        public int SlotIndex => slotIndex;
        public AbilityIconUI EquippedIcon { get; private set; }
```

to:

```csharp
        [SerializeField] private RectTransform slotRect;
        // 0-based index into whichever loadout owns this slot (AbilityLoadout for the castable
        // hotbar, PassiveAbilityLoadout for the passive row) — lets AbilitiesPageUI report
        // equip/unequip changes against the shared loadout data without needing to search the
        // slot array for "which one".
        [SerializeField] private int slotIndex;
        // True for a slot in the passive row: equipping here turns an ability permanently on
        // (no cast, no cooldown) instead of making it castable from the hotbar. Only an
        // AbilityIconUI with a matching IsPassive value can be dropped here.
        [SerializeField] private bool isPassiveSlot;

        public RectTransform SlotRect => slotRect;
        public int SlotIndex => slotIndex;
        public bool IsPassiveSlot => isPassiveSlot;
        public AbilityIconUI EquippedIcon { get; private set; }
```

- [ ] **Step 2: Verify**

Run: `grep -n "isPassiveSlot\|IsPassiveSlot" Assets/_Project/Scripts/Core/AbilityHotbarSlotUI.cs`
Expected: two lines — the field and the property.

- [ ] **Step 3: Commit**

```bash
git add Assets/_Project/Scripts/Core/AbilityHotbarSlotUI.cs
git commit -m "Add IsPassiveSlot flag to AbilityHotbarSlotUI"
```

---

### Task 4: Route drag-and-drop through the right loadout

**Files:**
- Modify: `Assets/_Project/Scripts/Core/AbilitiesPageUI.cs:17-134` (fields + `HandleDrop`), plus `Start()` at line 52-55.

**Interfaces:**
- Consumes: `AbilityIconUI.IsPassive` (Task 2), `AbilityHotbarSlotUI.IsPassiveSlot` (Task 3), `PassiveAbilityLoadout.SetSlot/ClearSlot/GetSlotAbilityName` (Task 1).
- Produces: `AbilitiesPageUI.passiveSlots` serialized field (wired by `StatMenuBootstrapper` in Task 7).

- [ ] **Step 1: Add the `passiveSlots` field**

Change:

```csharp
        [SerializeField] private AbilityHotbarSlotUI[] hotbarSlots = new AbilityHotbarSlotUI[10];
        [SerializeField] private AbilityInfoPanelUI infoPanel;
```

to:

```csharp
        [SerializeField] private AbilityHotbarSlotUI[] hotbarSlots = new AbilityHotbarSlotUI[10];
        [SerializeField] private AbilityHotbarSlotUI[] passiveSlots = new AbilityHotbarSlotUI[5];
        [SerializeField] private AbilityInfoPanelUI infoPanel;
```

- [ ] **Step 2: Restore passive icons to their saved slot on page open**

Change:

```csharp
        private void Start()
        {
            infoPanel?.Show(defaultAbilityName, defaultAbilityDescription, defaultTreeTitle, defaultCost, defaultIconSprite);
        }
```

to:

```csharp
        private void Start()
        {
            infoPanel?.Show(defaultAbilityName, defaultAbilityDescription, defaultTreeTitle, defaultCost, defaultIconSprite);
            RestorePassiveIcons();
        }

        // Re-places any passive ability icon into its saved slot when the page opens, so the
        // page's visual state matches PassiveAbilityLoadout's persisted state — which already
        // took effect on whatever it drives (e.g. EnemyHealthUI) before this menu was ever
        // opened this session.
        private void RestorePassiveIcons()
        {
            AbilityIconUI[] icons = GetComponentsInChildren<AbilityIconUI>(true);
            for (int slotIndex = 0; slotIndex < passiveSlots.Length; slotIndex++)
            {
                string abilityName = PassiveAbilityLoadout.GetSlotAbilityName(slotIndex);
                if (string.IsNullOrEmpty(abilityName))
                {
                    continue;
                }

                AbilityHotbarSlotUI slot = passiveSlots[slotIndex];
                if (slot == null)
                {
                    continue;
                }

                foreach (AbilityIconUI icon in icons)
                {
                    if (icon.AbilityName != abilityName)
                    {
                        continue;
                    }

                    slot.SetEquipped(icon);
                    icon.CurrentSlot = slot;
                    SnapIntoSlot(icon, slot);
                    break;
                }
            }
        }
```

- [ ] **Step 3: Route `HandleDrop` through the correct loadout**

Change:

```csharp
        public void HandleDrop(AbilityIconUI icon, AbilityHotbarSlotUI targetSlot)
        {
            if (icon == null)
            {
                return;
            }

            AbilityHotbarSlotUI previousSlot = icon.CurrentSlot;

            if (targetSlot == null)
            {
                if (previousSlot != null)
                {
                    previousSlot.Clear();
                    AbilityLoadout.ClearSlot(previousSlot.SlotIndex);
                    icon.CurrentSlot = null;
                }
                StartCoroutine(AnimateReturnHome(icon));
                return;
            }

            if (targetSlot == previousSlot)
            {
                // Dropped back into the slot it already occupied — settle back into place.
                SnapIntoSlot(icon, targetSlot);
                return;
            }

            AbilityIconUI displaced = targetSlot.EquippedIcon;
            if (displaced != null && displaced != icon)
            {
                displaced.CurrentSlot = null;
                targetSlot.Clear();
                StartCoroutine(AnimateReturnHome(displaced));
            }

            if (previousSlot != null)
            {
                previousSlot.Clear();
                AbilityLoadout.ClearSlot(previousSlot.SlotIndex);
            }

            targetSlot.SetEquipped(icon);
            icon.CurrentSlot = targetSlot;
            AbilityLoadout.SetSlot(targetSlot.SlotIndex, icon.IconSprite, icon.AbilityName);
            SnapIntoSlot(icon, targetSlot);
        }
```

to:

```csharp
        public void HandleDrop(AbilityIconUI icon, AbilityHotbarSlotUI targetSlot)
        {
            if (icon == null)
            {
                return;
            }

            AbilityHotbarSlotUI previousSlot = icon.CurrentSlot;

            // A passive-flagged ability can only occupy a passive slot and vice versa — a
            // mismatched drop is treated exactly like dropping on empty space.
            if (targetSlot != null && icon.IsPassive != targetSlot.IsPassiveSlot)
            {
                targetSlot = null;
            }

            if (targetSlot == null)
            {
                if (previousSlot != null)
                {
                    previousSlot.Clear();
                    ClearLoadoutSlot(previousSlot);
                    icon.CurrentSlot = null;
                }
                StartCoroutine(AnimateReturnHome(icon));
                return;
            }

            if (targetSlot == previousSlot)
            {
                // Dropped back into the slot it already occupied — settle back into place.
                SnapIntoSlot(icon, targetSlot);
                return;
            }

            AbilityIconUI displaced = targetSlot.EquippedIcon;
            if (displaced != null && displaced != icon)
            {
                displaced.CurrentSlot = null;
                targetSlot.Clear();
                StartCoroutine(AnimateReturnHome(displaced));
            }

            if (previousSlot != null)
            {
                previousSlot.Clear();
                ClearLoadoutSlot(previousSlot);
            }

            targetSlot.SetEquipped(icon);
            icon.CurrentSlot = targetSlot;
            SetLoadoutSlot(targetSlot, icon);
            SnapIntoSlot(icon, targetSlot);
        }

        // Routes to whichever loadout owns this slot — AbilityLoadout for the castable hotbar,
        // PassiveAbilityLoadout for the always-on passive row.
        private static void ClearLoadoutSlot(AbilityHotbarSlotUI slot)
        {
            if (slot.IsPassiveSlot)
            {
                PassiveAbilityLoadout.ClearSlot(slot.SlotIndex);
            }
            else
            {
                AbilityLoadout.ClearSlot(slot.SlotIndex);
            }
        }

        private static void SetLoadoutSlot(AbilityHotbarSlotUI slot, AbilityIconUI icon)
        {
            if (slot.IsPassiveSlot)
            {
                PassiveAbilityLoadout.SetSlot(slot.SlotIndex, icon.IconSprite, icon.AbilityName);
            }
            else
            {
                AbilityLoadout.SetSlot(slot.SlotIndex, icon.IconSprite, icon.AbilityName);
            }
        }
```

- [ ] **Step 4: Note the tier-supersede limitation on `ReplaceEquippedAbility`**

This method auto-migrates a hotbar slot to a newly-unlocked tier when an ability chain advances (e.g. Lite Concentration → Lite Concentration II). Power Sense 1 has only one tier today, so this never fires for a passive ability — but the method only loops `hotbarSlots`, so if a passive ability ever gets a second tier, it silently wouldn't migrate. Mark the gap rather than leave it undocumented. Change:

```csharp
        // Called by AbilityTierGateUI when unlocking a new tier supersedes an already-equipped
        // earlier one — moves whichever hotbar slot(s) held the old ability over to the new one's
        // icon automatically, the same way a manual drag-drop would, since the player never
        // dragged anything themselves for this to happen.
        public void ReplaceEquippedAbility(string oldAbilityName, AbilityIconUI newIcon)
        {
            for (int i = 0; i < hotbarSlots.Length; i++)
```

to:

```csharp
        // Called by AbilityTierGateUI when unlocking a new tier supersedes an already-equipped
        // earlier one — moves whichever hotbar slot(s) held the old ability over to the new one's
        // icon automatically, the same way a manual drag-drop would, since the player never
        // dragged anything themselves for this to happen.
        //
        // ponytail: only loops hotbarSlots — passive slots aren't covered because no passive
        // ability has more than one tier yet. Loop passiveSlots here too the day one does.
        public void ReplaceEquippedAbility(string oldAbilityName, AbilityIconUI newIcon)
        {
            for (int i = 0; i < hotbarSlots.Length; i++)
```

- [ ] **Step 5: Verify**

Run: `grep -n "AbilityLoadout\.\|PassiveAbilityLoadout\." Assets/_Project/Scripts/Core/AbilitiesPageUI.cs`
Expected: every remaining direct `AbilityLoadout.ClearSlot`/`AbilityLoadout.SetSlot` call site is gone from `HandleDrop` (they now only appear inside `ClearLoadoutSlot`/`SetLoadoutSlot`), and `PassiveAbilityLoadout.GetSlotAbilityName` appears once in `RestorePassiveIcons`.

- [ ] **Step 6: Commit**

```bash
git add Assets/_Project/Scripts/Core/AbilitiesPageUI.cs
git commit -m "Route Abilities page drag-and-drop through hotbar or passive loadout"
```

---

### Task 5: Switch `EnemyHealthUI` to the passive loadout

**Files:**
- Modify: `Assets/_Project/Scripts/Combat/EnemyHealthUI.cs:9-16,72-95,111-124`

**Interfaces:**
- Consumes: `PassiveAbilityLoadout.SlotChanged`, `PassiveAbilityLoadout.IsAbilityActive(string) : bool` (Task 1).

- [ ] **Step 1: Update the class comment**

Change:

```csharp
    // Same bar visual/feedback as PlayerHealthUI (fill + delayed damage trail + hit punch), but
    // world-space above the enemy's head and hidden by default — each press of the player's Power
    // Sense hotbar slot toggles it on/off, matching the ability's "revealing their health above
    // their heads" description. Health tracking itself keeps running underneath while hidden, so
    // the bar is already showing the correct value whenever it's revealed.
```

to:

```csharp
    // Same bar visual/feedback as PlayerHealthUI (fill + delayed damage trail + hit punch), but
    // world-space above the enemy's head and hidden by default — equipping Power Sense 1 into a
    // passive slot turns it on for as long as it stays equipped (no cast, no cooldown), matching
    // the ability's "revealing their health above their heads" description. Health tracking itself
    // keeps running underneath while hidden, so the bar is already showing the correct value
    // whenever it's revealed.
```

- [ ] **Step 2: Swap the subscription in `OnEnable`/`OnDisable`**

Change:

```csharp
        private void OnEnable()
        {
            if (combatant != null)
            {
                combatant.HealthChanged += UpdateHealth;
            }
            AbilityLoadout.Activated += HandleActivated;
        }
```

to:

```csharp
        private void OnEnable()
        {
            if (combatant != null)
            {
                combatant.HealthChanged += UpdateHealth;
            }
            PassiveAbilityLoadout.SlotChanged += HandleSlotChanged;
            ApplyRevealState();
        }
```

Change:

```csharp
        private void OnDisable()
        {
            if (combatant != null)
            {
                combatant.HealthChanged -= UpdateHealth;
            }
            AbilityLoadout.Activated -= HandleActivated;
        }
```

to:

```csharp
        private void OnDisable()
        {
            if (combatant != null)
            {
                combatant.HealthChanged -= UpdateHealth;
            }
            PassiveAbilityLoadout.SlotChanged -= HandleSlotChanged;
        }
```

- [ ] **Step 3: Replace the toggle-flip handler with a direct state read**

Change:

```csharp
        private void HandleActivated(int slotIndex)
        {
            if (AbilityLoadout.GetAbilityName(slotIndex) != AbilityName)
            {
                return;
            }

            _revealed = !_revealed;
            if (_canvas != null)
            {
                _canvas.enabled = _revealed;
            }
        }
```

to:

```csharp
        private void HandleSlotChanged(int slotIndex, Sprite icon)
        {
            ApplyRevealState();
        }

        private void ApplyRevealState()
        {
            _revealed = PassiveAbilityLoadout.IsAbilityActive(AbilityName);
            if (_canvas != null)
            {
                _canvas.enabled = _revealed;
            }
        }
```

- [ ] **Step 4: Verify**

Run: `grep -n "AbilityLoadout\." Assets/_Project/Scripts/Combat/EnemyHealthUI.cs`
Expected: no output — every reference now goes through `PassiveAbilityLoadout` instead.

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Scripts/Combat/EnemyHealthUI.cs
git commit -m "Make enemy health bars a passive-equip toggle instead of a hotbar cast"
```

---

### Task 6: Flag Power Sense 1 as passive at icon-build time

**Files:**
- Modify: `Assets/_Project/Scripts/Editor/StatMenuBootstrapper.cs:1561-1564` (new const), `:1824-1826`, `:1860-1862`, `:1884`, `:1960-1971`.

**Interfaces:**
- Consumes: `AbilityIconUI.IsPassive` setter path (Task 2 — the `isPassive` `SerializedProperty`).

This deliberately does **not** touch the `LiteTrees`/`LiteTreeBranches` data tables or `BuildTreeChain`/`BuildTreeBranches`/`BuildTreeNode` (the Lite skill-tree page's node builders) — those consume the same tuples but build the read-only skill tree diagram, not draggable `AbilityIconUI`s, so they have no use for an `isPassive` flag. Adding the field to the shared tuple type would have forced signature changes through all three of those methods for no behavioral benefit. Instead, `BuildCategoryBox` (the only place that builds `AbilityIconUI`s) determines the flag locally with the same ability-name-matching convention already used throughout this codebase (e.g. `EnemyHealthUI.AbilityName`, `LiteBracingAbility`'s cast handler).

- [ ] **Step 1: Add the passive-ability constant**

In `Assets/_Project/Scripts/Editor/StatMenuBootstrapper.cs`, change:

```csharp
        private static readonly Color LiteCategoryColor = new Color(0.95f, 0.8f, 0.3f);
        private static readonly Color StrengthCategoryColor = new Color(0.85f, 0.35f, 0.3f);
        private static readonly Color VitalityCategoryColor = new Color(0.4f, 0.85f, 0.45f);
        private static readonly Color DexterityCategoryColor = new Color(0.35f, 0.7f, 0.85f);
```

to:

```csharp
        private static readonly Color LiteCategoryColor = new Color(0.95f, 0.8f, 0.3f);
        private static readonly Color StrengthCategoryColor = new Color(0.85f, 0.35f, 0.3f);
        private static readonly Color VitalityCategoryColor = new Color(0.4f, 0.85f, 0.45f);
        private static readonly Color DexterityCategoryColor = new Color(0.35f, 0.7f, 0.85f);

        // The only ability flagged passive today — equipped into the Passive row instead of the
        // castable hotbar. Matched by name, the same convention EnemyHealthUI and every
        // *Ability.cs cast handler already use to identify their own ability in shared data.
        private const string PassiveAbilityName = "Power Sense 1";
```

- [ ] **Step 2: Extend `BuildAbilityIcon`'s signature and body**

Change:

```csharp
        private static AbilityIconUI BuildAbilityIcon(Transform parent, (string treeTitle, string abilityName, string iconFileName, string description, int cost) ability, float x, float y, float size, Sprite backgroundSprite)
```

to:

```csharp
        private static AbilityIconUI BuildAbilityIcon(Transform parent, (string treeTitle, string abilityName, string iconFileName, string description, int cost, bool isPassive) ability, float x, float y, float size, Sprite backgroundSprite)
```

Change:

```csharp
            nodeSo.FindProperty("cost").intValue = ability.cost;
            nodeSo.FindProperty("iconSprite").objectReferenceValue = iconSprite;
            nodeSo.ApplyModifiedProperties();

            return iconUI;
```

to:

```csharp
            nodeSo.FindProperty("cost").intValue = ability.cost;
            nodeSo.FindProperty("iconSprite").objectReferenceValue = iconSprite;
            nodeSo.FindProperty("isPassive").boolValue = ability.isPassive;
            nodeSo.ApplyModifiedProperties();

            return iconUI;
```

- [ ] **Step 3: Pass the flag from both `BuildCategoryBox` call sites**

Change:

```csharp
                    AbilityIconUI icon = BuildAbilityIcon(boxObject.transform,
                        (tree.treeTitle, tier.abilityName, tier.iconFileName, tier.description, tier.cost),
                        iconX, iconY, iconSize, slotBackground);
```

to:

```csharp
                    AbilityIconUI icon = BuildAbilityIcon(boxObject.transform,
                        (tree.treeTitle, tier.abilityName, tier.iconFileName, tier.description, tier.cost, tier.abilityName == PassiveAbilityName),
                        iconX, iconY, iconSize, slotBackground);
```

Change:

```csharp
                    AbilityIconUI branchIcon = BuildAbilityIcon(boxObject.transform,
                        (tree.treeTitle, branch.abilityName, branch.iconFileName, branch.description, branch.cost),
                        iconX, iconY, iconSize, slotBackground);
```

to:

```csharp
                    AbilityIconUI branchIcon = BuildAbilityIcon(boxObject.transform,
                        (tree.treeTitle, branch.abilityName, branch.iconFileName, branch.description, branch.cost, false),
                        iconX, iconY, iconSize, slotBackground);
```

(Branch abilities — Lite Release, Forceful Strike — are never passive, so this is always `false`.)

- [ ] **Step 4: Verify**

Run: `grep -n "PassiveAbilityName\|ability.isPassive" Assets/_Project/Scripts/Editor/StatMenuBootstrapper.cs`
Expected: three matches — the const declaration, its use in the tiers call site, and `ability.isPassive` inside `BuildAbilityIcon`.

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Scripts/Editor/StatMenuBootstrapper.cs
git commit -m "Flag Power Sense 1 as passive when building its Abilities page icon"
```

---

### Task 7: Build the Passive slot row and wire it up

**Files:**
- Modify: `Assets/_Project/Scripts/Editor/StatMenuBootstrapper.cs` — new `BuildPassiveSlot` method (near `BuildAbilityHotbarSlot`, line 1675), and layout/wiring changes inside `BuildAbilitiesPageContent` (lines 1590-1667).

**Interfaces:**
- Consumes: `AbilityHotbarSlotUI.isPassiveSlot`/`slotIndex`/`slotRect` serialized properties (Task 3), `AbilitiesPageUI.passiveSlots` serialized field (Task 4).

- [ ] **Step 1: Add `BuildPassiveSlot`**

Add this new method directly after `BuildAbilityHotbarSlot` (after its closing brace, before the `BuildCategoryBox` section comment):

```csharp
        // Same visual construction as BuildAbilityHotbarSlot minus the number badge — nothing to
        // press for a passive slot, equipping alone turns the ability on. x/y are the slot's
        // top-left corner, index is 0-based into PassiveAbilityLoadout.
        private static AbilityHotbarSlotUI BuildPassiveSlot(Transform parent, int index, float x, float y, float size, Sprite backgroundSprite)
        {
            GameObject slotObject = new GameObject($"PassiveSlot{index + 1}", typeof(RectTransform));
            slotObject.transform.SetParent(parent, false);
            RectTransform slotRect = slotObject.GetComponent<RectTransform>();
            slotRect.anchorMin = new Vector2(0f, 1f);
            slotRect.anchorMax = new Vector2(0f, 1f);
            slotRect.pivot = new Vector2(0f, 1f);
            slotRect.anchoredPosition = new Vector2(x, y);
            slotRect.sizeDelta = new Vector2(size, size);

            GameObject borderObject = new GameObject("Border", typeof(Image));
            borderObject.transform.SetParent(slotObject.transform, false);
            SceneBootstrapper.StretchRect(borderObject.GetComponent<RectTransform>());
            Image borderImage = borderObject.GetComponent<Image>();
            borderImage.sprite = SceneBootstrapper.CreateRoundedRectSprite();
            borderImage.type = Image.Type.Sliced;
            borderImage.color = new Color(0.45f, 0.6f, 0.7f, 0.55f);
            borderImage.raycastTarget = false;

            GameObject backgroundObject = new GameObject("Background", typeof(Image));
            backgroundObject.transform.SetParent(slotObject.transform, false);
            SceneBootstrapper.InsetRect(backgroundObject.GetComponent<RectTransform>(), 5f);
            Image backgroundImage = backgroundObject.GetComponent<Image>();
            backgroundImage.sprite = backgroundSprite;
            backgroundImage.type = Image.Type.Sliced;
            backgroundImage.raycastTarget = true;

            AbilityHotbarSlotUI slotUI = slotObject.AddComponent<AbilityHotbarSlotUI>();
            SerializedObject slotSo = new SerializedObject(slotUI);
            slotSo.FindProperty("slotRect").objectReferenceValue = slotRect;
            slotSo.FindProperty("slotIndex").intValue = index;
            slotSo.FindProperty("isPassiveSlot").boolValue = true;
            slotSo.ApplyModifiedProperties();

            return slotUI;
        }
```

- [ ] **Step 2: Lay out the passive row and shift the category boxes down**

In `BuildAbilitiesPageContent`, change:

```csharp
            AbilityHotbarSlotUI[] hotbarSlots = new AbilityHotbarSlotUI[hotbarColumns * hotbarRows];
            for (int row = 0; row < hotbarRows; row++)
            {
                for (int col = 0; col < hotbarColumns; col++)
                {
                    int index = row * hotbarColumns + col;
                    float x = hotbarLeftMargin + col * (hotbarSlotSize + hotbarGapX);
                    float cellTopY = hotbarY - row * (hotbarCellHeight + hotbarGapY);
                    hotbarSlots[index] = BuildAbilityHotbarSlot(content.transform, index + 1, x, cellTopY, hotbarSlotSize, slotBackground);
                }
            }
```

to:

```csharp
            AbilityHotbarSlotUI[] hotbarSlots = new AbilityHotbarSlotUI[hotbarColumns * hotbarRows];
            for (int row = 0; row < hotbarRows; row++)
            {
                for (int col = 0; col < hotbarColumns; col++)
                {
                    int index = row * hotbarColumns + col;
                    float x = hotbarLeftMargin + col * (hotbarSlotSize + hotbarGapX);
                    float cellTopY = hotbarY - row * (hotbarCellHeight + hotbarGapY);
                    hotbarSlots[index] = BuildAbilityHotbarSlot(content.transform, index + 1, x, cellTopY, hotbarSlotSize, slotBackground);
                }
            }

            // A second, smaller row below the castable hotbar — equipping here turns an ability
            // permanently on instead of making it castable, so it gets a plain "PASSIVE" label
            // instead of per-slot number badges (there's no key to press).
            const int passiveSlotCount = 5;
            const float passiveSlotSize = 80f;
            const float passiveSlotGap = 16f;
            const float passiveRowGapFromHotbar = 40f;
            const float passiveHeaderHeight = 24f;
            const float passiveHeaderGap = 6f;

            float hotbarBottom = hotbarY - hotbarTotalHeight;
            float passiveHeaderY = hotbarBottom - passiveRowGapFromHotbar;
            float passiveSlotsY = passiveHeaderY - passiveHeaderHeight - passiveHeaderGap;

            GameObject passiveHeaderObject = new GameObject("PassiveHeader", typeof(Text));
            passiveHeaderObject.transform.SetParent(content.transform, false);
            RectTransform passiveHeaderRect = passiveHeaderObject.GetComponent<RectTransform>();
            passiveHeaderRect.anchorMin = new Vector2(0f, 1f);
            passiveHeaderRect.anchorMax = new Vector2(0f, 1f);
            passiveHeaderRect.pivot = new Vector2(0f, 1f);
            passiveHeaderRect.anchoredPosition = new Vector2(hotbarLeftMargin, passiveHeaderY);
            passiveHeaderRect.sizeDelta = new Vector2(300f, passiveHeaderHeight);
            Text passiveHeaderText = passiveHeaderObject.GetComponent<Text>();
            passiveHeaderText.font = SceneBootstrapper.GetGameFont();
            passiveHeaderText.fontSize = 16;
            passiveHeaderText.fontStyle = FontStyle.Bold;
            passiveHeaderText.color = new Color(1f, 1f, 1f, 0.8f);
            passiveHeaderText.alignment = TextAnchor.MiddleLeft;
            passiveHeaderText.text = "PASSIVE";
            passiveHeaderText.raycastTarget = false;
            AddGlow(passiveHeaderObject, new Color(0.6f, 0.85f, 1f), 0.6f, 0.6f);

            AbilityHotbarSlotUI[] passiveSlots = new AbilityHotbarSlotUI[passiveSlotCount];
            for (int i = 0; i < passiveSlotCount; i++)
            {
                float x = hotbarLeftMargin + i * (passiveSlotSize + passiveSlotGap);
                passiveSlots[i] = BuildPassiveSlot(content.transform, i, x, passiveSlotsY, passiveSlotSize, slotBackground);
            }

            float passiveRowBottom = passiveSlotsY - passiveSlotSize;
```

- [ ] **Step 3: Point the category-box layout at the bottom of the passive row instead of the hotbar**

Change:

```csharp
            float hotbarBottom = hotbarY - hotbarTotalHeight;
            float infoPanelBottom = infoPanelY - infoPanelHeight;
            float boxTopY = Mathf.Min(hotbarBottom, infoPanelBottom) - 40f;
```

to:

```csharp
            float infoPanelBottom = infoPanelY - infoPanelHeight;
            float boxTopY = Mathf.Min(passiveRowBottom, infoPanelBottom) - 40f;
```

(The `hotbarBottom` local now only exists inside Step 2's block above, where it's used to position the passive row — this removes the old, now-duplicate declaration further down and reuses `passiveRowBottom`, which already accounts for the hotbar + gap + header + slots.)

- [ ] **Step 4: Wire `passiveSlots` onto `AbilitiesPageUI`**

Change:

```csharp
            AbilitiesPageUI pageUI = content.AddComponent<AbilitiesPageUI>();
            SerializedObject pageSo = new SerializedObject(pageUI);
            SerializedProperty slotsProp = pageSo.FindProperty("hotbarSlots");
            slotsProp.arraySize = hotbarSlots.Length;
            for (int i = 0; i < hotbarSlots.Length; i++)
            {
                slotsProp.GetArrayElementAtIndex(i).objectReferenceValue = hotbarSlots[i];
            }
            pageSo.FindProperty("infoPanel").objectReferenceValue = infoPanel;
            pageSo.FindProperty("dragLayer").objectReferenceValue = dragLayerRect;
```

to:

```csharp
            AbilitiesPageUI pageUI = content.AddComponent<AbilitiesPageUI>();
            SerializedObject pageSo = new SerializedObject(pageUI);
            SerializedProperty slotsProp = pageSo.FindProperty("hotbarSlots");
            slotsProp.arraySize = hotbarSlots.Length;
            for (int i = 0; i < hotbarSlots.Length; i++)
            {
                slotsProp.GetArrayElementAtIndex(i).objectReferenceValue = hotbarSlots[i];
            }
            SerializedProperty passiveSlotsProp = pageSo.FindProperty("passiveSlots");
            passiveSlotsProp.arraySize = passiveSlots.Length;
            for (int i = 0; i < passiveSlots.Length; i++)
            {
                passiveSlotsProp.GetArrayElementAtIndex(i).objectReferenceValue = passiveSlots[i];
            }
            pageSo.FindProperty("infoPanel").objectReferenceValue = infoPanel;
            pageSo.FindProperty("dragLayer").objectReferenceValue = dragLayerRect;
```

- [ ] **Step 5: Verify**

Run: `grep -n "BuildPassiveSlot\|passiveSlots\|passiveRowBottom" Assets/_Project/Scripts/Editor/StatMenuBootstrapper.cs`
Expected: the new method definition, its call inside the `for (int i = 0; i < passiveSlotCount; i++)` loop, the `passiveSlots` array declaration/loop/wiring, and `passiveRowBottom` used both where it's computed and in the `boxTopY` line.

Run: `grep -n "float hotbarBottom" Assets/_Project/Scripts/Editor/StatMenuBootstrapper.cs`
Expected: exactly one match (inside the Step 2 block) — confirms the old duplicate declaration from Step 3's diff was actually removed, not left behind as dead code.

- [ ] **Step 6: Commit**

```bash
git add Assets/_Project/Scripts/Editor/StatMenuBootstrapper.cs
git commit -m "Build the Passive slot row on the Abilities page"
```

- [ ] **Step 7: Regenerate the Abilities page and manually verify in Play Mode**

This step requires the Unity Editor and cannot be done from this environment — run it the next time this project is open in the Editor:

1. Open the project in Unity, open the scene containing `StatMenuCanvas` (or any scene — the menu item works regardless of what's currently in the hierarchy).
2. Run `Darclite > Stat Menu > Setup Stat Menu UI`. Confirm no console errors and that a `StatMenuCanvas` exists with an `AbilitiesPageContent` child containing both the existing 2×5 hotbar grid and a new 5-slot "PASSIVE" row beneath it.
3. Enter Play Mode. Open the Abilities page. Confirm Power Sense 1's icon still appears in the LITE category box, and dragging it onto a regular hotbar slot (1–10) does **not** work — it should animate back to the category box instead of equipping.
4. Drag Power Sense 1's icon onto a passive slot. Confirm enemy health bars become visible immediately, with no key press and no cooldown indicator.
5. Drag the icon back out of the passive slot (onto empty page space). Confirm enemy health bars hide immediately.
6. Re-equip it into a passive slot, then stop and re-enter Play Mode. Confirm enemy health bars are visible immediately on start (before the Abilities page is ever opened this session), and that opening the Abilities page shows the icon already sitting in its saved passive slot.
7. Attempt to drag a hotbar-only ability (e.g. Forceful Strike, from the Attack tree) onto a passive slot. Confirm it's rejected — the icon animates back to its category box.

If any of steps 3–7 fail, treat it as a bug against the specific task above whose diff covers that behavior (drag validation → Task 4; enemy health bar reactivity → Task 5; icon flag → Task 6; slot row/layout → Task 7) rather than re-deriving the fix from scratch.
