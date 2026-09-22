# RDBExplorer — Proposed Upstream Improvements

**Status:** Community-modified source based on `MrIkso/RDBExplorer`, provided for maintainer review. This is not an official upstream release. The accompanying V4 source archive contains the latest modified source. Please preserve the original author's credits and license.

## 1. Inline G1T image preview

**Problem:** Inspecting thousands of `.g1t` resources previously required opening a separate G1Tool window for each entry. This made visual searches for UI text textures, including those used in *Rise of the Ronin*, time-consuming.

**Change:** A resizable pane on the right of the main RDBExplorer window previews the selected G1T resource directly from the data retrieved via `root.rdb` and the associated `.fdata` containers. No bulk extraction to disk is needed. Double-clicking an entry still opens the original G1Tool window for detailed inspection and editing. The inline viewer previews mip 0 / layer 0; G1Tool retains its existing mip and layer controls.

## 2. Sequential browsing of resources and internal textures

A **Browse Textures** checkbox changes how previous/next navigation works:

- **Off:** Move between G1T resources and show the first texture (`Texture_000`) in each one.
- **On:** Move through all textures in the current G1T. Continuing past its final texture advances to the first texture of the next G1T; moving backward past its first texture opens the final texture of the previous G1T.
- Single-texture G1T files do not require an extra navigation step. Navigation follows the currently filtered resource list. The preview-pane buttons and supported arrow-key navigation can be used to browse.

## 3. Safer asynchronous preview and rapid navigation

**Reported issue:** Rapidly changing the selected texture in the original G1Tool could trigger an exception such as `Decoded data size mismatch: got 67108864, expected 1048576`. The same textures rendered successfully when users waited for a preview to finish.

**Changes in the modified source:**

- Display `Loading...` inside the preview pane while preparing an image, without intentionally blocking interaction with the resource list.
- Capture the requested texture reference and mip dimensions before background decoding instead of rereading mutable selection state after an `await`.
- Discard stale preview results when the selection has changed, and use a shared `SemaphoreSlim` gate to serialize preview decoding where concurrent work could interfere.
- Validate decoded byte counts against the requested dimensions. Show preview errors within the pane rather than letting a preview exception interrupt browsing, and dispose of unused bitmap objects.
- Reuse the existing G1T parser and texture decoder; no replacement G1T reader is introduced.

**Testing note:** These changes address a plausible race between texture data and image dimensions, rather than merely hiding the exception. They do not by themselves establish that every underlying decoder issue has been eliminated; rapid-navigation testing with varied game assets is still required.

## 4. Main-window layout improvements

The initial inline-preview integration obscured the file-search box and type filter. Subsequent layout changes separate the main menu from the fill-docked split view, keep the search and type-filter controls above the resource list, and place the resizable preview pane on the right. Users can adjust the pane widths with the divider.

## 5. Searchable container picker and container-scoped filtering

- Add a third toolbar control beside **Search Files** and **File Type**. The intended toolbar-width split is **36% / 29% / 35%**, respectively.
- Build a deduplicated, in-memory list of container names from the opened RDB index. Typing any substring narrows the container dropdown; choosing a container displays only the resources belonging to it. **All Containers** restores the full scope.
- Keep the selected container compatible with file-name search and file-type filtering. The main search also matches the Container column.
- Typing into the container picker filters only its dropdown; it does not change the resource list until a container is selected. The dropdown shows up to 1,000 matching names at once, while the complete name set remains available in memory for further narrowing.

## 6. Dark mode

- Enable the dark theme by default on first run, with a **Settings → Dark Mode** toggle.
- Persist the user's theme choice in `%LOCALAPPDATA%\RDBExplorer\theme.txt`.
- Apply shared theme handling to the resource list, search and filter controls, container picker and dropdown, inline G1T preview, menus, G1Tool, and other supported forms.

## Main source files added or modified

| File | Purpose |
|---|---|
| `RDBExplorer/Forms/ExplolerForm.InlinePreview.cs` | Inline preview pane, navigation across resources and textures, asynchronous preview flow. |
| `RDBExplorer/Forms/ExplolerForm.ContainerBrowser.cs` | Searchable container picker, container selection, and toolbar layout. |
| `RDBExplorer/Forms/ExplolerForm.DarkMode.cs` | Theme toggle integration in the main window. |
| `RDBExplorer/Forms/ExplolerForm.cs` | Resource-list events, filtering, and feature integration. |
| `RDBExplorer/Forms/ExplolerForm.Designer.cs` | Main-window resource-list, search, and status-row layout adjustments. |
| `RDBExplorer/Forms/G1ToolForm.cs` | More robust asynchronous preview in the original G1Tool. |
| `RDBExplorer/Utils/TextureConverter.cs` | Shared decoding gate, decoded-size validation, and bitmap handling. |
| `RDBExplorer/Services/ThemeManager.cs` | Theme application and preference persistence. |

## Suggested validation before upstream integration

1. Build the project on Windows with the supported .NET 10 SDK: `dotnet build RDBExplorer.sln`.
2. Open `root.rdb`. Test both single- and multi-texture G1T files, including backward and forward navigation with **Browse Textures** enabled and disabled.
3. Change selections rapidly during `Loading...`. Verify that the displayed image and dimensions always correspond to the latest selection, the resource list remains responsive, and no unhandled exceptions appear. Test the original G1Tool separately.
4. Search container names by substring, choose a container, combine the selection with file-name search and file-type filtering, and test **All Containers**, sorting, and opening another RDB index.
5. Toggle dark and light modes, restart the application, and inspect menus, dialogs, dropdowns, and preview controls in both themes.

**Verification scope:** The user's original project build succeeded, and they visually confirmed the improved preview/layout and container-browser interface on Windows. This handoff does **not** claim a new automated build or a comprehensive regression test of the final V4 source. Maintainer review and testing are recommended before merging these changes upstream.
