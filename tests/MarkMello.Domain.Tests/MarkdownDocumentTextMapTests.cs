using MarkMello.Domain;

namespace MarkMello.Domain.Tests;

public sealed class MarkdownDocumentTextMapTests
{
    [Fact]
    public void CreateBuildsCanonicalPlainTextForTypicalDocument()
    {
        var document = new RenderedMarkdownDocument(
        [
            new MarkdownHeadingBlock(1, [new MarkdownTextInline("Title")]),
            new MarkdownParagraphBlock(
            [
                new MarkdownTextInline("Hello "),
                new MarkdownLinkInline([new MarkdownTextInline("docs")], "https://example.com/docs", null),
                new MarkdownTextInline("!")
            ]),
            new MarkdownListBlock(false,
            [
                new MarkdownListItem([new MarkdownParagraphBlock([new MarkdownTextInline("One")])]),
                new MarkdownListItem([new MarkdownParagraphBlock([new MarkdownTextInline("Two")])])
            ]),
            new MarkdownCodeBlock("csharp", "line1\nline2"),
            new MarkdownTableBlock(
            [
                new MarkdownTableCell([new MarkdownTextInline("A")]),
                new MarkdownTableCell([new MarkdownTextInline("B")])
            ],
            [
                [
                    new MarkdownTableCell([new MarkdownTextInline("1")]),
                    new MarkdownTableCell([new MarkdownTextInline("2")])
                ],
                [
                    new MarkdownTableCell([new MarkdownTextInline("3")]),
                    new MarkdownTableCell([new MarkdownTextInline("4")])
                ]
            ])
        ]);

        var textMap = MarkdownDocumentTextMap.Create(document);

        Assert.Equal(
            "Title\n\nHello docs!\n\n• One\n• Two\n\nline1\nline2\n\nA\tB\n1\t2\n3\t4\n\n",
            textMap.Text);

        Assert.Collection(
            textMap.Fragments,
            fragment => AssertFragment(textMap.Text, fragment, "b0", MarkdownDocumentTextFragmentKind.Heading, "Title"),
            fragment => AssertFragment(textMap.Text, fragment, "b1", MarkdownDocumentTextFragmentKind.Paragraph, "Hello docs!"),
            fragment => AssertFragment(textMap.Text, fragment, "b2.i0.m", MarkdownDocumentTextFragmentKind.ListMarker, "• "),
            fragment => AssertFragment(textMap.Text, fragment, "b2.i0.b0", MarkdownDocumentTextFragmentKind.Paragraph, "One"),
            fragment => AssertFragment(textMap.Text, fragment, "b2.i1.m", MarkdownDocumentTextFragmentKind.ListMarker, "• "),
            fragment => AssertFragment(textMap.Text, fragment, "b2.i1.b0", MarkdownDocumentTextFragmentKind.Paragraph, "Two"),
            fragment => AssertFragment(textMap.Text, fragment, "b3", MarkdownDocumentTextFragmentKind.CodeBlock, "line1\nline2"),
            fragment => AssertFragment(textMap.Text, fragment, "b4.h0", MarkdownDocumentTextFragmentKind.TableCell, "A"),
            fragment => AssertFragment(textMap.Text, fragment, "b4.h1", MarkdownDocumentTextFragmentKind.TableCell, "B"),
            fragment => AssertFragment(textMap.Text, fragment, "b4.r0.c0", MarkdownDocumentTextFragmentKind.TableCell, "1"),
            fragment => AssertFragment(textMap.Text, fragment, "b4.r0.c1", MarkdownDocumentTextFragmentKind.TableCell, "2"),
            fragment => AssertFragment(textMap.Text, fragment, "b4.r1.c0", MarkdownDocumentTextFragmentKind.TableCell, "3"),
            fragment => AssertFragment(textMap.Text, fragment, "b4.r1.c1", MarkdownDocumentTextFragmentKind.TableCell, "4"));
    }

