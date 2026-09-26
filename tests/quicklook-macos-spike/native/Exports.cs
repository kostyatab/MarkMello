using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using MarkMello.Application.Abstractions;
using MarkMello.Application.Diagrams;
using MarkMello.Application.UseCases;
using MarkMello.Domain;
using MarkMello.Infrastructure.Diagrams;
using MarkMello.Infrastructure.Markdown;
#if HIGHLIGHT
using MarkMello.Infrastructure.Highlighting;
#endif

// Spike only: grubby HTML walker over the domain model, enough to prove
// parity path (Markdig -> RenderedMarkdownDocument -> HTML) works in-sandbox.
public static unsafe class Exports
{
    [UnmanagedCallersOnly(EntryPoint = "mm_render_html")]
    public static byte* RenderHtml(byte* pathUtf8)
    {
        var sw = Stopwatch.StartNew();
        var path = Marshal.PtrToStringUTF8((nint)pathUtf8) ?? "";
        string html;
        try
        {
            var text = File.ReadAllText(path);
            var dir = Path.GetDirectoryName(path);
            var useCase = new RenderMarkdownDocumentUseCase(
                new MarkdigMarkdownDocumentRenderer(),
                new DiagramRenderService([new MermaidDiagramRenderer(new ApproximateTextMeasurer())]));
            var doc = useCase.Execute(text, dir);
#if HIGHLIGHT
            using var hl = new TextMateCodeHighlighter();
            doc = new HighlightCodeBlocksUseCase(hl).Execute(doc, CancellationToken.None);
#endif
            var sb = new StringBuilder();
            sb.Append("<!doctype html><html><head><meta charset=utf-8><style>")
              .Append(":root{color-scheme:light dark}body{font:15px -apple-system,sans-serif;max-width:720px;margin:24px auto;padding:0 16px}pre{background:#8881;padding:12px;border-radius:6px;overflow:auto}.kw{color:#c678dd}.str{color:#98c379}.cm{color:#888}img{max-width:100%}")
              .Append("</style></head><body>");
            Blocks(sb, doc.Blocks, dir);
            sb.Append("<hr><small>spike: rendered in ").Append(sw.ElapsedMilliseconds).Append(" ms, pid ")
              .Append(Environment.ProcessId).Append(", sandbox=")
              .Append(Environment.GetEnvironmentVariable("APP_SANDBOX_CONTAINER_ID") ?? "none")
              .Append("</small></body></html>");
            html = sb.ToString();
        }
        catch (Exception ex)
        {
            html = "<html><body><pre>" + WebUtility.HtmlEncode(ex.ToString()) + "</pre></body></html>";
        }
        return ToNative(html);
    }

    [UnmanagedCallersOnly(EntryPoint = "mm_free")]
    public static void Free(byte* p) => NativeMemory.Free(p);

    static void Blocks(StringBuilder sb, IReadOnlyList<MarkdownBlock> blocks, string? dir)
    {
        foreach (var b in blocks)
        {
            switch (b)
            {
                case MarkdownHeadingBlock h:
                    sb.Append("<h").Append(h.Level).Append('>'); Inlines(sb, h.Inlines, dir); sb.Append("</h").Append(h.Level).Append('>'); break;
                case MarkdownParagraphBlock p:
                    sb.Append("<p>"); Inlines(sb, p.Inlines, dir); sb.Append("</p>"); break;
                case MarkdownQuoteBlock q:
                    sb.Append("<blockquote>"); Blocks(sb, q.Blocks, dir); sb.Append("</blockquote>"); break;
                case MarkdownListBlock l:
                    sb.Append(l.IsOrdered ? "<ol>" : "<ul>");
                    foreach (var i in l.Items) { sb.Append("<li>"); Blocks(sb, i.Blocks, dir); sb.Append("</li>"); }
                    sb.Append(l.IsOrdered ? "</ol>" : "</ul>"); break;
                case MarkdownCodeBlock c:
                    sb.Append("<pre><code>");
                    if (c.Tokens is { } tokens)
                    {
                        var pos = 0;
                        foreach (var t in tokens)
                        {
                            sb.Append(WebUtility.HtmlEncode(c.Code[pos..t.Start]));
                            var cls = t.Kind switch { MarkdownCodeTokenKind.Keyword => "kw", MarkdownCodeTokenKind.StringLiteral => "str", MarkdownCodeTokenKind.Comment => "cm", _ => "" };
                            sb.Append("<span class=").Append(cls).Append('>').Append(WebUtility.HtmlEncode(c.Code.Substring(t.Start, t.Length))).Append("</span>");
                            pos = t.Start + t.Length;
                        }
                        sb.Append(WebUtility.HtmlEncode(c.Code[pos..]));
                    }
                    else sb.Append(WebUtility.HtmlEncode(c.Code));
                    sb.Append("</code></pre>"); break;
                case MarkdownDiagramBlock d:
                    sb.Append(d.RenderResult is DiagramRenderResult.Success s ? s.Svg : "<pre>" + WebUtility.HtmlEncode(d.Source) + "</pre>"); break;
                case MarkdownImageBlock img:
                    Image(sb, img.Url, img.AltText, dir); break;
                case MarkdownTableBlock t:
                    sb.Append("<table><tr>");
                    foreach (var cell in t.Header) { sb.Append("<th>"); Inlines(sb, cell.Inlines, dir); sb.Append("</th>"); }
                    sb.Append("</tr>");
                    foreach (var row in t.Rows) { sb.Append("<tr>"); foreach (var cell in row) { sb.Append("<td>"); Inlines(sb, cell.Inlines, dir); sb.Append("</td>"); } sb.Append("</tr>"); }
                    sb.Append("</table>"); break;
                case MarkdownHorizontalRuleBlock:
                    sb.Append("<hr>"); break;
                default:
                    sb.Append("<p><i>[").Append(b.GetType().Name).Append("]</i></p>"); break;
            }
        }
    }

