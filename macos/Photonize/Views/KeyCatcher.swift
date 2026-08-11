import SwiftUI
import AppKit

/// Directions the focus cursor can travel across the grid.
enum FocusDirection {
    case left, right, up, down
}

/// A key event the grid knows how to act on. `KeyCatcher` decodes an `NSEvent`
/// into one of these and hands it to the view model's single `handleKey` entry
/// point, so later slices (e.g. 0-9 tagging) add their case in one place.
enum GridKey: Equatable {
    case arrow(FocusDirection, shift: Bool)
    case returnKey
    case digit(Int)
}

/// A zero-size first-responder view that turns `keyDown` into `GridKey` values.
///
/// We roll our own rather than reach for `.onKeyPress` (macOS 14+, and the
/// deployment target is 13) or a global `NSEvent` monitor (which would fire
/// while the user is typing in the directory field or, from slice 09, the text
/// editor — precisely the bug the Windows build avoids by scoping key handling
/// to the photo list rather than the window).
struct KeyCatcher: NSViewRepresentable {
    /// Return `true` if the key was handled and should not propagate further.
    var onKey: (GridKey) -> Bool

    func makeNSView(context: Context) -> KeyCatcherView {
        let view = KeyCatcherView()
        view.onKey = onKey
        return view
    }

    func updateNSView(_ nsView: KeyCatcherView, context: Context) {
        nsView.onKey = onKey
    }
}

final class KeyCatcherView: NSView {
    var onKey: ((GridKey) -> Bool)?

    override var acceptsFirstResponder: Bool { true }

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        // Grab focus once we're in a window so arrow keys reach us without the
        // user having to click a photo first. Only take it if nothing more
        // specific (e.g. the prefix field's editor) already holds it.
        DispatchQueue.main.async { [weak self] in
            guard let self, let window = self.window else { return }
            if window.firstResponder === window || window.firstResponder === window.contentView {
                window.makeFirstResponder(self)
            }
        }
    }

    override func keyDown(with event: NSEvent) {
        guard let key = KeyCatcherView.decode(event), onKey?(key) == true else {
            super.keyDown(with: event)
            return
        }
    }

    /// Map a raw key event to a `GridKey`. Command/Control/Option chords are left
    /// for the menu bar (⌘A, ⌘O, ⌘R and friends) and return `nil` here.
    private static func decode(_ event: NSEvent) -> GridKey? {
        if event.modifierFlags.contains(.command)
            || event.modifierFlags.contains(.control)
            || event.modifierFlags.contains(.option) {
            return nil
        }

        let shift = event.modifierFlags.contains(.shift)

        switch event.keyCode {
        case 123: return .arrow(.left, shift: shift)
        case 124: return .arrow(.right, shift: shift)
        case 125: return .arrow(.down, shift: shift)
        case 126: return .arrow(.up, shift: shift)
        case 36, 76: return .returnKey        // Return, keypad Enter
        default: break
        }

        // Digits 0-9 (top row and numpad), reserved for slice 10's quick tagging.
        if !shift, let chars = event.charactersIgnoringModifiers, chars.count == 1,
           let scalar = chars.unicodeScalars.first, (48...57).contains(scalar.value) {
            return .digit(Int(scalar.value) - 48)
        }

        return nil
    }
}
