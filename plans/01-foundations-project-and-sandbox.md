# Slice 01 — Foundations: project file, sandbox, settings

**Depends on:** nothing. **Blocks:** everything.

## Context

Three things in the macOS app will break or obstruct every slice that follows, and none of them
are features. They need doing first.

**1. The Xcode project is hand-written.**
`macos/Photonize.xcodeproj/project.pbxproj` was authored by hand with sequential fake UUIDs
(`200000000000000000000001` … `0009` for file references, `1000…` for build files). Adding one
Swift file means three coordinated edits in three sections. Across the remaining eleven slices
that is roughly twenty new source files, one SPM package reference (slice 05), one new framework
(slice 08) and a whole test target (slice 12). `macos/create-xcode-project.sh` does not help — it
is a self-described "placeholder script" that runs `mkdir -p` and prints instructions.

**2. Restoring the last directory cannot work under the sandbox.**
`Photonize.entitlements` grants `com.apple.security.app-sandbox` and
`com.apple.security.files.user-selected.read-write`. `PhotoGridViewModel.loadSettings`
(`macos/Photonize/ViewModels/PhotoGridViewModel.swift:381`) reconstructs `lastDirectoryPath` as a
raw path string on launch. The `NSOpenPanel` grant that originally allowed access does not survive
relaunch, so on a properly sandboxed build the app comes back to a folder it cannot read. There
are no security-scoped bookmarks anywhere in the codebase.

**3. Settings save at the wrong times.**
`saveSettings()` is called from exactly two places: the end of `loadPhotos()`
(`PhotoGridViewModel.swift:126`) and after a successful `applyRename()` (line 181). Moving the
thumbnail slider or toggling the preview pane persists nothing until you happen to reload a folder.

Plus two small correctness items worth fixing while we are in here: `PhotonizeApp.swift:16` uses
`CommandMenu("File")`, which *adds* a second File menu alongside AppKit's built-in one; and
`Info.plist` declares `CFBundleIconFile = AppIcon` with no icon asset anywhere in the repo.

**Outcome:** adding a source file, a dependency or a settings key becomes a one-line change; the
app reopens the folder you left it on; and the Settings scene exists as the home for every
preference the later slices add.

---

## Design

### Project generation — adopt XcodeGen

Replace the hand-maintained `project.pbxproj` with `macos/project.yml` and generate on demand.

```yaml
name: Photonize
options:
  bundleIdPrefix: com.photonize
  deploymentTarget:
    macOS: "13.0"
  createIntermediateGroups: true
settings:
  base:
    MARKETING_VERSION: 0.1.0
    CURRENT_PROJECT_VERSION: 1
    ARCHS: $(ARCHS_STANDARD)
    SWIFT_VERSION: "5.0"
    ENABLE_HARDENED_RUNTIME: YES
targets:
  Photonize:
    type: application
    platform: macOS
    sources: [Photonize]
    settings:
      base:
        PRODUCT_BUNDLE_IDENTIFIER: com.photonize.macos
        INFOPLIST_FILE: Photonize/Info.plist
        CODE_SIGN_ENTITLEMENTS: Photonize/Photonize.entitlements
        GENERATE_INFOPLIST_FILE: NO
```

`sources: [Photonize]` means every later slice adds files by *creating them* — no project surgery
at all. Slice 05's SPM dependency becomes a `packages:` block; slice 12's test target becomes a
second entry under `targets:`.

Commit the generated `.xcodeproj` so `open macos/Photonize.xcodeproj` still works for anyone
without XcodeGen installed, and regenerate whenever `project.yml` changes.

**CI change.** `.github/workflows/build-macos.yml:39` currently `sed`s `MARKETING_VERSION` into
the pbxproj. That becomes:

```yaml
- run: brew install xcodegen
- run: |
    cd macos
    sed -i '' "s/MARKETING_VERSION: 0.1.0/MARKETING_VERSION: ${VERSION}/" project.yml
    xcodegen generate
```

**Fallback if adding a build tool is unwanted.** Keep the hand-written pbxproj, but write
`macos/add-source-file.sh <relative/path.swift>` that appends the `PBXBuildFile`,
`PBXFileReference`, group child and `Sources` build-phase entries using the next free ID in the
documented `2000…000N` sequence, and record the scheme in `macos/SETUP.md`. This is strictly worse
for slice 05 (SPM package references need `XCRemoteSwiftPackageReference`,
`XCSwiftPackageProductDependency`, a `packageReferences` array on the `PBXProject` and
`packageProductDependencies` on the target) but it avoids a new dependency on the build machine.

Either way, **delete `macos/create-xcode-project.sh`** — it does nothing and misleads.

### Security-scoped bookmarks

New `macos/Photonize/Services/DirectoryAccess.swift`:

```swift
final class DirectoryAccess {
    /// Create an app-scoped bookmark for a folder the user picked in NSOpenPanel.
    func makeBookmark(for url: URL) -> Data?

    /// Resolve a stored bookmark, starting security-scoped access.
    /// Returns nil if the bookmark is stale or the folder is gone.
    func resolve(_ bookmark: Data) -> URL?

    /// Balance the startAccessing… call. Safe to call with a URL we never scoped.
    func relinquish(_ url: URL)
}
```

- `makeBookmark` uses `url.bookmarkData(options: [.withSecurityScope], includingResourceValuesForKeys: nil, relativeTo: nil)`.
- `resolve` uses `URL(resolvingBookmarkData:options: [.withSecurityScope], ...)` then
  `startAccessingSecurityScopedResource()`; on `isStale` it re-derives and hands the caller a
  fresh bookmark to store.