    [Fact]
    public void GetTextReturnsContinuousSliceAcrossBlockBoundaries()
    {
        var document = new RenderedMarkdownDocument(
        [
            new MarkdownHeadingBlock(1, [new MarkdownTextInline("Title")]),
            new MarkdownParagraphBlock(
            [
                new MarkdownTextInline("Hello "),
                new MarkdownLinkInline([new MarkdownTextInline("docs")], "https://example.com/docs", null),
                new MarkdownTextInline("!")
            ]),
            new MarkdownListBlock(false,
            [
                new MarkdownListItem([new MarkdownParagraphBlock([new MarkdownTextInline("One")])]),
                new MarkdownListItem([new MarkdownParagraphBlock([new MarkdownTextInline("Two")])])
            ])
        ]);

        var textMap = MarkdownDocumentTextMap.Create(document);
        var start = textMap.Text.IndexOf("docs", StringComparison.Ordinal);
        var end = textMap.Text.IndexOf("Two", StringComparison.Ordinal) + "Two".Length;

        var selectedText = textMap.GetText(new DocumentTextRange(start, end));

        Assert.Equal("docs!\n\n• One\n• Two", selectedText);
    }

    [Fact]
    public void CreateAddsOrderedListMarkersToCanonicalText()
    {
        var document = new RenderedMarkdownDocument(
        [
            new MarkdownListBlock(true,
            [
                new MarkdownListItem([new MarkdownParagraphBlock([new MarkdownTextInline("First")])]),
                new MarkdownListItem([new MarkdownParagraphBlock([new MarkdownTextInline("Second")])])
            ])
        ]);

        var textMap = MarkdownDocumentTextMap.Create(document);

        Assert.Equal("1. First\n2. Second\n\n", textMap.Text);
        Assert.Collection(
            textMap.Fragments,
            fragment => AssertFragment(textMap.Text, fragment, "b0.i0.m", MarkdownDocumentTextFragmentKind.ListMarker, "1. "),
            fragment => AssertFragment(textMap.Text, fragment, "b0.i0.b0", MarkdownDocumentTextFragmentKind.Paragraph, "First"),
            fragment => AssertFragment(textMap.Text, fragment, "b0.i1.m", MarkdownDocumentTextFragmentKind.ListMarker, "2. "),
            fragment => AssertFragment(textMap.Text, fragment, "b0.i1.b0", MarkdownDocumentTextFragmentKind.Paragraph, "Second"));
    }

    [Theory]
    [InlineData(7, "7. One\n8. Two\n\n")]
    [InlineData(0, "0. One\n1. Two\n\n")]
    [InlineData(99, "99. One\n100. Two\n\n")]
    public void CreateNumbersOrderedListFromItsStartNumber(int startNumber, string expectedText)
    {
        var document = new RenderedMarkdownDocument(
        [
            new MarkdownListBlock(true,
            [
                new MarkdownListItem([new MarkdownParagraphBlock([new MarkdownTextInline("One")])]),
                new MarkdownListItem([new MarkdownParagraphBlock([new MarkdownTextInline("Two")])])
            ],
            startNumber)
        ]);

        var textMap = MarkdownDocumentTextMap.Create(document);

        Assert.Equal(expectedText, textMap.Text);
    }

    [Fact]
    public void CreatePutsTaskCheckboxInPlaceOfTheBulletAndKeepsBulletOfRegularItems()
    {
        var document = new RenderedMarkdownDocument(
        [
            new MarkdownListBlock(false,
            [
                new MarkdownListItem([new MarkdownParagraphBlock([new MarkdownTextInline("Done")])], IsChecked: true),
                new MarkdownListItem([new MarkdownParagraphBlock([new MarkdownTextInline("Open")])], IsChecked: false),
                new MarkdownListItem([new MarkdownParagraphBlock([new MarkdownTextInline("Plain")])])
            ])
        ]);

        var textMap = MarkdownDocumentTextMap.Create(document);

        Assert.Equal("☑ Done\n☐ Open\n• Plain\n\n", textMap.Text);
        Assert.Collection(
            textMap.Fragments,
            fragment => AssertFragment(textMap.Text, fragment, "b0.i0.t", MarkdownDocumentTextFragmentKind.TaskCheckbox, "☑ "),
            fragment => AssertFragment(textMap.Text, fragment, "b0.i0.b0", MarkdownDocumentTextFragmentKind.Paragraph, "Done"),
            fragment => AssertFragment(textMap.Text, fragment, "b0.i1.t", MarkdownDocumentTextFragmentKind.TaskCheckbox, "☐ "),
            fragment => AssertFragment(textMap.Text, fragment, "b0.i1.b0", MarkdownDocumentTextFragmentKind.Paragraph, "Open"),
            fragment => AssertFragment(textMap.Text, fragment, "b0.i2.m", MarkdownDocumentTextFragmentKind.ListMarker, "• "),
            fragment => AssertFragment(textMap.Text, fragment, "b0.i2.b0", MarkdownDocumentTextFragmentKind.Paragraph, "Plain"));
    }

