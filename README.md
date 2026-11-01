<img src="icon.png" alt="" width="128" align="right">

# Classic Snap

**[Download on Thunderstore](https://thunderstore.io/c/valheim/p/TheShackDev/Classic_Snap/)** · version 1.0.1 · part of [The Shack Pack](https://thunderstore.io/c/valheim/p/TheShackDev/The_Shack_Pack/)

Brings back the **base game's snapping** alongside **[Extra Snap Points Made Easy](https://thunderstore.io/c/valheim/p/Searica/Extra_Snap_Points_Made_Easy/)** (ESP) on Valheim 1.0.

## The problem

Valheim 1.0 lets you choose which point of the piece you're holding snaps: in build mode, **Q / E** step through *Auto → Top → Bottom → Edge…*.

ESP adds many more snap points, which is great for detailed building. But it also:
- turns the base game's Q/E snap choosing off, and
- gives its extra points the same names, so the quick Top/Bottom/Edge choice is gone.

## What it does

It adds a **Classic** mode to ESP's **Toggle Manual Snap Mode** key (CapsLock by default). The key now cycles:

**Classic → Auto → Manual → Classic…**

| Mode | What you get |
|---|---|
| **Classic** | Only the game's original snap points, on the piece you hold and on the pieces you attach to. **Q / E** picks Top / Bottom / Edge like the base game. |
| **Auto** | ESP's extra points, snapping to the closest automatically. |
| **Manual** | ESP's extra points; ESP's keys pick the point. |

- Each switch shows "Snap Mode: …" the same way ESP does.
- ESP's other modes (Manual+, Grid) still work from any mode, and switching to them leaves Classic.
- Your mode is remembered between sessions. A fresh install starts in **Classic**.

## Notes

- **Client-side only.** Building happens on the player's PC, so the server doesn't need it.
- Needs Extra Snap Points Made Easy. Without it this mod doesn't load.
- It doesn't change any ESP settings or files. ESP's extra points are hidden only while Classic is on.
- If an ESP update renames what this mod hooks into, it logs a warning and stays off, rather than breaking building.
- Tested with Extra Snap Points Made Easy 2.1.1, BepInExPack 5.4.2351, Valheim 1.0.17.

## Credits

All credit for the snapping features goes to Iron Gate (Valheim's own snapping) and Searica (Extra Snap Points Made Easy). This mod only switches between them.

## Building from source

You need the [.NET SDK](https://dotnet.microsoft.com/download) (6.0 or newer) and Valheim. The project compiles against your own copies of the game's DLLs and BepInEx; none of them are in this repo.

```bash
dotnet build -c Release \
  -p:ValheimManaged="/path/to/Valheim/valheim_Data/Managed" \
  -p:BepInExCore="/path/to/BepInEx/core"
```

`BepInExCore` is the `BepInEx/core` folder of any BepInEx install or r2modman profile. Without the `-p:` options the project looks for a Linux Steam install and an r2modman profile named "The Shack Pack Test Environment". The DLL lands in `bin/Release/`.

This is the source of version 1.0.1 on Thunderstore.

## License

MIT, see [LICENSE](LICENSE).

*This mod was written with the help of an AI assistant (Claude).*
