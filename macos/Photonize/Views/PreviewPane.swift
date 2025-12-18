import SwiftUI

struct PreviewPane: View {
    @EnvironmentObject var viewModel: PhotoGridViewModel
    @State private var previewImage: NSImage?

    var body: some View {
        VStack(spacing: 0) {
            if let photo = viewModel.previewPhoto, !photo.isFolder {
                // Image preview
                ScrollView([.horizontal, .vertical]) {
                    if let image = previewImage {
                        Image(nsImage: image)
                            .resizable()
                            .aspectRatio(contentMode: .fit)
                    } else {
                        ProgressView()
                            .frame(maxWidth: .infinity, maxHeight: .infinity)
                    }
                }
                .frame(maxWidth: .infinity, maxHeight: .infinity)
                .background(Color.black)

                Divider()

                // Info panel
                VStack(alignment: .leading, spacing: 8) {
                    InfoRow(label: "Name:", value: photo.fileName)
                    InfoRow(label: "Type:", value: photo.fileExtension.uppercased())

                    if let image = previewImage {
                        InfoRow(label: "Size:", value: "\(Int(image.size.width)) × \(Int(image.size.height))")
                    }

                    if let fileSize = try? FileManager.default.attributesOfItem(atPath: photo.filePath.path)[.size] as? Int64 {
                        InfoRow(label: "File Size:", value: formatFileSize(fileSize))
                    }
                }
                .padding()
                .frame(maxWidth: .infinity, alignment: .leading)
                .background(Color(NSColor.controlBackgroundColor))
            } else {
                VStack {
                    Image(systemName: "photo.on.rectangle")
                        .font(.system(size: 60))
                        .foregroundColor(.gray)

                    Text("No Preview Available")
                        .font(.headline)
                        .foregroundColor(.gray)
                        .padding(.top)
                }
                .frame(maxWidth: .infinity, maxHeight: .infinity)
            }
        }
        .onChange(of: viewModel.previewPhoto) { newPhoto in
            loadPreview(for: newPhoto)
        }
        .onAppear {
            loadPreview(for: viewModel.previewPhoto)
        }
    }

    private func loadPreview(for photo: PhotoItem?) {
        guard let photo = photo, !photo.isFolder else {
            previewImage = nil
            return
        }

        Task {
            let image = await ThumbnailGenerator().generatePreview(for: photo.filePath)
            await MainActor.run {
                previewImage = image
            }
        }
    }

    private func formatFileSize(_ bytes: Int64) -> String {
        let formatter = ByteCountFormatter()
        formatter.countStyle = .file
        return formatter.string(fromByteCount: bytes)
    }
}

struct InfoRow: View {
    let label: String
    let value: String

    var body: some View {
        HStack {
            Text(label)
                .font(.system(size: 11, weight: .semibold))
                .foregroundColor(.secondary)
                .frame(width: 80, alignment: .trailing)

            Text(value)
                .font(.system(size: 11))
                .textSelection(.enabled)
        }
    }
}
