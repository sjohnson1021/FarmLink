# Stardew Valley Modding: Project Guidelines & Standards

## 1. Project Philosophy
**Goal**: Create seamless, "Vanilla+" experiences for Stardew Valley 1.6+ (SMAPI 4+).
**Core Tenet:** Functionality > Aesthetics, but Readability > Brevity.
**Risk Tolerance:** Low. Mods often hook into critical game loops (Update/Draw) or network layers. If we break, we crash the game or disconnect players. Harmony patches must be defensive, fail gracefully, and never corrupt save data.

## 2. Code Organization & Flow
To ensure logical flow, all files must adhere to the following member ordering. This allows a fresh reader to understand state before behavior.

### 2.1 Standard File Layout
1. **Dependencies**: `using` statements (cleaned and sorted).
2. **Namespace**: `[ModName]` (or semantic sub-namespaces like `.Patches` or `.UI`).
3. **Class Definition**:
    - **Constants**: `const` and `static readonly`. Magic numbers belong here.
    - **State**: Private fields `_camelCase`.
    - **Configuration**: Public properties or references to `ModConfig`.
    - **Lifecycle**: Constructors, `Entry`, or `Initialize` methods.
    - **Core Logic**: The primary public methods driving the class.
    - **Event Handlers**: Methods subscribed to SMAPI events (e.g., `OnButtonPressed`, `OnUpdateTicked`).
    - **Helpers**: Private utility methods, extracted to reduce complexity.

### 2.2 Region Usage
Use `#region` directives to group semantic sections, but do not use them to hide bad code.
- ✅ **Good**: `#region Harmony Patches`, `#region Network Handling`, `#region GMCM Integration`
- ❌ **Bad**: `#region messy_logic_i_ignore`

## 3. C# Styling & Refactoring Rules
- **Version**: C# 10 / .NET 6.
- **Var vs. Explicit**: Use `var` when the type is obvious (e.g., `var list = new List<string>()`). Use explicit types when the result is ambiguous (e.g., `int count = GetCalculation()`).
- **Null Safety**: Use nullable reference types. Use `?` and `??` operators freely.
- **String Interpolation**: Prefer `$"..."` over `string.Format`.
- **Switch Expressions**: Prefer modern `x switch { ... }` syntax over switch-case statements.

### 3.1 Refactoring Directives
- **Extraction**: If a method requires scrolling, extract inner logic into a private helper with a descriptive name.
- **Logic Inversion**: Avoid deep nesting. Use "Guard Clauses" to return early.
    - *Before:* `if (Context.IsWorldReady) { if (config.Enabled) { RunLogic(); } }`
    - *After:* `if (!Context.IsWorldReady || !config.Enabled) return; RunLogic();`
- **Magic Numbers**: Do not use raw numbers for UI coordinates, network ports, or timers. Define them as `const` at the top of the class.

## 4. Harmony Patching Guidelines
- **Prefix vs. Postfix**:
    - **Prefix**: Block execution, modify arguments, or run logic *before* state updates.
    - **Postfix**: Read results, modify return values, or render overlays (GUI).
- **Transpilers**: Avoid unless absolutely necessary. They are brittle and break easily with game updates.
- **Safety**: Wrap patch logic in `try/catch` blocks. If a patch fails, log a discrete Error once, then degrade gracefully (e.g., disable the feature rather than crashing).
- **Performance**: Do not instantiate `new` objects (Textures, Fonts, Packets) inside patches that run every frame/tick. Cache them.

## 5. Domain-Specific Logic (General SV Modding)

### 5.1 Networking & Multiplayer
- **Packets**: When sending custom data, verify `Context.IsMainPlayer` or `Context.IsMultiplayer` before transmission.
- **Serialization**: Keep payloads small. Use efficient types.
- **Discovery**: When scanning or broadcasting (e.g., LAN), ensure operations are async or non-blocking to prevent freezing the main game loop.

### 5.2 UI & Rendering
- **Scaling**: Always respect `Game1.options.uiScale`.
- **Texture Handling**: Load textures once in `Entry` or lazily. Dispose of them if they are dynamically generated.
- **GMCM**: Use Generic Mod Config Menu for all user-facing settings. Do not rely solely on `config.json` edits.

## 6. Testing Strategy (Manual)
Since automated tests are difficult in this environment, every change requires this manual checklist:

1. **The "Load" Test**: Does the game load without red text in SMAPI?
2. **The "Save/Load" Test**: Does the mod persist state correctly across days/sessions?
3. **The "Conflict" Test**: Does this work alongside other major mods (e.g., SpaceCore)?
4. **The "Fail" Test**: If the feature fails (e.g., network timeout), does the game continue running smoothly?