import SwiftUI

/// The user's answer to "insert here or distribute evenly?". `nil` (via Cancel)
/// aborts the import.
enum ImportModeChoice {
    case insert
    case distribute
}

/// A pending request to choose an import mode. Presented as a sheet; the view
/// model awaits the answer through the stored completion. The completion is
/// niled after the first call so an escape-then-button (or double tap) cannot
/// resume the continuation twice.
@MainActor
final class ImportModeRequest: ObservableObject, Identifiable {
    let id = UUID()
    let newCount: Int
    let existingCount: Int
    let insertIndex: Int
    private var completion: ((ImportModeChoice?) -> Void)?

    init(newCount: Int, existingCount: Int, insertIndex: Int, completion: @escaping (ImportModeChoice?) -> Void) {
        self.newCount = newCount
        self.existingCount = existingCount
        self.insertIndex = insertIndex
        self.completion = completion
    }

    func resolve(_ choice: ImportModeChoice?) {
        let pending = completion
        completion = nil
        pending?(choice)
    }
}

/// The analogue of the Windows `ImportDialog.xaml`: insert the dropped photos at
/// the drop position, or distribute them evenly through the set, or cancel.
struct ImportModeSheet: View {
    @ObservedObject var request: ImportModeRequest

    var body: some View {
        VStack(spacing: 16) {
            Text("Insert at drop position or distribute evenly?")
                .font(.headline)
                .multilineTextAlignment(.center)

            Text(detail)
                .font(.system(size: 12))
                .foregroundColor(.secondary)
                .multilineTextAlignment(.center)

            HStack(spacing: 12) {
                Button("Insert Here") { request.resolve(.insert) }
                    .help("Insert the new photos at the position where they were dropped")

                Button("Distribute Evenly") { request.resolve(.distribute) }
                    .help("Interleave the new photos evenly among the existing photos")
            }
            .controlSize(.large)

            Button("Cancel") { request.resolve(nil) }
                .keyboardShortcut(.cancelAction)
        }
        .padding(24)
        .frame(width: 420)
        // The choice must come from a button, not a swipe-away, or the awaiting
        // continuation would never resume.
        .interactiveDismissDisabled()
    }

    private var detail: String {
        if request.insertIndex >= request.existingCount {
            return "Adding \(request.newCount) new photo(s) to \(request.existingCount) existing photo(s)"
        }
        return "Adding \(request.newCount) new photo(s) to \(request.existingCount) existing photo(s), "
            + "inserting at position \(request.insertIndex + 1)"
    }
}
