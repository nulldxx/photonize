import XCTest
import AppKit
@testable import Photonize

// NOTE: There is no test target yet — slice 12 creates it. These tests are kept
// here alongside the code they cover so they are ready to wire in.
final class ThumbnailLoaderTests: XCTestCase {

    /// The loader must never run more than `maxConcurrent` decodes at once.
    func testNeverExceedsMaxConcurrent() async {
        let maxConcurrent = 3
        let observed = ConcurrencyProbe()

        let loader = ThumbnailLoader(maxConcurrent: maxConcurrent) { _, _ in
            await observed.enter()
            // Hold the permit long enough that later requests must queue.
            try? await Task.sleep(nanoseconds: 20_000_000)
            await observed.leave()
            return nil
        }

        let gen = await loader.currentGeneration
        await withTaskGroup(of: Void.self) { group in
            for i in 0..<50 {
                group.addTask {
                    _ = await loader.request(URL(fileURLWithPath: "/tmp/\(i)"), size: 100, generation: gen)
                }
            }
        }

        let peak = await observed.peak
        XCTAssertLessThanOrEqual(peak, maxConcurrent, "peak in-flight \(peak) exceeded cap \(maxConcurrent)")
        XCTAssertGreaterThan(peak, 1, "expected some real concurrency")

        let loaderPeak = await loader.peakInFlight
        XCTAssertLessThanOrEqual(loaderPeak, maxConcurrent)
    }

    /// After `cancelAll()`, results stamped with the old generation are discarded.
    func testCancelAllDiscardsStaleGeneration() async {
        let started = AsyncGate()
        let loader = ThumbnailLoader(maxConcurrent: 2) { _, _ in
            await started.open()
            try? await Task.sleep(nanoseconds: 30_000_000)
            return NSImage(size: NSSize(width: 1, height: 1))
        }

        let staleGen = await loader.currentGeneration
        async let stale = loader.request(URL(fileURLWithPath: "/tmp/a"), size: 100, generation: staleGen)

        // Wait until the decode is actually running, then supersede it.
        await started.wait()
        _ = await loader.cancelAll()

        let staleResult = await stale
        XCTAssertNil(staleResult, "result from a superseded generation should be dropped")

        // A request on the fresh generation still succeeds.
        let freshGen = await loader.currentGeneration
        let freshResult = await loader.request(URL(fileURLWithPath: "/tmp/b"), size: 100, generation: freshGen)
        XCTAssertNotNil(freshResult)
    }
}

/// Tracks concurrent entries to assert a ceiling.
private actor ConcurrencyProbe {
    private var current = 0
    private(set) var peak = 0

    func enter() {
        current += 1
        peak = max(peak, current)
    }

    func leave() {
        current -= 1
    }
}

/// One-shot gate: `open()` releases anyone parked in `wait()`.
private actor AsyncGate {
    private var isOpen = false
    private var waiters: [CheckedContinuation<Void, Never>] = []

    func open() {
        guard !isOpen else { return }
        isOpen = true
        for waiter in waiters { waiter.resume() }
        waiters.removeAll()
    }

    func wait() async {
        if isOpen { return }
        await withCheckedContinuation { waiters.append($0) }
    }
}
