# Slice 12 — Tests and packaging

**Depends on:** everything. **Blocks:** nothing.

## Context

Three things are true of the macOS client that should not survive the rest of this roadmap.

**There are no tests.** Windows has `Photonize.Tests` — `WorkerQueueTests.cs` (446 lines) and
`TagTests.cs` — and both cover exactly the code most worth covering: the concurrency primitive and
the pure tagging logic. The macOS project has no test target at all, so every slice above ends its
Tests section with "these land in slice 12".

**The build is Intel-only.** `.github/workflows/build-macos.yml:45` hardcodes `-arch x86_64` and
the DMG is named `-intel`. `macos/README.md:18` says "Apple Silicon support coming soon" and
`macos/SETUP.md:180` lists it as a future enhancement. Every Mac sold since 2020 runs this through
Rosetta.

**macOS is not part of releases.** `release.yml` builds and publishes the Windows MSI on a `v*`
tag. The macOS workflow is `workflow_dispatch` only, producing an artifact that expires in 30 days.
`macos/README.md:191` documents a `release-macos.yml` triggered by `macos-v*` tags — **that file
does not exist**. Anyone following the README to cut a macOS release pushes a tag and nothing
happens.

Alongside these, the docs need a pass: `macos/README.md:264`'s comparison table and
`macos/SETUP.md:157`'s limitations list are both stale and directly contradict each other on
drag-and-drop (the table claims it works, the list says it does not).

**Outcome:** a test target running in CI, a universal binary, and one `v*` tag that ships both the
MSI and the DMG.

---

## Design

### Test target

Add to `macos/project.yml` (slice 01 made this a few lines rather than pbxproj surgery):

```yaml
targets:
  PhotonizeTests:
    type: bundle.unit-test
    platform: macOS
    sources: [PhotonizeTests]
    dependencies:
      - target: Photonize
```

XCTest rather than swift-testing — swift-testing needs a newer toolchain than the macOS 13
deployment target implies, and XCTest is what `xcodebuild test` runs without ceremony.

Coverage, gathered from each slice's Tests section:

| Suite | Covers | From slice |
|---|---|---|
| `FileRenamerTests` | prefix validation, `targetName` off-by-one, two-pass swap, `RenameResult` mapping, `.txt` follows its image | 04, 09 |
| `TagServiceTests` | sidecar round trip, out-of-range values ignored, sidecar deleted when empty, missing file returns empty | 10 |
| `TagSorterTests` | group ordering, stability, idempotence | 10 |
| `ThumbnailLoaderTests` | concurrency cap, generation-based cancellation, cache keying | 02 |
| `PhotoImporterTests` | insert/distribute position maths, dedup, already-in-target filtering | 06 |
| `ImageExporterTests` | folder naming, overwrite policy, WebP round trip | 05 |
| `SelectionTests` | shift-range from anchor, ⌘A files-only, row-wrapping arrows | 03 |
| `PromptFileStoreTests` | `.txt` path derivation, delete-on-empty, UTF-8 round trip | 09 |
| `LaunchArgumentTests` | argument parsing including Xcode-injected flags | 11 |

Two design constraints that the slices above already respect, and that make this cheap:

- The pure logic (`TagSorter`, `FileRenamer` naming, `PhotoImporter` positions, selection maths)
  is kept free of SwiftUI and file I/O, exactly as `TagSorter.cs` is on Windows.
- File-touching tests operate in `FileManager.default.temporaryDirectory` subdirectories created
  and torn down per test, never in the user's home.

`xcodebuild test -project Photonize.xcodeproj -scheme Photonize -destination 'platform=macOS'` runs
in CI on every push, alongside the existing Windows `dotnet test`.

### Universal binary

- `ARCHS: $(ARCHS_STANDARD)` in `project.yml` (slice 01 set this) — resolves to `arm64 x86_64` for
  a Release build.
- Drop `-arch x86_64` from the workflow; build with `-destination 'generic/platform=macOS'`.
- Rename the DMG `Photonize-<version>-universal.dmg`.
- Verify with `lipo -archs Photonize.app/Contents/MacOS/Photonize` → `x86_64 arm64`.

Slice 05's libwebp comes from SPM as source, so it compiles for both slices with no extra work —
worth confirming rather than assuming.

### Release integration

Fold macOS into `.github/workflows/release.yml` rather than creating the phantom
`release-macos.yml`. One `v*` tag, one GitHub release, two assets:

```yaml
jobs:
  build-windows:  # existing
  build-macos:
    runs-on: macos-latest
    steps: [checkout, brew install xcodegen, version-bump project.yml,
            xcodegen generate, xcodebuild test, xcodebuild build, create DMG]
  release:
    needs: [build-windows, build-macos]
    # extract [Unreleased] from CHANGELOG.md, attach MSI + DMG
```

