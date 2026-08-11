import Foundation

/// The value a `WindowGroup(for:)` presents to open the compare window: the two
/// photos to compare, A over B.
///
/// `Codable & Hashable` because a data-driven `WindowGroup` encodes the presented
/// value for state restoration and uses equality to reuse an already-open window
/// for the same pair rather than stacking a second one.
///
/// Ordered by the grid's display order — A is the earlier photo, the one the
/// divider reveals, matching the Windows compare window which passes its
/// selection in order.
struct ComparePair: Codable, Hashable {
    let urlA: URL
    let urlB: URL
}