    [Fact]
    public void CreateKeepsTheNumberBeforeTheTaskCheckboxInOrderedList()
    {
        var document = new RenderedMarkdownDocument(
        [
            new MarkdownListBlock(true,
            [
                new MarkdownListItem([new MarkdownParagraphBlock([new MarkdownTextInline("Done")])], IsChecked: true),
                new MarkdownListItem([new MarkdownParagraphBlock([new MarkdownTextInline("Plain")])])
            ])
        ]);

        var textMap = MarkdownDocumentTextMap.Create(document);

        Assert.Equal("1. ☑ Done\n2. Plain\n\n", textMap.Text);
        Assert.Collection(
            textMap.Fragments,
            fragment => AssertFragment(textMap.Text, fragment, "b0.i0.m", MarkdownDocumentTextFragmentKind.ListMarker, "1. "),
            fragment => AssertFragment(textMap.Text, fragment, "b0.i0.t", MarkdownDocumentTextFragmentKind.TaskCheckbox, "☑ "),
            fragment => AssertFragment(textMap.Text, fragment, "b0.i0.b0", MarkdownDocumentTextFragmentKind.Paragraph, "Done"),
            fragment => AssertFragment(textMap.Text, fragment, "b0.i1.m", MarkdownDocumentTextFragmentKind.ListMarker, "2. "),
            fragment => AssertFragment(textMap.Text, fragment, "b0.i1.b0", MarkdownDocumentTextFragmentKind.Paragraph, "Plain"));
    }

    /// <summary>
    /// Маркер по уровню списка среди предков того же вида, как <c>ul ul</c> и
    /// <c>ol ol</c> в CSS: маркированный • ◦ ▪, нумерованный из цифр 1. a. i.;
    /// глубже третий уровень повторяется.
    /// </summary>
    [Fact]
    public void CreateMarksListsByTheirLevelAmongListsOfTheSameKind()
    {
        var document = new RenderedMarkdownDocument(
        [
            Nested(isOrdered: false, depth: 4),
            Nested(isOrdered: true, depth: 4)
        ]);

        var textMap = MarkdownDocumentTextMap.Create(document);

        Assert.Equal(
            "• L1\n◦ L2\n▪ L3\n▪ L4\n\n1. L1\na. L2\ni. L3\ni. L4\n\n",
            textMap.Text);
    }

    [Fact]
    public void CreateCountsTheLevelOnlyAmongListsOfTheSameKind()
    {
        // 1. → • → a.: нумерованный внутри маркированного — второй нумерованный.
        var document = new RenderedMarkdownDocument(
        [
            new MarkdownListBlock(true,
            [
                new MarkdownListItem(
                [
                    Paragraph("Ordered"),
                    new MarkdownListBlock(false,
                    [
                        new MarkdownListItem(
                        [
                            Paragraph("Bullet"),
                            new MarkdownListBlock(true, [new MarkdownListItem([Paragraph("Inner")])])
                        ])
                    ])
                ])
            ])
        ]);

        var textMap = MarkdownDocumentTextMap.Create(document);

        Assert.Equal("1. Ordered\n• Bullet\na. Inner\n\n", textMap.Text);
    }

