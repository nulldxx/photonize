import Foundation

/// Manages security-scoped access to user-selected folders so the app can
/// re-open the last directory across launches under the App Sandbox.
///
/// The sandbox only grants access to folders the user picked in an
/// `NSOpenPanel` for the lifetime of the process. To regain access on the
/// next launch we persist a security-scoped bookmark and resolve it at startup.
final class DirectoryAccess {

    /// Create an app-scoped bookmark for a folder the user picked in NSOpenPanel.
    /// Returns nil if the bookmark could not be created.
    func makeBookmark(for url: URL) -> Data? {
        do {
            return try url.bookmarkData(
                options: [.withSecurityScope],
                includingResourceValuesForKeys: nil,
                relativeTo: nil
            )
        } catch {
            NSLog("DirectoryAccess.makeBookmark failed: \(error.localizedDescription)")
            return nil
        }
    }

    /// The result of resolving a stored bookmark.
    struct Resolved {
        /// The folder URL, with security-scoped access already started.
        let url: URL
        /// A freshly re-derived bookmark, present only when the stored bookmark
        /// was stale. Callers should persist it in place of the old one.
        let refreshedBookmark: Data?
    }

    /// Resolve a stored bookmark, starting security-scoped access.
    /// Returns nil if the bookmark is unresolvable or the folder is gone.
    /// On success the caller owns the started access and must eventually
    /// balance it with `relinquish(_:)`.
    func resolve(_ bookmark: Data) -> Resolved? {
        var isStale = false
        let url: URL
        do {
            url = try URL(
                resolvingBookmarkData: bookmark,
                options: [.withSecurityScope],
                relativeTo: nil,
                bookmarkDataIsStale: &isStale
            )
        } catch {
            NSLog("DirectoryAccess.resolve failed: \(error.localizedDescription)")
            return nil
        }

        guard url.startAccessingSecurityScopedResource() else {
            return nil
        }

        // The folder may have been resolved but no longer exist on disk.
        guard FileManager.default.fileExists(atPath: url.path) else {
            url.stopAccessingSecurityScopedResource()
            return nil
        }

        var refreshedBookmark: Data?
        if isStale {
            refreshedBookmark = makeBookmark(for: url)
        }

        return Resolved(url: url, refreshedBookmark: refreshedBookmark)
    }

    /// Balance the `startAccessingSecurityScopedResource` call made in `resolve`.
    /// Safe to call with a URL that was never scoped.
    func relinquish(_ url: URL) {
        url.stopAccessingSecurityScopedResource()
    }
}
