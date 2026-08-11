import Foundation
import SwiftUI

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
