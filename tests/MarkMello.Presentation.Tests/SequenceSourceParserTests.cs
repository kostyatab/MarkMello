using System.Text;
using MarkMello.Infrastructure.Diagrams.Sequence;
using Naiad;
using Naiad.Diagrams.Sequence;

namespace MarkMello.Presentation.Tests;

public sealed class SequenceSourceParserTests
{
    [Fact]
    public void ParticipantsKeepTheirKindAndAlias()
    {
        var model = Parse(
            """
            sequenceDiagram
                actor Client as Client App
                participant Server as Remote Server (Port 8080)
                participant Db
            """);

        Assert.Collection(model.Participants,
            participant =>
            {
                Assert.Equal("Client", participant.Id);
                Assert.Equal(ParticipantType.Actor, participant.Type);
                Assert.Equal("Client App", participant.DisplayName);
            },
            participant =>
            {
                Assert.Equal("Server", participant.Id);
                Assert.Equal(ParticipantType.Participant, participant.Type);
                Assert.Equal("Remote Server (Port 8080)", participant.DisplayName);
            },
            participant =>
            {
                Assert.Equal("Db", participant.Id);
                Assert.Null(participant.Alias);
            });
    }

    [Theory]
    [InlineData("->>", MessageType.SolidArrow)]
    [InlineData("-->>", MessageType.DottedArrow)]
    [InlineData("->", MessageType.SolidOpen)]
    [InlineData("-->", MessageType.DottedOpen)]
    [InlineData("-x", MessageType.SolidCross)]
    [InlineData("--x", MessageType.DottedCross)]
    [InlineData("-)", MessageType.SolidAsync)]
    [InlineData("--)", MessageType.DottedAsync)]
    public void EveryArrowIsRecognised(string arrow, MessageType expected)
    {
        var model = Parse($"sequenceDiagram\n    Alice{arrow}Bob: Hello there");

        var message = Assert.IsType<Message>(Assert.Single(model.Elements));
        Assert.Equal(expected, message.Type);
        Assert.Equal("Alice", message.FromId);
        Assert.Equal("Bob", message.ToId);
        Assert.Equal("Hello there", message.Text);
    }

    [Fact]
    public void MessagesDeclareParticipantsInOrderOfAppearance()
    {
        var model = Parse(
            """
            sequenceDiagram
                Alice->>Bob: Hi
                Carol->>Alice: Hey
                Bob->>Bob: Think
            """);

        Assert.Equal(["Alice", "Bob", "Carol"], model.Participants.Select(static participant => participant.Id));
        var self = Assert.IsType<Message>(model.Elements[2]);
        Assert.Equal(self.FromId, self.ToId);
    }

    [Fact]
    public void ActivationShorthandAndStatementsAreKept()
    {
        var model = Parse(
            """
            sequenceDiagram
                Alice->>+Bob: Open
                activate Alice
                Bob-->>-Alice: Close
                deactivate Alice
            """);

        Assert.True(Assert.IsType<Message>(model.Elements[0]).Activate);
        Assert.True(Assert.IsType<Activation>(model.Elements[1]).IsActivate);
        Assert.True(Assert.IsType<Message>(model.Elements[2]).Deactivate);
        Assert.False(Assert.IsType<Activation>(model.Elements[3]).IsActivate);
    }

    [Fact]
    public void NotesKeepPositionAndBothParticipants()
    {
        var model = Parse(
            """
            sequenceDiagram
                participant A
                participant B
                Note over A, B: Both
                Note left of A: Left
                note right of B: Right
                Note over B: One
            """);

        var notes = model.Elements.Cast<Note>().ToList();
        Assert.Equal(NotePosition.Over, notes[0].Position);
        Assert.Equal("A", notes[0].ParticipantId);
        Assert.Equal("B", notes[0].OverParticipantId2);
        Assert.Equal("Both", notes[0].Text);
        Assert.Equal(NotePosition.LeftOf, notes[1].Position);
        Assert.Equal(NotePosition.RightOf, notes[2].Position);
        Assert.Null(notes[3].OverParticipantId2);
    }

    [Fact]
    public void NestedBlocksAreFlattenedAsNaiadDrawsThem()
    {
        var model = Parse(
            """
            sequenceDiagram
                loop Every minute
                    A->>B: Ping
                    alt Healthy
                        B-->>A: Pong
                    else Down
                        opt Retry
                            A->>B: Ping again
                        end
                    end
                end
                par First
                    A->>B: One
                and Second
                    A->>B: Two
                end
                critical Connect
                    A->>B: Open
                end
                break Failed
                    A->>B: Stop
                end
                rect rgb(200, 220, 255)
                    A->>B: Inside
                end
            """);

        Assert.Equal(
            ["Ping", "Pong", "Ping again", "One", "Two", "Open", "Stop", "Inside"],
            model.Elements.Cast<Message>().Select(static message => message.Text));
    }

    [Fact]
    public void AutonumberTitleAndCommentsAreRead()
    {
        var model = Parse(
            """
            sequenceDiagram
                title Connection handshake
                %% a comment
                autonumber

                A->>B: Hi
            """);

        Assert.True(model.AutoNumber);
        Assert.Equal("Connection handshake", model.Title);
        Assert.Single(model.Elements);
    }

    [Theory]
    [InlineData("sequenceDiagram\n    box Aqua Group\n    A->>B: Hi\n    end")]
    [InlineData("sequenceDiagram\n    create participant C\n    A->>C: Hi")]
    [InlineData("sequenceDiagram\n    A->>B Hi")]
    [InlineData("sequenceDiagram\n    autonumber 10")]
    [InlineData("sequenceDiagram\n    participant A\n    participant A")]
    [InlineData("flowchart LR\n    A --> B")]
    public void UnknownOrAmbiguousConstructsAreNotGuessed(string source)
    {
        Assert.Null(SequenceSourceParser.Parse(source));
    }

    [Theory]
    [MemberData(nameof(NaiadSources))]
    public void ModelRendersExactlyAsNaiadRendersTheSource(string source)
    {
        var model = Parse(source);
        var options = new RenderOptions();
        var document = new SequenceRenderer().Render(model, options);
        var builder = new StringBuilder();
        document.ToXml(builder);

        Assert.Equal(Mermaid.Render(source, options), builder.ToString());
    }

    public static TheoryData<string> NaiadSources() =>
    [
        "sequenceDiagram\n    Alice->>Bob: Hi\n    Bob-->>Alice: Hey",
        "sequenceDiagram\r\n    participant A as First\r\n    A->>B: CRLF\r\n",
        """
        sequenceDiagram
            title Everything at once
            autonumber
            actor U as User
            participant S as Service
            U->>+S: Request
            loop Retry
                S-)S: Work
                S--xU: Fail
            end
            Note over U, S: Spanning note
            Note left of U: Left note
            activate U
            S-->>-U: Done
            deactivate U
            S->U: Open arrow
            S-->U: Dotted open
            S--)U
        """,
    ];

    private static SequenceModel Parse(string source)
    {
        var model = SequenceSourceParser.Parse(source.Trim());
        Assert.NotNull(model);
        return model;
    }
}
