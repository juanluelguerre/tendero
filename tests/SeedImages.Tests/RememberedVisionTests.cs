using ElGuerre.Tendero.SeedImages;
using Xunit;

namespace ElGuerre.Tendero.SeedImages.Tests;

/// <summary>
/// The answers a vision model already gave, remembered (phase 13a): the board
/// asks some forty questions of every picture of the catalogue, and a picture
/// that has not changed, asked a question that has not changed, by a model that
/// has not changed, is answered what it was answered — it is asked at
/// temperature zero with a fixed seed. Adding twenty-seven pictures should cost
/// twenty-seven pictures.
///
/// **What makes an answer the same answer is everything that was sent**: the
/// model, the kind of question, its words, its schema and every picture's
/// bytes, in their order. Nothing is remembered by a name that could go stale.
/// </summary>
public sealed class RememberedVisionTests
{
    private static readonly byte[] Lamp = [1, 2, 3];
    private static readonly byte[] Stool = [4, 5, 6];

    /// <summary>Answers by counting, so two calls never look alike by accident, and says whether it was reached for.</summary>
    private sealed class Counting(string name = "model") : IVisionModel
    {
        public int Calls { get; private set; }
        public int Reached { get; private set; }

        public string Name => name;

        public void EnsureReachable() => Reached++;

        public string Ask(IReadOnlyList<byte[]> pngs, string question, string schema) => $"ask-{++Calls}";

        public string Describe(byte[] png) => $"describe-{++Calls}";

        public string Answer(byte[] png, string question) => $"answer-{++Calls}";
    }

    [Fact]
    public void The_same_question_about_the_same_picture_is_asked_once()
    {
        var model = new Counting();
        var remembering = new RememberedVision(model, new Dictionary<string, string>());

        var first = remembering.Ask([Lamp], "Is there text?", "{}");
        var again = remembering.Ask([Lamp], "Is there text?", "{}");

        Assert.Equal((first, 1), (again, model.Calls));
        Assert.Equal((1, 1), (remembering.Asked, remembering.Remembered));
    }

    /// <summary>Each of these is a different question to a model, and each is asked.</summary>
    [Fact]
    public void Another_picture_another_question_another_schema_or_another_kind_of_question_is_asked()
    {
        var model = new Counting();
        var remembering = new RememberedVision(model, new Dictionary<string, string>());

        remembering.Ask([Lamp], "Is there text?", "{}");
        remembering.Ask([Stool], "Is there text?", "{}");
        remembering.Ask([Lamp], "Is there a mark?", "{}");
        remembering.Ask([Lamp], "Is there text?", """{"type":"object"}""");
        remembering.Answer(Lamp, "Is there text?");
        remembering.Describe(Lamp);

        Assert.Equal((6, 0), (model.Calls, remembering.Remembered));
    }

    /// <summary>"The first" and "the second" in a question mean the order they were sent in.</summary>
    [Fact]
    public void The_same_two_pictures_in_the_other_order_are_another_question()
    {
        var model = new Counting();
        var remembering = new RememberedVision(model, new Dictionary<string, string>());

        remembering.Ask([Lamp, Stool], "Same style?", "{}");
        remembering.Ask([Stool, Lamp], "Same style?", "{}");

        Assert.Equal(2, model.Calls);
    }

    /// <summary>Where one part ends and the next begins is part of what was asked.</summary>
    [Fact]
    public void Words_split_differently_between_the_question_and_its_schema_are_not_the_same_question()
    {
        var model = new Counting();
        var remembering = new RememberedVision(model, new Dictionary<string, string>());

        remembering.Ask([Lamp], "ab", "c");
        remembering.Ask([Lamp], "a", "bc");
        remembering.Ask([[1, 2], [3]], "q", "{}");
        remembering.Ask([[1], [2, 3]], "q", "{}");

        Assert.Equal(4, model.Calls);
    }

    [Fact]
    public void What_another_model_answered_is_not_this_models_answer()
    {
        var first = new RememberedVision(new Counting("qwen-4b"), new Dictionary<string, string>());
        first.Ask([Lamp], "Is there text?", "{}");

        var other = new Counting("qwen-9b");
        var second = new RememberedVision(other, first.Used);
        second.Ask([Lamp], "Is there text?", "{}");

        Assert.Equal((1, 0), (other.Calls, second.Remembered));
    }

    /// <summary>
    /// The point of it: a run whose every question was answered before needs no
    /// model at all — not asked, and not even looked for.
    /// </summary>
    [Fact]
    public void With_everything_remembered_the_model_is_neither_asked_nor_reached_for()
    {
        var before = new RememberedVision(new Counting(), new Dictionary<string, string>());
        var said = (before.Ask([Lamp], "Is there text?", "{}"), before.Answer(Lamp, "What colour?"), before.Describe(Lamp));

        var model = new Counting();
        var replay = new RememberedVision(model, before.Used);
        replay.EnsureReachable();

        Assert.Equal(said, (replay.Ask([Lamp], "Is there text?", "{}"), replay.Answer(Lamp, "What colour?"), replay.Describe(Lamp)));
        Assert.Equal((0, 0, 3), (model.Calls, model.Reached, replay.Remembered));
    }

    /// <summary>And the first question nothing remembers finds out whether the model is there, once, before it is asked.</summary>
    [Fact]
    public void The_model_is_reached_for_once_before_the_first_question_it_must_answer()
    {
        var model = new Counting();
        var remembering = new RememberedVision(model, new Dictionary<string, string>());
        remembering.EnsureReachable();
        Assert.Equal(0, model.Reached);

        remembering.Ask([Lamp], "Is there text?", "{}");
        remembering.Ask([Stool], "Is there text?", "{}");

        Assert.Equal(1, model.Reached);
    }

    /// <summary>What a run used is what is kept: an answer about a picture that is gone, or to a question reworded, is not carried forever.</summary>
    [Fact]
    public void Only_what_this_run_used_is_kept()
    {
        var before = new RememberedVision(new Counting(), new Dictionary<string, string>());
        before.Ask([Lamp], "Is there text?", "{}");
        before.Ask([Stool], "Is there text?", "{}");

        var now = new RememberedVision(new Counting(), before.Used);
        now.Ask([Lamp], "Is there text?", "{}");
        now.Ask([Lamp], "Is there a mark?", "{}");

        Assert.Equal(2, now.Used.Count);
        Assert.Single(now.Used.Keys.Intersect(before.Used.Keys));
    }

    [Fact]
    public void The_memory_is_written_one_answer_a_line_in_the_same_order_every_time_and_read_back()
    {
        var run = new RememberedVision(new Counting(), new Dictionary<string, string>());
        run.Ask([Stool], "Is there text?", "{}");
        run.Ask([Lamp], "Is there \"text\"?\nAnswer.", "{}");
        run.Describe(Lamp);
        var file = Path.Combine(Path.GetTempPath(), $"answers-{Guid.NewGuid():N}.jsonl");

        try
        {
            AnswerMemory.Write(file, run.Used);
            var written = File.ReadAllText(file);
            AnswerMemory.Write(file, run.Used.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value));

            Assert.Equal(written, File.ReadAllText(file));
            Assert.Equal(3, written.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
            Assert.DoesNotContain('\r', written);
            Assert.Equal(run.Used.OrderBy(pair => pair.Key), AnswerMemory.Read(file).OrderBy(pair => pair.Key));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void A_memory_that_was_never_written_is_an_empty_one()
    {
        Assert.Empty(AnswerMemory.Read(Path.Combine(Path.GetTempPath(), $"never-{Guid.NewGuid():N}.jsonl")));
    }
}
