using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Avalonia;
using MarkMello.Application.Abstractions;
using MarkMello.Domain;
using MarkMello.Infrastructure.Diagrams;
using MarkMello.Infrastructure.Diagrams.Sequence;
using MarkMello.Presentation.Services;
using MarkMello.Presentation.Views.Markdown;
using Mermaid = Naiad.Mermaid;
using RenderOptions = Naiad.RenderOptions;

namespace MarkMello.Presentation.Tests;

[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class SequenceDiagramLayoutTests(AvaloniaHeadlessFixture fixture)
{
    private static readonly XNamespace SvgNamespace = "http://www.w3.org/2000/svg";

    private const string BlocksSource =
        """
        sequenceDiagram
            title Checkout with retries and a long title that is wider than the diagram
            actor Customer as Returning customer with a long name
            participant Shop
            participant Pay as Payment gateway (sandbox)
            Customer->>+Shop: Place order #1042 with express delivery
            loop Until the gateway answers
                Shop->>Pay: Authorize card ending 4242 for 129.90 EUR
                Pay--xShop: Timeout after 30 seconds
                Shop->>Shop: Back off exponentially before the next attempt
            end
            alt Approved
                Pay-->>Shop: Approved
            else Declined
                Pay--)Shop: Declined by issuer
            end
            Note right of Pay: The gateway keeps the authorization for seven days
            Note left of Customer: Waiting on the confirmation page
            Note over Customer, Pay: Everything below happens after payment
            activate Pay
            Pay->Customer: Receipt by e-mail
            deactivate Pay
            Shop-->>-Customer: Order confirmed
        """;

    [Fact]
    public Task Issue35LabelsFitTheCanvasAndParticipantBoxes()
        => fixture.Session.Dispatch(() =>
        {
            var svg = Render(ReadIssueSource());
            AssertLabelsInsideCanvas(svg);

            var boxes = Boxes(svg);
            Assert.Equal(2, boxes.Count);
            Assert.All(boxes, box => Assert.True(Number(box, "width") >= 240));
            var serverLabels = Texts(svg).Where(static text => text.Value == "Remote Server (Port 8080)").ToList();
            Assert.Equal(2, serverLabels.Count);
            foreach (var label in serverLabels)
            {
                var box = boxes.Single(box => Math.Abs(Number(box, "y") + 20 - Number(label, "y")) < 0.01);
                var half = Measure(label).Width / 2;
                Assert.InRange(Number(label, "x") - half, Number(box, "x") + 10, double.MaxValue);
                Assert.InRange(Number(label, "x") + half, double.MinValue, Number(box, "x") + Number(box, "width") - 10);
            }

            Assert.True(AotSafeSvgImage.TryLoad(Encoding.UTF8.GetBytes(svg.ToString()), out var image));
            Assert.Equal(Canvas(svg)[2], image.Size.Width, precision: 2);
        }, CancellationToken.None);

    [Fact]
    public Task Issue35MessageLabelsDoNotOverlapEachOtherOrTheNotes()
        => fixture.Session.Dispatch(() =>
        {
            var svg = Render(ReadIssueSource());
            var labels = Texts(svg).Select(text => Bounds(text)).ToList();
            var notes = svg.Elements(SvgNamespace + "path")
                .Where(static path => (string?)path.Attribute("fill") == "#FFFFCC")
                .Select(NoteBounds).ToList();
            Assert.Equal(3, notes.Count);
            for (var first = 0; first < labels.Count; first++)
            {
                for (var second = first + 1; second < labels.Count; second++)
                {
                    Assert.False(labels[first].Intersects(labels[second]),
                        $"Labels overlap: {labels[first]} and {labels[second]}");
                }

                // A note's own label lies inside it; any other label must stay clear.
                Assert.All(notes.Where(note => !note.Contains(labels[first])),
                    note => Assert.False(note.Intersects(labels[first]), $"Label runs into a note: {labels[first]}"));
            }
        }, CancellationToken.None);

    [Fact]
    public Task InitControlsParticipantWidthAndMargin()
        => fixture.Session.Dispatch(() =>
        {
            var original = Render(ReadIssueSource());
            var wider = Render(ReadIssueSource()
                .Replace("\"width\": 240", "\"width\": 300", StringComparison.Ordinal)
                .Replace("\"actorMargin\": 120", "\"actorMargin\": 320", StringComparison.Ordinal));

            Assert.True(Canvas(wider)[2] > Canvas(original)[2]);
            Assert.All(Boxes(wider), box => Assert.True(Number(box, "width") >= 300));
            // Centres are at least (width + width) / 2 + actorMargin apart.
            Assert.True(Gap(original) >= 240 + 120 - 0.01);
            Assert.True(Gap(wider) >= 300 + 320 - 0.01);
        }, CancellationToken.None);

    [Fact]
    public Task MessageMarginChangesVerticalSpacing()
        => fixture.Session.Dispatch(() =>
        {
            var original = Render(ReadIssueSource());
            var spacious = Render(ReadIssueSource().Replace("\"messageMargin\": 45", "\"messageMargin\": 120", StringComparison.Ordinal));

            Assert.True(Canvas(spacious)[3] > Canvas(original)[3]);
            var rows = MessageRows(spacious);
            Assert.All(rows.Zip(rows.Skip(1)), pair => Assert.True(pair.Second - pair.First >= 120 - 0.01));
        }, CancellationToken.None);

    [Fact]
    public Task WithoutInitParticipantsTakeMermaidDefaultsAndLabelsStillFit()
        => fixture.Session.Dispatch(() =>
        {
            var source = ReadIssueSource();
            var svg = Render(source[(source.IndexOf("}%%", StringComparison.Ordinal) + 3)..]);

            AssertLabelsInsideCanvas(svg);
            var box = Boxes(svg)[0];
            var label = Texts(svg).First(static text => text.Value == "Remote Server (Port 8080)");
            Assert.True(Number(box, "width") >= 150);
            Assert.True(Measure(label).Width + 20 <= Number(box, "width") + 0.01);
        }, CancellationToken.None);

    [Fact]
    public Task NotesSpanBothParticipants()
        => fixture.Session.Dispatch(() =>
        {
            var svg = Render(ReadIssueSource());
            var lifelines = svg.Elements(SvgNamespace + "line")
                .Where(static line => (string?)line.Attribute("stroke") == "#999").ToList();
            var left = lifelines.Min(line => Number(line, "x1"));
            var right = lifelines.Max(line => Number(line, "x1"));
            var children = svg.Elements().ToList();
            foreach (var note in children.Where(static element => (string?)element.Attribute("fill") == "#FFFFCC"))
            {
                var bounds = NoteBounds(note);
                var label = children[children.IndexOf(note) + 3];
                Assert.True(bounds.Left <= left && bounds.Right >= right);
                Assert.True(bounds.Contains(Bounds(label)));
            }
        }, CancellationToken.None);

    [Theory]
    [InlineData("Note left of Client: 1. Connection Initialization")]
    [InlineData("Note right of Server: 1. Connection Initialization")]
    public Task NotesBesideOuterParticipantsStayInsideCanvas(string note)
        => fixture.Session.Dispatch(() =>
        {
            var svg = Render(ReadIssueSource().Replace(
                "Note over Client, Server: 1. Connection Initialization", note, StringComparison.Ordinal));

            AssertLabelsInsideCanvas(svg);
            var canvas = new Rect(0, 0, Canvas(svg)[2], Canvas(svg)[3]);
            Assert.All(svg.Elements(SvgNamespace + "path").Where(static path => (string?)path.Attribute("fill") == "#FFFFCC"),
                path => Assert.True(canvas.Contains(NoteBounds(path))));
        }, CancellationToken.None);

    [Fact]
    public Task SelfMessageRetainsItsLoopGeometryAndLabelFits()
        => fixture.Session.Dispatch(() =>
        {
            var svg = Render(ReadIssueSource().Replace("Client->>Server: Init Request", "Client->>Client: Init Request", StringComparison.Ordinal));
            var loop = svg.Elements(SvgNamespace + "path").Single(element => (string?)element.Attribute("stroke") == "#333");
            var points = loop.Attribute("d")!.Value.Split(' ').Select(point => point[1..].Split(',')
                .Select(value => double.Parse(value, CultureInfo.InvariantCulture)).ToArray()).ToArray();

            Assert.Equal(40, points[1][0] - points[0][0], precision: 2);
            Assert.Equal(30, points[2][1] - points[0][1], precision: 2);
            AssertLabelsInsideCanvas(svg);
        }, CancellationToken.None);

    [Fact]
    public Task ActorNameHangsBelowTheFigure()
        => fixture.Session.Dispatch(() =>
        {
            var svg = Render(ReadIssueSource());
            var heads = svg.Elements(SvgNamespace + "circle").ToList();
            Assert.Equal(2, heads.Count);
            foreach (var head in heads)
            {
                var label = svg.Elements().SkipWhile(element => element != head).Skip(5).First();
                Assert.Equal("Client App", label.Value);
                Assert.Equal("hanging", (string?)label.Attribute("dominant-baseline"));
                Assert.True(Number(label, "y") >= Number(head, "cy") + 31);
                Assert.True(Number(label, "y") + Measure(label).Height <= Canvas(svg)[3]);
            }
        }, CancellationToken.None);

    [Fact]
    public Task BlocksNotesActivationsAndTitleAreLaidOut()
        => fixture.Session.Dispatch(() =>
        {
            var svg = Render(BlocksSource);

            Assert.NotEqual(NaiadXml(BlocksSource), svg.ToString(SaveOptions.DisableFormatting));
            AssertLabelsInsideCanvas(svg);
            var title = Texts(svg).First();
            Assert.Equal(Canvas(svg)[2] / 2, Number(title, "x"), precision: 2);
            var bars = svg.Elements(SvgNamespace + "rect").Where(static rect => (string?)rect.Attribute("fill") == "#F4F4F4").ToList();
            Assert.Equal(2, bars.Count);
            var rows = MessageRows(svg);
            Assert.All(bars, bar =>
            {
                Assert.Contains(rows, row => Math.Abs(row - Number(bar, "y")) < 0.01);
                Assert.Contains(rows, row => Math.Abs(row - Number(bar, "y") - Number(bar, "height")) < 0.01);
            });
            Assert.True(AotSafeSvgImage.TryLoad(Encoding.UTF8.GetBytes(svg.ToString()), out _));
        }, CancellationToken.None);

    [Fact]
    public Task MeasurerWorksOffTheUiThreadLikeTheEditPreview()
        => fixture.RunAsync(async () =>
        {
            // The edit-mode preview renders diagrams from a thread-pool thread.
            // Had measuring failed there, the layout would have fallen back
            // to Naiad's SVG, whose labels leave its 290 px canvas.
            var svg = await Task.Run(() => Render(ReadIssueSource())).ConfigureAwait(true);

            Assert.NotEqual(NaiadXml(ReadIssueSource()), svg.ToString(SaveOptions.DisableFormatting));
            Assert.All(Boxes(svg), box => Assert.True(Number(box, "width") >= 240));
            AssertLabelsInsideCanvas(svg);
        });

    [Theory]
    [InlineData("sequenceDiagram\nAlice->>Bob: Hi\nbox Aqua Group\nBob->>Alice: Hidden by Naiad")]
    [InlineData("sequenceDiagram\nAlice->>Bob: Hi\ncreate participant Carol\nAlice->>Carol: Hidden by Naiad")]
    [InlineData("sequenceDiagram\n    participant A\n    participant A\n    A->>A: Twice")]
    public void UnknownConstructKeepsNaiadSvg(string source)
    {
        var result = new MermaidDiagramRenderer(new FixedAdvanceDiagramTextMeasurer()).Render(new DiagramRenderRequest(source));

        var success = Assert.IsType<DiagramRenderResult.Success>(result);
        Assert.Equal(NaiadSvg(source), success.Svg);
    }

    [Fact]
    public void UnexpectedSvgMarkupKeepsTheSvgItWasGiven()
    {
        const string source = "sequenceDiagram\n    Alice->>Bob: Hi";
        var options = new RenderOptions();
        var naiad = Mermaid.Render(source, options);
        var tampered = naiad.Replace("<rect ", "<ellipse ", StringComparison.Ordinal);

        Assert.Equal(tampered, SequenceSvgLayout.Apply(source, tampered, options, new FixedAdvanceDiagramTextMeasurer()));
        Assert.Equal("<svg/>", SequenceSvgLayout.Apply(source, "<svg/>", options, new FixedAdvanceDiagramTextMeasurer()));
    }

    [Fact]
    public void MeasurerFailureKeepsNaiadSvg()
    {
        const string source = "sequenceDiagram\n    Alice->>Bob: Hi";

        var result = new MermaidDiagramRenderer(new ThrowingMeasurer()).Render(new DiagramRenderRequest(source));

        Assert.Equal(NaiadSvg(source), Assert.IsType<DiagramRenderResult.Success>(result).Svg);
    }

    [Theory]
    [InlineData("flowchart LR\n    A[Start] --> B{Decide}\n    B -->|yes| C[End]")]
    [InlineData("stateDiagram-v2\n    [*] --> Idle\n    Idle --> Working: start")]
    [InlineData("classDiagram\n    class Animal {\n        +String name\n    }\n    Animal <|-- Dog")]
    [InlineData("pie title Pets\n    \"Dogs\" : 386\n    \"Cats\" : 85")]
    [InlineData("gantt\n    title Plan\n    dateFormat YYYY-MM-DD\n    section Build\n    Text :a1, 2026-09-01, 3d")]
    [InlineData("%%{init: {\"sequence\": {\"width\": 400}}}%%\nflowchart TD\n    A --> B")]
    public void OtherDiagramTypesRenderExactlyAsNaiadDrawsThem(string source)
    {
        var result = new MermaidDiagramRenderer(new FixedAdvanceDiagramTextMeasurer()).Render(new DiagramRenderRequest(source));

        Assert.Equal(NaiadSvg(source), Assert.IsType<DiagramRenderResult.Success>(result).Svg);
    }

    [Theory]
    [InlineData("%%{init: {\"sequence\": {\"width\": 240, \"actorMargin\": 120, \"messageMargin\": 45}}}%%", 240, 120, 45)]
    [InlineData("%%{ init: { \"sequence\": { \"width\": 180 } } }%%", 180, 50, 35)]
    [InlineData("%%{init: {\"theme\": \"dark\"}}%%", 150, 50, 35)]
    [InlineData("%%{init: {\"sequence\": {\"width\": -5, \"actorMargin\": \"wide\"}}}%%", 150, 50, 35)]
    [InlineData("%%{init: {not json}}%%", 150, 50, 35)]
    [InlineData("%%{init: {'sequence': {'width': 240, 'actorMargin': 120}}}%%", 240, 120, 35)]
    [InlineData("%%{initialize: {\"sequence\": {\"messageMargin\": 60}}}%%", 150, 50, 60)]
    [InlineData("%%{init : {\"sequence\": {\"width\": 200}}}%%", 200, 50, 35)]
    [InlineData("%%{initial: {\"sequence\": {\"width\": 200}}}%%", 150, 50, 35)]
    [InlineData("", 150, 50, 35)]
    public void InitDirectiveReadsOnlyValidSequenceSizes(string directive, double width, double actorMargin, double messageMargin)
    {
        var (source, settings) = SequenceDiagramSettings.Read(directive + "\nsequenceDiagram\n    A->>B: Hi\n");

        Assert.Equal("sequenceDiagram\n    A->>B: Hi", source);
        Assert.Equal(new SequenceDiagramSettings(width, actorMargin, messageMargin), settings);
    }

    private static void AssertLabelsInsideCanvas(XElement svg)
    {
        var canvas = new Rect(0, 0, Canvas(svg)[2], Canvas(svg)[3]);
        foreach (var text in Texts(svg))
        {
            Assert.True(canvas.Contains(Bounds(text)), $"Label leaves the canvas: {text.Value} at {Bounds(text)} in {canvas}");
        }
    }

    /// <summary>Where the viewer draws a label, from its anchor and baseline.</summary>
    private static Rect Bounds(XElement text)
    {
        var size = Measure(text);
        var x = Number(text, "x");
        var y = Number(text, "y");
        var left = (string?)text.Attribute("text-anchor") switch
        {
            "middle" => x - size.Width / 2,
            "end" => x - size.Width,
            _ => x,
        };
        var top = (string?)text.Attribute("dominant-baseline") switch
        {
            "middle" => y - size.Height / 2,
            "hanging" => y,
            "bottom" => y - size.Height,
            _ => y - size.Height * 0.8,
        };
        return new Rect(left, top, size.Width, size.Height);
    }

    private static Rect NoteBounds(XElement path)
    {
        var points = path.Attribute("d")!.Value.Split(' ')
            .Where(static part => part != "Z")
            .Select(static part => part[1..].Split(',').Select(value => double.Parse(value, CultureInfo.InvariantCulture)).ToArray())
            .ToList();
        var left = points.Min(static point => point[0]);
        var top = points.Min(static point => point[1]);
        return new Rect(left, top, points.Max(static point => point[0]) - left, points.Max(static point => point[1]) - top);
    }

    private static List<double> MessageRows(XElement svg)
        => svg.Elements(SvgNamespace + "line")
            .Where(static line => (string?)line.Attribute("stroke") == "#333"
                && Number(line, "y1") == Number(line, "y2") && Math.Abs(Number(line, "x2") - Number(line, "x1")) > 20)
            .Select(static line => Number(line, "y1"))
            .ToList();

    private static double Gap(XElement svg)
    {
        var lifelines = svg.Elements(SvgNamespace + "line")
            .Where(static line => (string?)line.Attribute("stroke") == "#999").Select(static line => Number(line, "x1")).ToList();
        return lifelines.Max() - lifelines.Min();
    }

    private static List<XElement> Boxes(XElement svg)
        => svg.Elements(SvgNamespace + "rect").Where(static rect => (string?)rect.Attribute("fill") == "#ECECFF").ToList();

    private static IEnumerable<XElement> Texts(XElement svg) => svg.Elements(SvgNamespace + "text");

    private static XElement Render(string source)
    {
        var renderer = new MermaidDiagramRenderer(new AvaloniaDiagramTextMeasurer());
        var result = Assert.IsType<DiagramRenderResult.Success>(renderer.Render(new DiagramRenderRequest(source)));
        return XDocument.Parse(result.Svg).Root!;
    }

    private static string NaiadSvg(string source) => Mermaid.Render(source, new RenderOptions());

    /// <summary>
    /// Naiad's SVG serialised as <see cref="XElement"/> writes it, so it can be
    /// compared with a parsed result: Naiad quotes attributes with <c>'</c>.
    /// </summary>
    private static string NaiadXml(string source)
        => XDocument.Parse(NaiadSvg(source)).Root!.ToString(SaveOptions.DisableFormatting);

    private static string ReadIssueSource()
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "issue-35.md"))
            .Replace("```mermaid", string.Empty, StringComparison.Ordinal)
            .Replace("```", string.Empty, StringComparison.Ordinal).Trim();

    private static double Number(XElement element, string name)
        => double.Parse(element.Attribute(name)!.Value, CultureInfo.InvariantCulture);

    private static double[] Canvas(XElement svg)
        => svg.Attribute("viewBox")!.Value.Split(' ').Select(value => double.Parse(value, CultureInfo.InvariantCulture)).ToArray();

    private static DiagramTextSize Measure(XElement text)
        => new AvaloniaDiagramTextMeasurer().Measure(text.Value, text.Attribute("font-family")!.Value,
            double.Parse(text.Attribute("font-size")!.Value.Replace("px", string.Empty, StringComparison.Ordinal), CultureInfo.InvariantCulture),
            (string?)text.Attribute("font-weight") == "bold");

    private sealed class ThrowingMeasurer : IDiagramTextMeasurer
    {
        public DiagramTextSize Measure(string text, string fontFamily, double fontSize, bool bold)
            => throw new InvalidOperationException("No fonts here.");
    }
}