    /// <summary>Вид автора (MM-47) важнее уровня: «c.» остаётся буквой и в корне, и во вложенном.</summary>
    [Theory]
    [InlineData(MarkdownListNumbering.LowerAlpha, 3, "c. One\nd. Two\n\n")]
    [InlineData(MarkdownListNumbering.UpperAlpha, 26, "Z. One\nAA. Two\n\n")]
    [InlineData(MarkdownListNumbering.LowerRoman, 4, "iv. One\nv. Two\n\n")]
    [InlineData(MarkdownListNumbering.UpperRoman, 9, "IX. One\nX. Two\n\n")]
    [InlineData(MarkdownListNumbering.LowerAlpha, 0, "0. One\na. Two\n\n")]
    public void CreateNumbersListsInTheAuthorsNumbering(MarkdownListNumbering numbering, int startNumber, string expectedText)
    {
        var document = new RenderedMarkdownDocument(
        [
            new MarkdownListBlock(true, [new MarkdownListItem([Paragraph("One")]), new MarkdownListItem([Paragraph("Two")])], startNumber, Numbering: numbering)
        ]);

        Assert.Equal(expectedText, MarkdownDocumentTextMap.Create(document).Text);
    }

    [Fact]
    public void CreateKeepsTheAuthorsNumberingInANestedList()
    {
        var document = new RenderedMarkdownDocument(
        [
            new MarkdownListBlock(true,
            [
                new MarkdownListItem(
                [
                    Paragraph("Parent"),
                    new MarkdownListBlock(true, [new MarkdownListItem([Paragraph("Child")])], Numbering: MarkdownListNumbering.UpperRoman)
                ])
            ])
        ]);

        Assert.Equal("1. Parent\nI. Child\n\n", MarkdownDocumentTextMap.Create(document).Text);
    }

    [Theory]
    [InlineData(1, MarkdownListNumbering.LowerAlpha, "a")]
    [InlineData(27, MarkdownListNumbering.LowerAlpha, "aa")]
    [InlineData(52, MarkdownListNumbering.UpperAlpha, "AZ")]
    [InlineData(1994, MarkdownListNumbering.UpperRoman, "MCMXCIV")]
    [InlineData(3999, MarkdownListNumbering.LowerRoman, "mmmcmxcix")]
    [InlineData(4000, MarkdownListNumbering.LowerRoman, "4000")]
    [InlineData(0, MarkdownListNumbering.UpperRoman, "0")]
    [InlineData(-2, MarkdownListNumbering.Digits, "-2")]
    public void FormatListNumberFollowsCssListStyles(int number, MarkdownListNumbering numbering, string expected)
        => Assert.Equal(expected, MarkdownDocumentTextMap.FormatListNumber(number, numbering));

    /// <summary>Список глубиной <paramref name="depth"/>: в каждом пункте «L1», «L2»… и следующий уровень.</summary>
    private static MarkdownListBlock Nested(bool isOrdered, int depth, int level = 1)
    {
        MarkdownBlock[] blocks = level == depth
            ? [Paragraph($"L{level}")]
            : [Paragraph($"L{level}"), Nested(isOrdered, depth, level + 1)];
        return new MarkdownListBlock(isOrdered, [new MarkdownListItem(blocks)]);
    }

    private static MarkdownParagraphBlock Paragraph(string text) => new([new MarkdownTextInline(text)]);

    [Fact]
    public void CreateGivesNestedTaskItemsTheirOwnCheckboxes()
    {
        var document = new RenderedMarkdownDocument(
        [
            new MarkdownListBlock(false,
            [
                new MarkdownListItem(
                [
                    new MarkdownParagraphBlock([new MarkdownTextInline("Parent")]),
                    new MarkdownListBlock(false,
                    [
                        new MarkdownListItem([new MarkdownParagraphBlock([new MarkdownTextInline("Child")])], IsChecked: true)
                    ])
                ],
                IsChecked: false)
            ])
        ]);

        var textMap = MarkdownDocumentTextMap.Create(document);

        Assert.Equal("☐ Parent\n☑ Child\n\n", textMap.Text);
        Assert.True(textMap.TryGetFragment("b0.i0.b1.i0.t", out var nested));
        Assert.Equal("☑ ", nested.Text);
    }