    static void Inlines(StringBuilder sb, IReadOnlyList<MarkdownInline> inlines, string? dir)
    {
        foreach (var i in inlines)
        {
            switch (i)
            {
                case MarkdownTextInline t: sb.Append(WebUtility.HtmlEncode(t.Text)); break;
                case MarkdownStrongInline s: sb.Append("<b>"); Inlines(sb, s.Inlines, dir); sb.Append("</b>"); break;
                case MarkdownEmphasisInline e: sb.Append("<i>"); Inlines(sb, e.Inlines, dir); sb.Append("</i>"); break;
                case MarkdownCodeInline c: sb.Append("<code>").Append(WebUtility.HtmlEncode(c.Code)).Append("</code>"); break;
                case MarkdownLinkInline l: sb.Append("<a href=\"").Append(WebUtility.HtmlEncode(l.Url)).Append("\">"); Inlines(sb, l.Inlines, dir); sb.Append("</a>"); break;
                case MarkdownImageInline img: Image(sb, img.Url, img.AltText, dir); break;
                case MarkdownLineBreakInline: sb.Append("<br>"); break;
                default: sb.Append("[").Append(i.GetType().Name).Append("]"); break;
            }
        }
    }

    // Relative images are read by the extension itself and inlined as data: URI —
    // this is the sibling-file read that needs the sandbox exception.
    static void Image(StringBuilder sb, string url, string? alt, string? dir)
    {
        string src;
        if (dir is not null && !url.Contains("://", StringComparison.Ordinal))
        {
            try
            {
                var full = Path.GetFullPath(Path.Combine(dir, url));
                var mime = Path.GetExtension(full).ToLowerInvariant() switch { ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".svg" => "image/svg+xml", ".gif" => "image/gif", _ => "application/octet-stream" };
                src = "data:" + mime + ";base64," + Convert.ToBase64String(File.ReadAllBytes(full));
            }
            catch (Exception ex)
            {
                sb.Append("<p style=color:red>image read failed: ").Append(WebUtility.HtmlEncode(ex.GetType().Name + ": " + ex.Message)).Append("</p>");
                return;
            }
        }
        else
        {
            sb.Append("<p>[remote image skipped: ").Append(WebUtility.HtmlEncode(url)).Append("]</p>");
            return;
        }
        sb.Append("<img alt=\"").Append(WebUtility.HtmlEncode(alt ?? "")).Append("\" src=\"").Append(src).Append("\">");
    }

    static byte* ToNative(string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        var p = (byte*)NativeMemory.Alloc((nuint)bytes.Length + 1);
        bytes.CopyTo(new Span<byte>(p, bytes.Length));
        p[bytes.Length] = 0;
        return p;
    }
}

// No Avalonia in the Quick Look extension: estimate label widths for the
// sequence-diagram layout; the browser draws the SVG with its own fonts.
file sealed class ApproximateTextMeasurer : IDiagramTextMeasurer
{
    public DiagramTextSize Measure(string text, string fontFamily, double fontSize, bool bold)
        => new(text.Length * fontSize * (bold ? 0.62 : 0.56), fontSize * 1.2);
}
