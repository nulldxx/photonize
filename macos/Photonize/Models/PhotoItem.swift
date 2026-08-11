import Foundation
import SwiftUI

/// The kind of a grid item, used to pick the right context menu and to gate
/// operations (e.g. the `..` tile is never deletable). Keeping this a single
/// enum lets slices 05, 07, 08 and 10 add menu entries per-kind without
/// re-tangling one big `ViewBuilder`.
enum PhotoKind {
    case image
    case video
    case folder
    case parent  // the ".." navigation tile
}

struct PhotoItem: Identifiable, Equatable {
    let id = UUID()
    var filePath: URL
    var fileName: String
    var displayOrder: Int
    var isFolder: Bool
    var thumbnail: NSImage?
    /// Set at enumeration time (slice 08) from the file extension. A default keeps
    /// the synthesized memberwise initializer usable by the many call sites that
    /// only ever construct images or folders.
    var isVideo: Bool = false
    /// Quick-tag digit 0-9 the user applied, or nil when untagged (slice 10). A
    /// default keeps the synthesized initializer usable at the many call sites
    /// that build plain images and folders.
    var tag: Int? = nil

    var hasTag: Bool { tag != nil }

    var fileExtension: String {
        isFolder ? "" : filePath.pathExtension.lowercased()
    }

    var kind: PhotoKind {
        if isFolder {
            return fileName == ".." ? .parent : .folder
        }
        return isVideo ? .video : .image
    }

    /// Value equality across every rendered field, not just `id`. SwiftUI uses
    /// this to decide whether a grid cell needs redrawing: an id-only `==` makes
    /// it treat a late-arriving `thumbnail` (or a new `tag`, or a rename) as "no
    /// change" and keep showing the placeholder until an unrelated relayout forces
    /// a rebuild. `thumbnail` is compared by identity (`===`) since `NSImage`
    /// isn't `Equatable` and the loader always hands back a fresh instance.
    /// Grid reordering keys off `id` (`firstIndex(where:)`), never this `==`, so
    /// a thumbnail arriving mid-drag can't disturb a move.
    static func == (lhs: PhotoItem, rhs: PhotoItem) -> Bool {
        lhs.id == rhs.id
            && lhs.filePath == rhs.filePath
            && lhs.fileName == rhs.fileName
            && lhs.displayOrder == rhs.displayOrder
            && lhs.isFolder == rhs.isFolder
            && lhs.isVideo == rhs.isVideo
            && lhs.tag == rhs.tag
            && lhs.thumbnail === rhs.thumbnail
    }
}

struct AppSettings: Codable {
    var lastDirectoryPath: String
    var lastThumbnailSize: Double
    var lastPrefix: String
    var isPreviewVisible: Bool
    var isTextEditorVisible: Bool
    var lastDirectoryBookmark: Data?
    var restoreLastFolderOnLaunch: Bool

    static let `default` = AppSettings(
        lastDirectoryPath: "",
        lastThumbnailSize: 200,
        lastPrefix: "",
        isPreviewVisible: false,
        isTextEditorVisible: false,
        lastDirectoryBookmark: nil,
        restoreLastFolderOnLaunch: true
    )

    // New optional fields default so an older persisted blob still decodes.
    enum CodingKeys: String, CodingKey {
        case lastDirectoryPath
        case lastThumbnailSize
        case lastPrefix
        case isPreviewVisible
        case isTextEditorVisible
        case lastDirectoryBookmark
        case restoreLastFolderOnLaunch
    }

    init(
        lastDirectoryPath: String,
        lastThumbnailSize: Double,
        lastPrefix: String,
        isPreviewVisible: Bool,
        isTextEditorVisible: Bool = false,
        lastDirectoryBookmark: Data? = nil,
        restoreLastFolderOnLaunch: Bool = true
    ) {
        self.lastDirectoryPath = lastDirectoryPath
        self.lastThumbnailSize = lastThumbnailSize
        self.lastPrefix = lastPrefix
        self.isPreviewVisible = isPreviewVisible
        self.isTextEditorVisible = isTextEditorVisible
        self.lastDirectoryBookmark = lastDirectoryBookmark
        self.restoreLastFolderOnLaunch = restoreLastFolderOnLaunch
    }

    init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        lastDirectoryPath = try container.decode(String.self, forKey: .lastDirectoryPath)
        lastThumbnailSize = try container.decode(Double.self, forKey: .lastThumbnailSize)
        lastPrefix = try container.decode(String.self, forKey: .lastPrefix)
        isPreviewVisible = try container.decode(Bool.self, forKey: .isPreviewVisible)
        isTextEditorVisible = try container.decodeIfPresent(Bool.self, forKey: .isTextEditorVisible) ?? false
        lastDirectoryBookmark = try container.decodeIfPresent(Data.self, forKey: .lastDirectoryBookmark)
        restoreLastFolderOnLaunch = try container.decodeIfPresent(Bool.self, forKey: .restoreLastFolderOnLaunch) ?? true
    }
}