    [Fact]
    public void CreateWritesAFootnoteReferenceAsTheNumberInBrackets()
    {
        var document = new RenderedMarkdownDocument(
        [
            new MarkdownParagraphBlock(
            [
                new MarkdownTextInline("Markdig"),
                new MarkdownFootnoteReferenceInline(1),
                new MarkdownTextInline(" and again"),
                new MarkdownFootnoteReferenceInline(1),
                new MarkdownTextInline(".")
            ])
        ]);

        var textMap = MarkdownDocumentTextMap.Create(document);

        Assert.Equal("Markdig[1] and again[1].\n\n", textMap.Text);
    }

    [Fact]
    public void CreateListsFootnotesWithNumbersAndKeepsEveryParagraph()
    {
        var document = new RenderedMarkdownDocument(
        [
            new MarkdownParagraphBlock([new MarkdownTextInline("Text"), new MarkdownFootnoteReferenceInline(1)]),
            new MarkdownFootnotesBlock(
            [
                new MarkdownFootnote(1, [new MarkdownParagraphBlock([new MarkdownTextInline("One")])]),
                new MarkdownFootnote(2,
                [
                    new MarkdownParagraphBlock([new MarkdownTextInline("Two")]),
                    new MarkdownParagraphBlock([new MarkdownTextInline("More")])
                ])
            ])
        ]);

        var textMap = MarkdownDocumentTextMap.Create(document);

        Assert.Equal("Text[1]\n\n1 One\n2 Two\nMore\n\n", textMap.Text);
        Assert.Collection(
            textMap.Fragments,
            fragment => AssertFragment(textMap.Text, fragment, "b0", MarkdownDocumentTextFragmentKind.Paragraph, "Text[1]"),
            fragment => AssertFragment(textMap.Text, fragment, "b1.f0.m", MarkdownDocumentTextFragmentKind.FootnoteMarker, "1 "),
            fragment => AssertFragment(textMap.Text, fragment, "b1.f0.b0", MarkdownDocumentTextFragmentKind.Paragraph, "One"),
            fragment => AssertFragment(textMap.Text, fragment, "b1.f1.m", MarkdownDocumentTextFragmentKind.FootnoteMarker, "2 "),
            fragment => AssertFragment(textMap.Text, fragment, "b1.f1.b0", MarkdownDocumentTextFragmentKind.Paragraph, "Two"),
            fragment => AssertFragment(textMap.Text, fragment, "b1.f1.b1", MarkdownDocumentTextFragmentKind.Paragraph, "More"));
    }

    [Fact]
    public void CreatePutsTheFootnotesAfterAParagraphBreak()
    {
        var document = new RenderedMarkdownDocument(
        [
            new MarkdownListBlock(false, [new MarkdownListItem([new MarkdownParagraphBlock([new MarkdownTextInline("Item")])])]),
            new MarkdownFootnotesBlock([new MarkdownFootnote(1, [new MarkdownParagraphBlock([new MarkdownTextInline("Note")])])])
        ]);

        var textMap = MarkdownDocumentTextMap.Create(document);

        Assert.Equal("• Item\n\n1 Note\n\n", textMap.Text);
    }

    [Fact]
    public void CreatePutsTheAlertTitleOnItsOwnLineBeforeTheAlertBody()
    {
        var document = new RenderedMarkdownDocument(
        [
            new MarkdownParagraphBlock([new MarkdownTextInline("Before")]),
            new MarkdownQuoteBlock([new MarkdownParagraphBlock([new MarkdownTextInline("Body")])], MarkdownAlertKind.Warning),
            new MarkdownParagraphBlock([new MarkdownTextInline("After")])
        ]);

        var textMap = MarkdownDocumentTextMap.Create(document);

        Assert.Equal("Before\n\nWarning\nBody\n\nAfter\n\n", textMap.Text);
        Assert.Collection(
            textMap.Fragments,
            fragment => AssertFragment(textMap.Text, fragment, "b0", MarkdownDocumentTextFragmentKind.Paragraph, "Before"),
            fragment => AssertFragment(textMap.Text, fragment, "b1.a", MarkdownDocumentTextFragmentKind.AlertTitle, "Warning"),
            fragment => AssertFragment(textMap.Text, fragment, "b1.b0", MarkdownDocumentTextFragmentKind.Paragraph, "Body"),
            fragment => AssertFragment(textMap.Text, fragment, "b2", MarkdownDocumentTextFragmentKind.Paragraph, "After"));
    }

