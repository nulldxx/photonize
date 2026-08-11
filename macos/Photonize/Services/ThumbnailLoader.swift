import Foundation
import AppKit

/// Bounded-concurrency loader for thumbnail-style work — the Swift analogue of
/// the Windows `WorkerQueue<T>`. It caps the number of in-flight decodes at
/// `maxConcurrent` and supports whole-queue cancellation by generation counter.
///
/// Cancellation is deliberately *not* a `CancellationToken` threaded through
/// every job. Instead each request carries a generation stamp; `cancelAll()`
/// bumps the loader's generation, and any request whose stamp no longer matches
/// is discarded the moment it would otherwise start or finish. This handles the
/// real case — the user clicking through folders faster than they load — without
/// the bookkeeping of per-job tokens.
actor ThumbnailLoader {
    /// A unit of decode work. Injectable so slices 05 (export) and 08 (video)
    /// can drive the same concurrency gate with a different decoder, and so
    /// tests can substitute a controllable stub.
    typealias Decoder = (URL, Int) async -> NSImage?

    private let maxConcurrent: Int
    private let decode: Decoder

    /// Simple async semaphore: `available` permits, with parked callers resumed
    /// by direct hand-off so a released permit goes straight to the next waiter.
    private var available: Int
    private var waiters: [CheckedContinuation<Void, Never>] = []

    private var generation: Int = 0

    /// Instrumentation for the concurrency test in slice 12. Never read in
    /// production code paths.
    private(set) var inFlight = 0
    private(set) var peakInFlight = 0

    init(maxConcurrent: Int = ProcessInfo.processInfo.activeProcessorCount,
         decode: Decoder? = nil) {
        self.maxConcurrent = max(1, maxConcurrent)
        self.available = max(1, maxConcurrent)
        if let decode {
            self.decode = decode
        } else {
            let generator = ThumbnailGenerator()
            self.decode = { url, size in await generator.generateThumbnail(for: url, maxSize: size) }
        }
    }

    /// The generation new work should be stamped with.
    var currentGeneration: Int { generation }

    /// Abandon everything in flight and return a fresh generation token. In-flight
    /// decodes are not force-killed (there is no safe way to interrupt ImageIO),
    /// but their results are dropped and they release their permits promptly,
    /// which drains any parked waiters.
    @discardableResult
    func cancelAll() -> Int {
        generation += 1
        return generation
    }

    /// Decode one thumbnail, honouring the concurrency cap. Returns `nil` if the
    /// generation was superseded (either before starting or by the time the
    /// decode finished) or if the decode itself failed.
    func request(_ url: URL, size: Int, generation gen: Int) async -> NSImage? {
        guard gen == generation else { return nil }

        await acquire()
        defer { release() }

        // May have been cancelled while parked waiting for a permit.
        guard gen == generation else { return nil }

        inFlight += 1
        peakInFlight = max(peakInFlight, inFlight)
        let image = await decode(url, size)
        inFlight -= 1

        return gen == generation ? image : nil
    }

    // MARK: - Async semaphore

    private func acquire() async {
        if available > 0 {
            available -= 1
            return
        }
        await withCheckedContinuation { continuation in
            waiters.append(continuation)
        }
        // Resumed via direct hand-off: the permit is already ours, `available`
        // was intentionally not incremented by `release()`.
    }

    private func release() {
        if waiters.isEmpty {
            available += 1
        } else {
            let next = waiters.removeFirst()
            next.resume()
        }
    }
}
