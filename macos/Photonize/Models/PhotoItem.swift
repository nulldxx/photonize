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

    static let `default` = AppSettings(
        lastDirectoryPath: "",
        lastThumbnailSize: 200,
        lastPrefix: "",
        isPreviewVisible: false
    )
}
