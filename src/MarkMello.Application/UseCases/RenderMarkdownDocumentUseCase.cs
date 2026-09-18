using MarkMello.Application.Abstractions;
using MarkMello.Domain;

namespace MarkMello.Application.UseCases;

/// <summary>
/// Изолирует markdown pipeline от presentation layer и гарантирует безопасный fallback.
/// Parse/render ошибки не должны ломать viewer path.
///
/// После Markdig pass проходит по полученным блокам и материализует
/// <see cref="DiagramRenderResult"/> для каждого <see cref="MarkdownDiagramBlock"/>
/// через зарегистрированный <see cref="IDiagramRenderService"/> — view получает
/// документ с уже готовыми SVG payload-ами и не вызывает renderer на каждый
/// rebuild (ADR-0005 §5).
/// </summary>
public sealed class RenderMarkdownDocumentUseCase
{
    private readonly IMarkdownDocumentRenderer _renderer;
    private readonly IDiagramRenderService _diagramService;

    public RenderMarkdownDocumentUseCase(
        IMarkdownDocumentRenderer renderer,
        IDiagramRenderService diagramService)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(diagramService);
        _renderer = renderer;
        _diagramService = diagramService;
    }

    public RenderedMarkdownDocument Execute(string markdown)
        => Execute(markdown, baseDirectory: null);

    public RenderedMarkdownDocument Execute(string markdown, string? baseDirectory)
    {
        RenderedMarkdownDocument rendered;
        try
        {
            rendered = _renderer.Render(markdown, baseDirectory);
        }
        catch
        {
            var fallback = RenderedMarkdownDocument.PlainText(markdown);
            return baseDirectory is null ? fallback : fallback with { BaseDirectory = baseDirectory };
        }

        return MaterializeDiagramResults(rendered);
    }

    private RenderedMarkdownDocument MaterializeDiagramResults(RenderedMarkdownDocument document)
    {
        if (!ContainsAnyDiagramBlock(document.Blocks))
        {
            return document;
        }

        var rebuilt = MaterializeBlocks(document.Blocks);
        return document with { Blocks = rebuilt };
    }

    private List<MarkdownBlock> MaterializeBlocks(IReadOnlyList<MarkdownBlock> blocks)
    {
        var result = new List<MarkdownBlock>(blocks.Count);
        foreach (var block in blocks)
        {
            result.Add(MaterializeBlock(block));
        }
        return result;
    }

    private MarkdownBlock MaterializeBlock(MarkdownBlock block) => block switch
    {
        MarkdownDiagramBlock diagram => diagram with
        {
            RenderResult = _diagramService.Render(diagram.Kind, diagram.Source),
        },
        MarkdownQuoteBlock quote => quote with { Blocks = MaterializeBlocks(quote.Blocks) },
        MarkdownListBlock list => list with { Items = MaterializeListItems(list.Items) },
        _ => block,
    };

    private List<MarkdownListItem> MaterializeListItems(IReadOnlyList<MarkdownListItem> items)
    {
        var result = new List<MarkdownListItem>(items.Count);
        foreach (var item in items)
        {
            result.Add(item with { Blocks = MaterializeBlocks(item.Blocks) });
        }
        return result;
    }

    private static bool ContainsAnyDiagramBlock(IReadOnlyList<MarkdownBlock> blocks)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case MarkdownDiagramBlock:
                    return true;
                case MarkdownQuoteBlock quote when ContainsAnyDiagramBlock(quote.Blocks):
                    return true;
                case MarkdownListBlock list:
                    foreach (var item in list.Items)
                    {
                        if (ContainsAnyDiagramBlock(item.Blocks))
                        {
                            return true;
                        }
                    }
                    break;
            }
        }
        return false;
    }
}
