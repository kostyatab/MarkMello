import Foundation
import QuickLookUI
import UniformTypeIdentifiers

typealias RenderFn = @convention(c) (UnsafePointer<CChar>) -> UnsafeMutablePointer<CChar>?
typealias FreeFn = @convention(c) (UnsafeMutablePointer<CChar>?) -> Void

final class PreviewProvider: QLPreviewProvider, QLPreviewingController {
    func providePreview(for request: QLFilePreviewRequest) async throws -> QLPreviewReply {
        let url = request.fileURL
        let html = Self.render(url: url)
        return QLPreviewReply(dataOfContentType: .html, contentSize: CGSize(width: 800, height: 900)) { reply in
            reply.stringEncoding = .utf8
            return Data(html.utf8)
        }
    }

    private static func render(url: URL) -> String {
        let libURL = Bundle.main.bundleURL
            .appendingPathComponent("Contents/Frameworks/libmmpreview.dylib")
        let started = Date()
        guard let handle = dlopen(libURL.path, RTLD_NOW) else {
            let err = String(cString: dlerror())
            return "<html><body><h1>dlopen failed</h1><pre>\(err)</pre></body></html>"
        }
        guard let renderSym = dlsym(handle, "mm_render_html"),
              let freeSym = dlsym(handle, "mm_free") else {
            return "<html><body><h1>dlsym failed</h1></body></html>"
        }
        let loadMs = Int(Date().timeIntervalSince(started) * 1000)
        let render = unsafeBitCast(renderSym, to: RenderFn.self)
        let free = unsafeBitCast(freeSym, to: FreeFn.self)
        let out = url.path.withCString { render($0) }
        defer { free(out) }
        guard let out else { return "<html><body>null</body></html>" }
        return String(cString: out) + "<!-- dlopen \(loadMs) ms -->"
            + "<p><small>swift: dlopen \(loadMs) ms</small></p>"
    }
}
