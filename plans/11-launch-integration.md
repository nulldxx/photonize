# Slice 11 — Launch integration

**Depends on:** 01 (bookmarks), 05 (for the WebP export entry point). **Blocks:** nothing.

## Context

On Windows, most people never open Photonize from the Start Menu. They right-click a folder in
Explorer and choose **Open in Photonize**, or right-click images and choose **Export to WebP**.
`Photonize.Installer/Product.wxs` writes four registry verbs behind an optional
"Windows Explorer Integration" feature:

| Key | Verb | Command |
|---|---|---|
| `HKCR\Directory\shell\Photonize` | Open in Photonize | `Photonize.exe -d "%1"` |
| `HKCR\Directory\Background\shell\Photonize` | Open in Photonize | `Photonize.exe -d "%V"` |
| `HKCR\SystemFileAssociations\image\shell\PhotonizeExportWebP` | Export to WebP | `Photonize.exe -e "%1"` |

`Photonize/App.xaml.cs` parses `-d/--directory` and `-e/--export-webp`, and a `-d` path
deliberately suppresses the saved-directory restore while still loading the other settings
(`LoadSavedSettings(skipDirectoryPath: true)`).

macOS has none of this. `Info.plist` already declares `CFBundleDocumentTypes` for `public.folder`
and `public.image` (Viewer role) — but nothing implements opening them, so `open -a Photonize
~/Pictures` launches the app and ignores the argument, and dragging a folder onto the Dock icon
does nothing.

**Outcome:** `open -a Photonize <folder>`, dragging a folder onto the Dock icon, and a Finder
right-click all get you into the right folder; images can be exported to WebP straight from
Finder.

---

## Design

### Command line

Parse in `AppDelegate.applicationWillFinishLaunching` — before the first window appears, so the
view model starts on the right directory rather than loading the saved one and then jumping.

```
Photonize --directory <path>    (-d)
Photonize --export-webp <file…> (-e)
```

Match the Windows semantics precisely:

- `-d` suppresses restoring `lastDirectoryPath` but still loads thumbnail size, prefix and pane
  state.
- `-e` opens the containing directory, immediately exports the named files to WebP using the
  slice-05 exporter, then navigates to the output folder (Windows `49a28d2`).
- Unknown arguments are ignored, not fatal. Xcode injects `-NSDocumentRevisionsDebugMode YES` and
  similar into debug runs, so a strict parser would refuse to launch under the debugger.

Use plain `CommandLine.arguments`; ArgumentParser would be a second SPM dependency for twenty lines
of parsing.

### `open` and the Dock

Implement the AppKit entry points the declared document types imply:

```swift
func application(_ sender: NSApplication, open urls: [URL])
```

- A single folder → navigate there.
- Image files → treat as a selection in their containing folder (navigate to the directory and
  select them), which is the sane reading of "open these images in a photo organiser".
- A mix, or several folders → take the first folder.

This one method covers `open -a Photonize <path>`, dragging onto the Dock icon, dragging onto the
app in Finder, and "Open With" — all of which route through the same delegate call.

**Sandbox:** URLs arriving this way come with a transient grant via Powerbox. Immediately hand them
to slice 01's `DirectoryAccess.makeBookmark` and persist, so the folder still opens on the next
launch. Without this the folder works once and then silently fails.

### Finder integration — `NSServices`, not Automator

The closest macOS analogue to the Explorer registry verbs is a **Services** entry declared in
`Info.plist`. It needs no separate installer step, no user-visible Automator workflow file, and it
appears in Finder's right-click Services submenu automatically once the app has been launched once.

```xml
<key>NSServices</key>
<array>
  <dict>
    <key>NSMenuItem</key><dict><key>default</key><string>Open in Photonize</string></dict>
    <key>NSMessage</key><string>openFolderInPhotonize</string>
    <key>NSPortName</key><string>Photonize</string>
    <key>NSSendFileTypes</key><array><string>public.folder</string></array>
  </dict>
  <dict>
    <key>NSMenuItem</key><dict><key>default</key><string>Export to WebP</string></dict>
    <key>NSMessage</key><string>exportToWebP</string>
    <key>NSPortName</key><string>Photonize</string>
    <key>NSSendFileTypes</key><array><string>public.image</string></array>
  </dict>
</array>
```

