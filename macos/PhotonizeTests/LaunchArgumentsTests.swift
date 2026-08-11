import XCTest
@testable import Photonize

// NOTE: No test target yet (slice 12 adds it). Kept alongside the code for now.
// Little of slice 11 is unit-testable — it is AppKit lifecycle — but the pure
// command-line parser and the intent → restore-suppression contract are.
final class LaunchArgumentsTests: XCTestCase {

    // MARK: - Directory

    func testShortDirectoryFlag() {
        let intent = AppDelegate.parseLaunchArguments(["Photonize", "-d", "/tmp/x"])
        XCTAssertEqual(intent, .openDirectory(URL(fileURLWithPath: "/tmp/x")))
    }

    func testLongDirectoryFlag() {
        let intent = AppDelegate.parseLaunchArguments(["Photonize", "--directory", "/tmp/x"])
        XCTAssertEqual(intent, .openDirectory(URL(fileURLWithPath: "/tmp/x")))
    }

    /// The array the plan documents, exe path omitted, still parses — the scan
    /// tolerates arg[0] being either the executable or the first real argument.
    func testDirectoryFlagWithoutExecutablePath() {
        let intent = AppDelegate.parseLaunchArguments(["-d", "/tmp/x"])
        XCTAssertEqual(intent, .openDirectory(URL(fileURLWithPath: "/tmp/x")))
    }

    /// A dangling `-d` with no following path is ignored rather than fatal.
    func testDirectoryFlagWithoutValueIsIgnored() {
        XCTAssertEqual(AppDelegate.parseLaunchArguments(["Photonize", "-d"]), .none)
    }

    // MARK: - Export

    func testExportFlagCollectsRemainingFiles() {
        // Existence/type filtering happens later, so non-existent test paths still
        // parse to an export intent.
        let intent = AppDelegate.parseLaunchArguments(["Photonize", "-e", "a.jpg", "b.jpg"])
        XCTAssertEqual(intent, .exportImages([
            URL(fileURLWithPath: "a.jpg"),
            URL(fileURLWithPath: "b.jpg"),
        ]))
    }

    func testExportFlagWithNoFilesIsIgnored() {
        XCTAssertEqual(AppDelegate.parseLaunchArguments(["Photonize", "-e"]), .none)
    }

    // MARK: - No / unknown arguments

    func testNoArguments() {
        XCTAssertEqual(AppDelegate.parseLaunchArguments(["Photonize"]), .none)
    }

    /// Xcode injects debug flags like `-NSDocumentRevisionsDebugMode YES`; a
    /// strict parser would refuse to launch under the debugger, so these are
    /// silently ignored.
    func testDebuggerInjectedArgumentsAreIgnored() {
        let intent = AppDelegate.parseLaunchArguments(
            ["Photonize", "-NSDocumentRevisionsDebugMode", "YES"]
        )
        XCTAssertEqual(intent, .none)
    }

    func testDirectorySurvivesDebuggerInjectedArguments() {
        let intent = AppDelegate.parseLaunchArguments(
            ["Photonize", "-d", "/tmp/x", "-NSDocumentRevisionsDebugMode", "YES"]
        )
        XCTAssertEqual(intent, .openDirectory(URL(fileURLWithPath: "/tmp/x")))
    }

    // MARK: - Restore suppression

    /// A `-d`/`-e` launch suppresses the saved-folder restore; a plain launch does
    /// not. (The rest of the settings still load — verified by reading
    /// `loadSettings(skipDirectoryRestore:)`.)
    func testDirectoryIntentSuppressesRestore() {
        XCTAssertTrue(LaunchIntent.openDirectory(URL(fileURLWithPath: "/tmp/x")).suppressesDirectoryRestore)
    }

    func testExportIntentSuppressesRestore() {
        XCTAssertTrue(LaunchIntent.exportImages([URL(fileURLWithPath: "a.jpg")]).suppressesDirectoryRestore)
    }

    func testNoIntentDoesNotSuppressRestore() {
        XCTAssertFalse(LaunchIntent.none.suppressesDirectoryRestore)
    }
}