    [Fact]
    public void CreateTakesTheAlertTitleFromTheCaller()
    {
        var document = new RenderedMarkdownDocument(
        [
            new MarkdownQuoteBlock([new MarkdownParagraphBlock([new MarkdownTextInline("Текст")])], MarkdownAlertKind.Note)
        ]);

        var textMap = MarkdownDocumentTextMap.Create(document, static kind => kind == MarkdownAlertKind.Note ? "Примечание" : "?");

        Assert.Equal("Примечание\nТекст\n\n", textMap.Text);
        Assert.True(textMap.TryGetFragment("b0.a", out var title));
        Assert.Equal("Примечание", title.Text);
    }

    [Fact]
    public void CreateKeepsAnAlertWithoutBodyAsItsTitle()
    {
        var document = new RenderedMarkdownDocument(
        [
            new MarkdownQuoteBlock([], MarkdownAlertKind.Tip),
            new MarkdownParagraphBlock([new MarkdownTextInline("Next")])
        ]);

        var textMap = MarkdownDocumentTextMap.Create(document);

        Assert.Equal("Tip\n\nNext\n\n", textMap.Text);
    }

    [Fact]
    public void CreateAddsNoTitleToAPlainQuote()
    {
        var document = new RenderedMarkdownDocument(
        [
            new MarkdownQuoteBlock([new MarkdownParagraphBlock([new MarkdownTextInline("Quote")])])
        ]);

        var textMap = MarkdownDocumentTextMap.Create(document);

        Assert.Equal("Quote\n\n", textMap.Text);
        Assert.DoesNotContain(textMap.Fragments, static fragment => fragment.Kind == MarkdownDocumentTextFragmentKind.AlertTitle);
    }

    [Fact]
    public void CreateKeepsTheParagraphBreakBeforeAnAlertWhoseTitleIsEmpty()
    {
        var document = new RenderedMarkdownDocument(
        [
            new MarkdownParagraphBlock([new MarkdownTextInline("Before")]),
            new MarkdownQuoteBlock([new MarkdownParagraphBlock([new MarkdownTextInline("Body")])], MarkdownAlertKind.Caution)
        ]);

        var textMap = MarkdownDocumentTextMap.Create(document, static _ => string.Empty);

        Assert.Equal("Before\n\nBody\n\n", textMap.Text);
    }

    [Theory]
    [InlineData(MarkdownAlertKind.Note, "Note")]
    [InlineData(MarkdownAlertKind.Tip, "Tip")]
    [InlineData(MarkdownAlertKind.Important, "Important")]
    [InlineData(MarkdownAlertKind.Warning, "Warning")]
    [InlineData(MarkdownAlertKind.Caution, "Caution")]
    public void DefaultAlertTitleIsTheKindAsGitHubWritesIt(MarkdownAlertKind kind, string expected)
        => Assert.Equal(expected, MarkdownDocumentTextMap.GetDefaultAlertTitle(kind));

    [Fact]
    public void ExtractPlainTextUsesLinkTextAndFallsBackToUrlWhenLabelIsMissing()
    {
        var inlines = new MarkdownInline[]
        {
            new MarkdownTextInline("Read "),
            new MarkdownLinkInline([new MarkdownTextInline("documentation")], "https://example.com/docs", null),
            new MarkdownTextInline(" or visit "),
            new MarkdownLinkInline(Array.Empty<MarkdownInline>(), "https://example.com/root", null)
        };

        var text = MarkdownDocumentTextMap.ExtractPlainText(inlines);

        Assert.Equal("Read documentation or visit https://example.com/root", text);
    }

