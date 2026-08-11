import SwiftUI

/// One editable row in the move-tagged sheet: a tag digit and its badge, an
/// editable destination folder name (defaulting to the digit) and how many
/// photos carry the tag.
@MainActor
final class TagFolderRow: ObservableObject, Identifiable {
    let id = UUID()
    let tag: Int
    let count: Int
    @Published var folderName: String

    init(tag: Int, count: Int) {
        self.tag = tag
        self.count = count
        self.folderName = String(tag)
    }

    var countLabel: String { count == 1 ? "1 photo" : "\(count) photos" }
}

/// A pending "move tagged images into sub-folders" request. Presented as a sheet;
/// the view model awaits the per-tag folder names through the stored completion
/// (nil == cancelled). The analogue of the Windows `TagFolderDialog`. The
/// completion is niled after the first call so an escape-then-button (or double
/// tap) can't resolve it twice.
@MainActor
final class TagFolderRequest: ObservableObject, Identifiable {
    let id = UUID()
    let rows: [TagFolderRow]
    private var completion: (([Int: String]?) -> Void)?

    init(counts: [(tag: Int, count: Int)], completion: @escaping ([Int: String]?) -> Void) {
        self.rows = counts
            .sorted { $0.tag < $1.tag }
            .map { TagFolderRow(tag: $0.tag, count: $0.count) }
        self.completion = completion
    }

    func resolve(_ names: [Int: String]?) {
        let pending = completion
        completion = nil
        pending?(names)
    }
}

/// The macOS analogue of `TagFolderDialog.xaml`: one row per tag in use, each
/// with its badge, an editable sub-folder name and a photo count. Move / Cancel.
struct TagFolderSheet: View {
    @ObservedObject var request: TagFolderRequest

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            Text("Move tagged images into sub-folders")
                .font(.headline)

            Text("Choose a sub-folder name for each tag.")
                .font(.system(size: 12))
                .foregroundColor(.secondary)

            ScrollView {
                VStack(spacing: 10) {
                    ForEach(request.rows) { row in
                        TagFolderRowView(row: row)
                    }
                }
            }
            .frame(maxHeight: 220)

            HStack {
                Spacer()
                Button("Cancel") { request.resolve(nil) }
                    .keyboardShortcut(.cancelAction)
                Button("Move") { request.resolve(folderNames()) }
                    .keyboardShortcut(.defaultAction)
            }
        }
        .padding(24)
        .frame(width: 460)
        // The answer must come from a button, or the awaiting continuation would
        // never resume.
        .interactiveDismissDisabled()
    }

    private func folderNames() -> [Int: String] {
        var result: [Int: String] = [:]
        for row in request.rows {
            result[row.tag] = row.folderName.trimmingCharacters(in: .whitespaces)
        }
        return result
    }
}

struct TagFolderRowView: View {
    @ObservedObject var row: TagFolderRow

    var body: some View {
        HStack(spacing: 10) {
            TagBadge(digit: row.tag)

            TextField("Folder name", text: $row.folderName)
                .textFieldStyle(.roundedBorder)

            Text(row.countLabel)
                .font(.system(size: 12))
                .foregroundColor(.secondary)
                .frame(width: 72, alignment: .trailing)
        }
    }
}
