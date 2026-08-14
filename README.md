# Lexus WarKey

Lexus WarKey is a small Warcraft III / Dota 1 skill hotkey remapper.

It has two jobs:

- **Warkey**: map each skill cell to the key you want to press and the Warcraft letter the skill currently uses.
- **QuickChat**: configure exactly two one-key chat messages.

There is no inventory binding, mouse binding, profile switching, account system, activation gate, updater, macro editor, scripting, admin flow, anti-cheat bypass, process hiding, or Warcraft file editing.

## Workflow

1. Open Lexus WarKey.
2. Configure the 8 skill cells:
   - top key = the key you press
   - bottom key = the Warcraft skill letter sent to the game
3. Optionally configure QuickChat 1 and QuickChat 2.
4. Start Warcraft / Dota 1.
5. During a game, press **Ctrl + F6** to show the small in-game Warkey window.
6. Pick a skill cell, press your key, then press the Warcraft letter for that skill.
7. Press **Ctrl + F6** again or Esc to hide the window and keep playing.

Settings are saved to:

```text
%LocalAppData%\LexusWarKey\profile.json
```

## Runtime Behavior

- Remapping runs only while Warcraft III is the focused window.
- One physical key sends one Warcraft key.
- Key-down and key-up are both remapped, so Warcraft receives a complete keystroke.
- The hook ignores keys injected by Lexus WarKey, preventing remap loops.
- Held remap keys remember the exact injected target until key-up, preventing stuck keys if mappings change mid-press.
- Enter/Esc are never remapped because they control Warcraft chat, and neither can be
  assigned as a trigger key or a target letter — a cell bound to Enter would look configured
  and never cast.
- While Warcraft chat is open, all remaps and QuickChat actions pass through.
- Closing the main window hides it to the tray so the remapper keeps running.

## Manual Warcraft Checklist

Test this in a real Warcraft/Dota game before release:

1. `Q -> T` casts a skill whose in-game letter is `T`.
2. Key-up is released correctly after holding and releasing the remapped key.
3. Change a skill binding through **Ctrl + F6** while Warcraft is open.
4. Close the Ctrl+F6 window and confirm Warcraft input continues normally.
5. Open chat with Enter and type a bound key; it should type the original letter, not cast.
6. Send/cancel chat with Enter/Esc and confirm remapping resumes.
7. Trigger QuickChat 1 and QuickChat 2.
8. Confirm duplicate source keys are reported in the main window.
9. Restart the app and confirm skill/QuickChat settings persisted.

## Development

```bash
dotnet test tests/LexusWarKey.Tests
dotnet run --project src/LexusWarKey
```
