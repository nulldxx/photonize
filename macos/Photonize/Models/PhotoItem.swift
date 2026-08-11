import Foundation
import SwiftUI

/// The kind of a grid item, used to pick the right context menu and to gate
/// operations (e.g. the `..` tile is never deletable). Keeping this a single
/// enum lets slices 05, 07, 08 and 10 add menu entries per-kind without
/// re-tangling one big `ViewBuilder`.
enum PhotoKind {
    case image
    case video   // reachable once slice 08 recognises video files
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

    var fileExtension: String {
        isFolder ? "" : filePath.pathExtension.lowercased()
    }

    var kind: PhotoKind {
        if isFolder {
            return fileName == ".." ? .parent : .folder
        }
        // Video recognition arrives in slice 08; every file is an image today.
        return .image
    }

    static func == (lhs: PhotoItem, rhs: PhotoItem) -> Bool {
        lhs.id == rhs.id
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
