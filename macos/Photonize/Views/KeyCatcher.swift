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

/// A zero-size view that routes the grid's keys (arrows, Return, 0-9) to the
/// view model.
///
/// It listens with a **local `NSEvent` keyDown monitor** rather than relying on
/// being the first responder: on macOS the directory `TextField` readily grabs
/// and keeps keyboard focus (SwiftUI auto-focuses the first text field), so a
/// first-responder-only catcher silently lost 0-9 to the address bar. The
/// monitor fires before the event reaches whoever holds focus, but it yields
/// whenever the user is genuinely editing text (the path/prefix fields or the
/// caption editor), so typing still works — the same scoping the Windows build
/// gets by attaching key handling to the photo list rather than the window.
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

    static func dismantleNSView(_ nsView: KeyCatcherView, coordinator: ()) {
        nsView.removeMonitor()
    }
}

final class KeyCatcherView: NSView {
    var onKey: ((GridKey) -> Bool)?
    private var monitor: Any?

    override var acceptsFirstResponder: Bool { true }

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        if window == nil {
            removeMonitor()
            return
        }
        installMonitorIfNeeded()

        // Take focus off any auto-focused text field at launch so arrow keys and
        // 0-9 reach the grid without the user having to click a photo first. Only
        // if nothing more specific already holds it.
        DispatchQueue.main.async { [weak self] in
            guard let self, let window = self.window else { return }
            if window.firstResponder === window || window.firstResponder === window.contentView {
                window.makeFirstResponder(self)
            }
        }
    }

    deinit { removeMonitor() }

    private func installMonitorIfNeeded() {
        guard monitor == nil else { return }
        monitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            self?.handle(event) ?? event
        }
    }

    func removeMonitor() {
        if let monitor { NSEvent.removeMonitor(monitor) }
        monitor = nil
    }

    /// Decide the fate of a keyDown. Acts only for our own key window, and never
    /// while a text field or the caption editor is being edited (first responder
    /// is an `NSText`), so those keystrokes pass straight through. Returns `nil`
    /// to swallow a key the grid handled, or the event to let it continue.
    private func handle(_ event: NSEvent) -> NSEvent? {
        guard let window, window.isKeyWindow else { return event }
        if window.firstResponder is NSText { return event }
        guard let key = KeyCatcherView.decode(event), onKey?(key) == true else { return event }
        return nil
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