- Exactly one folder is scoped at a time. `PhotoGridViewModel` relinquishes the previous folder
  before scoping a new one, including when navigating into a subfolder — child folders are covered
  by the parent's grant, so only the root the user picked needs bookmarking.

Entitlement to add: **`com.apple.security.files.bookmarks.app-scope`**. Leave
`com.apple.security.network.client` out for now; it is only needed if the deferred ComfyUI
auto-caption feature is picked up (see `plans/README.md`).

`AppSettings.lastDirectoryPath: String` gains a sibling `lastDirectoryBookmark: Data?`. Keep the
path string — it is what the UI displays and what non-sandboxed debug builds fall back to.

### Settings correctness and the Settings scene

- `AppSettings` gains `isTextEditorVisible: Bool` (Windows has six keys to our four; slice 09
  consumes this one) and `lastDirectoryBookmark: Data?`.
- Because `AppSettings` is `Codable` and stored as one blob under the `PhotonizeSettings`
  `UserDefaults` key, adding optional fields is backwards-compatible — but give every new field a
  default so an old blob still decodes.
- Replace the two ad-hoc `saveSettings()` calls with a debounced save driven from the view model's
  `didSet`s (`thumbnailSize`, `isPreviewVisible`, `isTextEditorVisible`, `renamePrefix`,
  `directoryPath`). ~500 ms of quiet is enough; the slider fires continuously while dragged.
- New `macos/Photonize/Views/SettingsView.swift`, wired as a `Settings { }` scene in
  `PhotonizeApp.swift` so ⌘, works. Initial contents: default thumbnail size, and a "restore last
  folder on launch" toggle. It exists mainly so later slices have somewhere to put preferences
  instead of growing the toolbar.

### Menu bar fix

`PhotonizeApp.swift:14-32` currently does:

```swift
CommandGroup(replacing: .newItem) {}
CommandMenu("File") { … }        // ← adds a SECOND File menu
```

Replace with `CommandGroup(replacing: .newItem) { Open Folder…; Divider(); Refresh }` so the items
land in the real File menu. Slices 03, 04, 07 and 10 add further menu items; establishing the
correct pattern here stops each of them repeating the mistake.

### App icon

Add `macos/Photonize/Assets.xcassets` with an `AppIcon` image set, and switch `Info.plist` from
`CFBundleIconFile` to `ASSETCATALOG_COMPILER_APPICON_NAME = AppIcon` in the target settings. The
Windows app uses `icons/brown-photo-frame/favicon.ico` — the same artwork, rendered at the macOS
sizes (16/32/128/256/512 @1x and @2x), keeps the two clients recognisably the same product.

---

## Files

**New**
- `macos/project.yml` — XcodeGen spec
- `macos/Photonize/Services/DirectoryAccess.swift` — bookmark create/resolve/relinquish
- `macos/Photonize/Views/SettingsView.swift` — Settings scene
- `macos/Photonize/Assets.xcassets/AppIcon.appiconset/` — icon set

**Modified**
- `macos/Photonize/Photonize.entitlements` — add `com.apple.security.files.bookmarks.app-scope`
- `macos/Photonize/Models/PhotoItem.swift` — `AppSettings` gains `isTextEditorVisible`,
  `lastDirectoryBookmark`, with defaults
- `macos/Photonize/Services/SettingsManager.swift` — unchanged API; tolerate missing fields
- `macos/Photonize/ViewModels/PhotoGridViewModel.swift` — bookmark on browse, resolve on launch,
  relinquish on change; debounced `didSet` saves replacing the two ad-hoc `saveSettings()` calls
- `macos/Photonize/PhotonizeApp.swift` — `CommandGroup(replacing:)` instead of `CommandMenu`;
  add the `Settings` scene
- `macos/Photonize/Info.plist` — drop `CFBundleIconFile` in favour of the asset catalogue
- `.github/workflows/build-macos.yml` — install XcodeGen, version-bump `project.yml`, generate
- `macos/SETUP.md` — replace the manual Xcode instructions with the XcodeGen workflow

**Deleted**
- `macos/create-xcode-project.sh`

---

## Verification

1. **Project regenerates and builds.**
   ```bash
   cd macos && xcodegen generate
   xcodebuild -project Photonize.xcodeproj -scheme Photonize -configuration Release build
   ```
   Compare the target's file list against the nine current Swift files — nothing dropped.

2. **Bookmark survives relaunch.** Build a *signed, sandboxed* Release build (the bug is invisible
   in an unsandboxed debug run). Browse to a folder outside the container, e.g.
   `~/Pictures/holiday`. Quit. Relaunch. The photos load. Before this slice they do not.

3. **Stale bookmark degrades gracefully.** Rename the folder while the app is closed, relaunch:
   the app starts empty with a sensible status message rather than hanging or crashing.

4. **Settings persist immediately.** Move the thumbnail slider, toggle the preview, quit **without**
   loading a folder or renaming. Relaunch: both are restored. Check with
   `defaults read com.photonize.macos PhotonizeSettings`.

5. **One File menu.** Only one "File" appears in the menu bar, containing Open Folder… (⌘O) and
   Refresh (⌘R). ⌘, opens Settings.

6. **Icon.** The Dock and Finder show the Photonize icon, not the generic app placeholder.

7. **CI.** Run the `Build macOS App` workflow with a version like `v0.1.31` and confirm the DMG's
   `Info.plist` carries that `CFBundleShortVersionString`.

---

## Out of scope

- Universal (arm64) builds and release-workflow integration — slice 12.
- Code signing and notarization — documented in slice 12, not implemented here.
- `com.apple.security.network.client` — only needed by the deferred ComfyUI auto-caption feature.
- Any user-visible feature. This slice should look identical to today apart from the Settings
  scene, the corrected File menu, and the app icon.
