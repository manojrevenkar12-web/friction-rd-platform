using Rdo.Core.Domain;
using Rdo.Core.Intake;
using Xunit;

namespace Rdo.Tests;

public class IntakeTests
{
    private readonly IntakeValidator validator = new(TestData.Master, TestData.Settings.Intake);

    private static (string Text, OrderDraft Draft) Request(string? axleWords = "front axle", string? customer = null)
    {
        var (f, a) = TestData.ReleasedPassengerCar();
        var axle = axleWords is null ? string.Empty : " " + (a.Axle == "front" ? axleWords : axleWords.Replace("front", "rear", StringComparison.Ordinal));
        var release = customer is null ? string.Empty : $" We need results by week 20 for the {customer} release.";
        var text = $"Hi lab, please run AK Master on {f.Code} for the {a.Vehicle}{axle}. 2 pad sets are ready.{release} Thanks, Anna";
        var proc = customer == "OEM-B" ? "AKM-OEMB" : "AKM";
        var draft = new OrderDraft(
            new FieldClaim(f.Code, f.Code),
            new FieldClaim(a.Id, $"{a.Vehicle}{axle}"),
            new FieldClaim(proc, "AK Master"),
            new FieldClaim("2", "2 pad sets"),
            customer is null ? FieldClaim.None : new FieldClaim("2027-05-21", "week 20"),
            []);
        return (text, draft);
    }

    [Fact]
    public void A_draft_that_matches_the_request_becomes_an_order()
    {
        var (text, draft) = Request();
        var d = validator.Validate(text, draft, "TO-1", TestData.Now);
        Assert.Equal(IntakeStatus.Accepted, d.Status);
        Assert.Equal(2, d.Order!.Samples);
        Assert.Equal(Priority.Exploratory, d.Order.Priority);
    }

    [Fact]
    public void A_neighbouring_formulation_code_is_caught()
    {
        var (text, draft) = Request();
        var wrong = TestData.Master.Formulations.Keys.First(k => k != draft.Formulation.Value);
        var d = validator.Validate(text, draft with { Formulation = new FieldClaim(wrong, draft.Formulation.Evidence) }, "TO-1", TestData.Now);
        Assert.Equal(IntakeStatus.NeedsClarification, d.Status);
        Assert.Contains(d.Problems, p => p.Contains("is not what the request says", StringComparison.Ordinal));
    }

    [Fact]
    public void Evidence_that_is_not_in_the_request_is_caught()
    {
        var (text, draft) = Request();
        var d = validator.Validate(text, draft with { Application = new FieldClaim(draft.Application.Value, "some other vehicle") }, "TO-1", TestData.Now);
        Assert.Equal(IntakeStatus.NeedsClarification, d.Status);
        Assert.Null(d.Order);
    }

    [Fact]
    public void A_missing_axle_is_asked_not_guessed()
    {
        var (text, draft) = Request(axleWords: null);
        var d = validator.Validate(text, draft, "TO-1", TestData.Now);
        Assert.Equal(IntakeStatus.NeedsClarification, d.Status);
        Assert.Contains(d.Questions, q => q.StartsWith("Front or rear", StringComparison.Ordinal));
    }

    [Fact]
    public void A_customer_with_its_own_variant_gets_that_variant()
    {
        var (text, draft) = Request(customer: "OEM-B");
        var ok = validator.Validate(text, draft, "TO-1", TestData.Now);
        Assert.Equal(IntakeStatus.Accepted, ok.Status);
        Assert.Equal("AKM-OEMB", ok.Order!.Procedure);
        Assert.Equal(Priority.Homologation, ok.Order.Priority);
        var missed = validator.Validate(text, draft with { Procedure = new FieldClaim("AKM", "AK Master") }, "TO-1", TestData.Now);
        Assert.Contains(missed.Problems, p => p.Contains("needs procedure AKM-OEMB", StringComparison.Ordinal));
    }

    [Fact]
    public void A_wrong_week_conversion_is_caught()
    {
        var (text, draft) = Request(customer: "OEM-A");
        var d = validator.Validate(text, draft with { Deadline = new FieldClaim("2027-05-14", "week 20") }, "TO-1", TestData.Now);
        Assert.Contains(d.Problems, p => p.StartsWith("deadline", StringComparison.Ordinal));
    }

    [Fact]
    public void An_obsolete_formulation_is_refused()
    {
        var obsolete = TestData.Master.Formulations.Values.First(f => f.Status == FormulationStatus.Obsolete);
        var (text, draft) = Request();
        text = text.Replace(draft.Formulation.Value!, obsolete.Code, StringComparison.Ordinal);
        var d = validator.Validate(text, draft with { Formulation = new FieldClaim(obsolete.Code, obsolete.Code) }, "TO-1", TestData.Now);
        Assert.Equal(IntakeStatus.Rejected, d.Status);
    }

    private sealed class ScriptedModel(params OrderDraft[] drafts) : IIntakeModel
    {
        public List<IReadOnlyList<string>> Feedback { get; } = [];

        public string Name => "scripted";

        public Task<OrderDraft> DraftAsync(string requestText, IReadOnlyList<string> feedback, CancellationToken cancellationToken)
        {
            Feedback.Add(feedback);
            return Task.FromResult(drafts[Math.Min(Feedback.Count - 1, drafts.Length - 1)]);
        }
    }

    [Fact]
    public async Task The_agent_gives_the_model_the_findings_and_one_more_read()
    {
        var (text, good) = Request();
        var bad = good with { Samples = new FieldClaim("3", "2 pad sets") };
        var model = new ScriptedModel(bad, good);
        var r = await new IntakeAgent(model, validator).HandleAsync("r1", text, TestData.Now, CancellationToken.None);
        Assert.Equal(IntakeStatus.Accepted, r.Decision.Status);
        Assert.Equal(2, r.ModelCalls);
        Assert.Empty(model.Feedback[0]);
        Assert.Contains(model.Feedback[1], p => p.Contains("sample count", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Open_questions_go_to_the_engineer_without_a_second_read()
    {
        var (text, draft) = Request(axleWords: null);
        var model = new ScriptedModel(draft);
        var r = await new IntakeAgent(model, validator).HandleAsync("r2", text, TestData.Now, CancellationToken.None);
        Assert.Equal(IntakeStatus.NeedsClarification, r.Decision.Status);
        Assert.Equal(1, r.ModelCalls);
    }
}
