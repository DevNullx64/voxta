using Voxta.Abstractions.Model;
using Microsoft.DeepDev;
using Moq;

namespace Voxta.Services.OpenAI.Tests;

public class OpenAIPromptBuilderTests
{
    private OpenAIPromptBuilder _builder = null!;

    [SetUp]
    public void Setup()
    {
        var tokenizer = new Mock<ITokenizer>();
        tokenizer.Setup(m => m.Encode(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>())).Returns(new List<int>());
        _builder = new OpenAIPromptBuilder(tokenizer.Object);
    }

    [Test]
    public void TestPromptMinimal()
    {
        var messages = _builder.BuildReplyPrompt(new ChatSessionData
        {
            UserName = "Joe",
            Character = new()
            {
                Name = "Jane",
                Description = "some-description",
                Personality = "some-personality",
                Scenario = "some-scenario",
                FirstMessage = "some-first-message",
            },
            Messages =
            {
                new ChatMessageData { User = "Joe", Text = "Hello" },
                new ChatMessageData { User = "Jane", Text = "World" },
                new ChatMessageData { User = "Joe", Text = "Question" },
            }
        }, 4096);

        var actual = string.Join("\n", messages.Select(x => $"{x.role}: {x.content}"));
        Assert.That(actual, Is.EqualTo("""
        system: Description of Jane: some-description
        Personality of Jane: some-personality
        Circumstances and context of the dialogue: some-scenario
        Only write a single reply from Jane for natural speech.
        user: Hello
        assistant: World
        user: Question
        """.ReplaceLineEndings("\n").TrimExcess()));
    }
    
    [Test]
    public void TestPromptFull()
    {
        var messages = _builder.BuildReplyPrompt(new ChatSessionData
        {
            UserName = "Joe",
            Character = new()
            {
                Name = "Jane",
                Description = "some-description",
                Personality = "some-personality",
                Scenario = "some-scenario",
                FirstMessage = "some-first-message",
                SystemPrompt = "some-system-prompt",
                PostHistoryInstructions = "some-post-history-instructions",
                MessageExamples = "Joe: Request\nJane: Response",
            },
            Messages =
            {
                new ChatMessageData { User = "Joe", Text = "Hello" },
                new ChatMessageData { User = "Jane", Text = "World" },
                new ChatMessageData { User = "Joe", Text = "Question" },
            },
            Actions = new[] { "action1", "action2" },
            Context = "some-context",
        }, 4096);

        var actual = string.Join("\n", messages.Select(x => $"{x.role}: {x.content}"));
        Assert.That(actual, Is.EqualTo("""
        system: some-system-prompt
        Description of Jane: some-description
        Personality of Jane: some-personality
        Circumstances and context of the dialogue: some-scenario
        Only write a single reply from Jane for natural speech.
        user: Hello
        assistant: World
        user: Question
        system: some-post-history-instructions
        Current context: some-context
        Available actions to be inferred after the response: action1, action2
        """.ReplaceLineEndings("\n").TrimExcess()));
    }
    
    [Test]
    public void TestPromptActionInference()
    {
        var messages = _builder.BuildActionInferencePrompt(new ChatSessionData
        {
            UserName = "Joe",
            Character = new()
            {
                Name = "Jane",
                Description = "some-description",
                Personality = "some-personality",
                Scenario = "some-scenario",
                FirstMessage = "some-first-message",
                SystemPrompt = "some-system-prompt",
                PostHistoryInstructions = "some-post-history-instructions",
                MessageExamples = "Joe: Request\nJane: Response",
            },
            Messages =
            {
                new ChatMessageData { User = "Joe", Text = "Hello" },
                new ChatMessageData { User = "Jane", Text = "World" },
                new ChatMessageData { User = "Joe", Text = "Question" },
            },
            Actions = new[] { "action1", "action2" },
            Context = "some-context",
        });

        var actual = string.Join("\n", messages.Select(x => $"{x.role}: {x.content}"));
        Assert.That(actual, Is.EqualTo("""
        system: You are tasked with inferring the best action from a list based on the content of a sample chat.

        Actions: [action1], [action2]
        user: Conversation Context:
        Jane's Personality: some-personality
        Scenario: some-scenario
        Context: some-context

        Conversation:
        Joe: Hello
        Jane: World
        Joe: Question

        Based on the last message, which of the following actions is the most applicable for Jane: [action1], [action2]

        Only write the action.
        """.ReplaceLineEndings("\n").TrimExcess()));
    }
}