with matching handlers registered via `NSApp.servicesProvider`:

```swift
@objc func openFolderInPhotonize(_ pboard: NSPasteboard, userData: String, error: AutoreleasingUnsafeMutablePointer<NSString>)
@objc func exportToWebP(_ pboard: NSPasteboard, userData: String, error: AutoreleasingUnsafeMutablePointer<NSString>)
```

Two things worth knowing and documenting in `macos/README.md`:

- Services are registered by Launch Services when the app is first seen in `/Applications`. Users
  may need to log out and back in, or run `/System/Library/CoreServices/pbs -flush`, before the
  entries appear. Note it in the troubleshooting section.
- Services entries are off by default in some macOS versions and may need enabling in
  System Settings → Keyboard → Keyboard Shortcuts → Services.

**One genuine improvement over Windows:** the `Export to WebP` verb on Windows opens one instance
of the app *per selected file* (a known wart recorded in the changelog). The pasteboard hands
macOS all selected files in a single call, so multi-select export works properly here. Say so in
the README.

### Multi-selection and single-instance

`WindowGroup` will happily open a second window when a second `open` arrives, and every window
shares the same `@StateObject` view model (`PhotonizeApp.swift:6,11`), so a second window would
mirror the first — confusing. Route `application(_:open:)` and both service handlers into the
existing window: navigate it and bring it to the front, rather than opening a new one.

---

## Files

**Modified**
- `macos/Photonize/PhotonizeApp.swift` — `AppDelegate` gains `applicationWillFinishLaunching`
  (argument parsing), `application(_:open:)`, the two `@objc` service handlers, and
  `NSApp.servicesProvider` registration
- `macos/Photonize/Info.plist` — `NSServices` array
- `macos/Photonize/ViewModels/PhotoGridViewModel.swift` — `openDirectory(_:selecting:)` entry
  point that bookmarks, loads, and optionally pre-selects; `skipDirectoryRestore` flag
- `macos/README.md` — command line usage, Services setup and the `pbs -flush` note

---

## Tests

Little is unit-testable here — it is all AppKit lifecycle. What is worth pinning:

- Argument parsing: `["-d", "/tmp/x"]`, `["--directory", "/tmp/x"]`, `["-e", "a.jpg", "b.jpg"]`,
  `[]`, and a run polluted with `-NSDocumentRevisionsDebugMode YES` all parse to the expected
  intent without throwing.
- A `-d` path sets `skipDirectoryRestore` while leaving the other settings loaded.

## Verification

1. **Command line.** `open -a Photonize --args -d ~/Pictures/holiday` → the app launches on that
   folder, not the last-used one, but with the thumbnail size and pane state you left.
2. **Direct open.** `open -a Photonize ~/Pictures/holiday` → same result via
   `application(_:open:)`.
3. **Dock drag.** Drag a folder onto the Dock icon with the app running, and again with it quit.
   Both navigate there.
4. **Bookmark persistence.** Open a folder via `open`, quit, relaunch with no arguments — it comes
   back to that folder. Without the bookmark step it will not.
5. **Open With.** Right-click a folder → Open With → Photonize.
6. **Services.** Install to `/Applications`, launch once, then right-click a folder in Finder →
   Services → Open in Photonize. If absent, run `pbs -flush` and retry — then confirm the README's
   troubleshooting note matches what actually happened.
7. **Multi-file export.** Select eight images in Finder → Services → Export to WebP. **One** app
   instance opens and all eight are exported, unlike the Windows behaviour.
8. **Existing window reuse.** With a folder already open, `open -a Photonize <other folder>` →
   the same window navigates and comes forward; no second window.
9. **Debugger.** Run from Xcode with the default scheme arguments — no crash, no spurious folder.

---

## Out of scope

- A Finder extension (`FIFinderSync`) with a custom toolbar button. Heavier than the value; needs
  an app extension target, its own entitlements, and its own signing.
- A Shortcuts.app action or App Intents. A reasonable future addition, but `NSServices` covers the
  Explorer-verb parity this slice is about.
- URL schemes (`photonize://`).
- Registering as the default handler for image types — Photonize is an organiser, not a viewer;
  hijacking double-click on JPEGs would be wrong.
- Universal build and release packaging — slice 12.
