import SwiftUI

/// Observable state for a long-running, cancellable operation. Owned by the view
/// model and presented as a sheet. Deliberately general — export builds it here,
/// and slices 06 (import) and 10 (tag-move) reuse it unchanged, the way Windows
/// shares one `ProgressDialog.xaml` across every batch operation.
@MainActor
final class OperationProgress: ObservableObject, Identifiable {
    let id = UUID()
    @Published var title: String
    @Published var status: String
    @Published var current: Int
    @Published var total: Int
    @Published private(set) var isCancelling = false

    /// The task doing the work. `cancel()` cancels it; the operation observes
    /// `Task.isCancelled` between units and stops.
    var task: Task<Void, Never>?

    init(title: String, status: String = "", current: Int = 0, total: Int = 0) {
        self.title = title
        self.status = status
        self.current = current
        self.total = total
    }

    func update(current: Int, total: Int, status: String) {
        self.current = current
        self.total = total
        self.status = status
    }

    /// Flip to the "Cancelling…" affordance and cancel the owning task. Partial
    /// output stays on disk; the completion summary reports how far it got.
    func cancel() {
        guard !isCancelling else { return }
        isCancelling = true
        task?.cancel()
    }
}

/// The shared progress sheet: title, status line, a determinate bar, an "N of M"
/// count, and a Cancel button that becomes "Cancelling…" once pressed — the same
/// affordances as the Windows `ProgressDialog.xaml`.
struct ProgressSheet: View {
    @ObservedObject var progress: OperationProgress

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            Text(progress.title)
                .font(.headline)

            Text(progress.status)
                .font(.system(size: 12))
                .foregroundColor(.secondary)
                .lineLimit(1)
                .frame(maxWidth: .infinity, alignment: .leading)

            ProgressView(value: fraction)
                .progressViewStyle(.linear)

            HStack {
                Text("\(progress.current) of \(progress.total)")
                    .font(.system(size: 12))
                    .foregroundColor(.secondary)

                Spacer()

                Button(progress.isCancelling ? "Cancelling…" : "Cancel") {
                    progress.cancel()
                }
                .disabled(progress.isCancelling)
            }
        }
        .padding(24)
        .frame(width: 380)
    }

    /// Clamped so an off-by-one completion count never overfills the bar.
    private var fraction: Double {
        guard progress.total > 0 else { return 0 }
        return min(1, Double(progress.current) / Double(progress.total))
    }
}