    [Fact]
    public void ExtractPlainTextKeepsStrikethroughContentWithoutMarkers()
    {
        var inlines = new MarkdownInline[]
        {
            new MarkdownTextInline("Price "),
            new MarkdownStrikethroughInline([new MarkdownTextInline("10")]),
            new MarkdownTextInline(" 8")
        };

        var text = MarkdownDocumentTextMap.ExtractPlainText(inlines);

        Assert.Equal("Price 10 8", text);
    }

    [Fact]
    public void ExtractPlainTextUsesShortPlaceholderForDataImageWithoutAltText()
    {
        var inlines = new MarkdownInline[]
        {
            new MarkdownTextInline("Cost "),
            new MarkdownImageInline("data:image/png;base64,QUJDREVGRw==", null, null),
            new MarkdownTextInline(" now")
        };

        var text = MarkdownDocumentTextMap.ExtractPlainText(inlines);

        Assert.Equal("Cost image now", text);
        Assert.DoesNotContain("QUJD", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TryGetFragmentReturnsFalseForUnknownKey()
    {
        var textMap = MarkdownDocumentTextMap.Create(
            new RenderedMarkdownDocument(
            [
                new MarkdownParagraphBlock([new MarkdownTextInline("Only paragraph")])
            ]));

        var found = textMap.TryGetFragment("missing", out var fragment);

        Assert.False(found);
        Assert.Equal(default, fragment);
    }

    /// <summary>
    /// MM-48: front matter — отдельный тип блока, но в карте он остаётся таблицей
    /// без строки заголовка: тот же текст, те же пути и тот же вид фрагментов,
    /// чтобы выделение, поиск и копирование не изменились.
    /// </summary>
    [Fact]
    public void FrontMatterGoesIntoTheMapLikeATableWithoutAHeaderRow()
    {
        var entries = new MarkdownFrontMatterBlock(
        [
            new MarkdownFrontMatterEntry("id", "MM-48"),
            new MarkdownFrontMatterEntry("empty", string.Empty)
        ]);
        var asTable = new MarkdownTableBlock(
            [],
            [
                [
                    new MarkdownTableCell([new MarkdownStrongInline([new MarkdownTextInline("id")])]),
                    new MarkdownTableCell([new MarkdownTextInline("MM-48")])
                ],
                [
                    new MarkdownTableCell([new MarkdownStrongInline([new MarkdownTextInline("empty")])]),
                    new MarkdownTableCell([])
                ]
            ]);

        var textMap = MarkdownDocumentTextMap.Create(new RenderedMarkdownDocument([entries]));
        var before = MarkdownDocumentTextMap.Create(new RenderedMarkdownDocument([asTable]));

        Assert.Equal("id\tMM-48\nempty\t\n\n", textMap.Text);
        Assert.Equal(before.Text, textMap.Text);
        Assert.Equal(
            before.Fragments.Select(static fragment => (fragment.Key, fragment.Kind, fragment.Range)),
            textMap.Fragments.Select(static fragment => (fragment.Key, fragment.Kind, fragment.Range)));

        Assert.Collection(
            textMap.Fragments,
            fragment => AssertFragment(textMap.Text, fragment, "b0.r0.c0", MarkdownDocumentTextFragmentKind.TableCell, "id"),
            fragment => AssertFragment(textMap.Text, fragment, "b0.r0.c1", MarkdownDocumentTextFragmentKind.TableCell, "MM-48"),
            fragment => AssertFragment(textMap.Text, fragment, "b0.r1.c0", MarkdownDocumentTextFragmentKind.TableCell, "empty"));

        Assert.Equal(
            MarkdownDocumentTextMap.ExtractPlainText(asTable),
            MarkdownDocumentTextMap.ExtractPlainText(entries));
    }

    private static void AssertFragment(
        string fullText,
        MarkdownDocumentTextFragment fragment,
        string expectedKey,
        MarkdownDocumentTextFragmentKind expectedKind,
        string expectedText)
    {
        Assert.Equal(expectedKey, fragment.Key);
        Assert.Equal(expectedKind, fragment.Kind);
        Assert.Equal(expectedText, fragment.Text);
        Assert.Equal(expectedText.Length, fragment.Range.Length);
        Assert.Equal(expectedText, fullText[fragment.Range.Start..fragment.Range.End]);
    }
}