The existing CHANGELOG extraction already produces the release notes; both platforms share them,
which is right — they are one product. Note in `CLAUDE.md`'s release checklist that a release now
covers both.

Keep `build-macos.yml` as the `workflow_dispatch` test build.

### Signing and notarization

Right now the DMG is unsigned, so every user meets Gatekeeper and has to right-click → Open. Do
not implement signing in this slice — it needs a paid Developer ID the project may not have — but
**document precisely what it would take**, in `macos/SETUP.md`:

- A Developer ID Application certificate exported as a `.p12`, stored as a repo secret alongside
  its password, an App Store Connect API key (issuer id, key id, `.p8`), and a keychain-import step
  in CI.
- `codesign --deep --force --options runtime --sign "Developer ID Application: …"` over the `.app`
  (hardened runtime is already enabled in the project settings).
- `xcrun notarytool submit --wait` on the DMG, then `xcrun stapler staple`.
- Until then, keep the Gatekeeper work-around in `macos/README.md:32` and make sure it says
  right-click → Open (System Settings moved the "Open Anyway" button in recent releases, so the
  current instructions are already slightly wrong).

### Documentation pass

- `macos/README.md`: rewrite the comparison table at line 264 against what actually shipped;
  correct the requirements line (universal, not Intel-only); fix the keyboard shortcuts list; drop
  the reference to the non-existent `release-macos.yml`; add tagging, video, export, compare,
  import and the prompt editor to the features list.
- `macos/SETUP.md`: replace "Known Limitations" (line 157) and "Future Enhancements" (line 176) —
  most items are now done, and the two that are not (Topaz upscaling, ComfyUI auto-caption) should
  point at `plans/README.md`'s deferred section rather than being restated.
- Root `README.md`: it currently opens "Photonize is a Windows desktop application". It is not,
  any more. Add the macOS download and a short parity note.
- `CHANGELOG.md`: one `[Unreleased]` entry covering the macOS parity work as a whole, in the Keep
  a Changelog format the release workflow parses.
- `CLAUDE.md`: the macOS build commands still describe `-arch x86_64` and manual Xcode steps;
  update for XcodeGen and universal builds.

---

## Files

**New**
- `macos/PhotonizeTests/` — the nine suites above
- `macos/project.yml` — `PhotonizeTests` target (modified, but the target is new)

**Modified**
- `.github/workflows/release.yml` — `build-macos` job, DMG asset, shared release notes
- `.github/workflows/build-macos.yml` — universal build, `xcodebuild test`, `-universal` DMG name
- `macos/README.md`, `macos/SETUP.md`, `README.md`, `CHANGELOG.md`, `CLAUDE.md` — documentation pass

---

## Verification

1. **Tests run and pass.**
   ```bash
   cd macos && xcodegen generate
   xcodebuild test -project Photonize.xcodeproj -scheme Photonize -destination 'platform=macOS'
   ```
   Then deliberately break one assertion and confirm CI goes red — a test job that cannot fail is
   worse than none.
2. **Cross-client parity by test.** `TagServiceTests` and `TagSorterTests` should assert the same
   behaviours as `Photonize.Tests/TagTests.cs`. Read both side by side; any divergence is a real
   bug in one of the clients.
3. **Universal binary.** `lipo -archs` on the built executable prints `x86_64 arm64`. Launch the
   DMG's app on an Apple Silicon Mac and confirm Activity Monitor shows Kind = Apple, not Intel.
4. **libwebp on both slices.** Export to WebP on an M-series Mac and on an Intel Mac. If libwebp
   only built for one architecture, this is where it shows.
5. **Release end to end.** Push a tag to a scratch repo or use `workflow_dispatch`. One GitHub
   release appears with both `PhotonizeInstaller-vX.Y.Z-x64.msi` and
   `Photonize-X.Y.Z-universal.dmg`, and the notes come from CHANGELOG's `[Unreleased]`.
6. **Fresh-machine install.** On a Mac that has never run Photonize, mount the DMG, drag to
   Applications, launch. The Gatekeeper instructions in the README must match what actually
   happens, word for word.
7. **Docs audit.** Re-read `macos/README.md` and `macos/SETUP.md` against the app. Every ✓ in the
   comparison table must be demonstrable, and nothing in "not yet implemented" may in fact be
   implemented. This is the check that has been failing until now.

---

## Out of scope

- Actually signing and notarizing — documented, not implemented, pending a Developer ID.
- Mac App Store distribution (a different sandbox profile and review process).
- Sparkle or any in-app updater.
- UI tests. The unit suites cover the logic; XCUITest against a drag-and-drop photo grid is a large
  investment for shallow coverage.
- Localisation (`macos/SETUP.md:189` lists it; neither client has it).
