using Rdo.Core.Domain;

namespace Rdo.Core.Intake;

public sealed record IntakeResult(string RequestId, string Model, OrderDraft? Draft, IntakeDecision Decision, int ModelCalls);

/// <summary>
/// Turns a free-text test request into an order: the model drafts, the validator decides. When the
/// validator finds that the draft contradicts the request, the model gets those findings and one more
/// read; questions the request itself leaves open go to the engineer. A model failure never becomes
/// an order; the request goes back to a person with the reason.
/// </summary>
public sealed class IntakeAgent(IIntakeModel model, IntakeValidator validator)
{
    public async Task<IntakeResult> HandleAsync(string requestId, string text, DateTimeOffset now, CancellationToken ct)
    {
        OrderDraft? draft = null;
        IntakeDecision? decision = null;
        var calls = 0;
        IReadOnlyList<string> feedback = [];
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                calls++;
                draft = await model.DraftAsync(text, feedback, ct).ConfigureAwait(false);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidDataException)
            {
                var failed = new IntakeDecision(IntakeStatus.NeedsClarification, null, [$"the model could not read the request ({e.GetType().Name})"], ["A coordinator should enter this request by hand."], []);
                return new IntakeResult(requestId, model.Name, null, failed, calls);
            }

            decision = validator.Validate(text, draft, $"TO-{requestId}", now);
            if (decision.Status != IntakeStatus.NeedsClarification || decision.Problems.Count == 0)
            {
                break;
            }

            feedback = decision.Problems;
        }

        return new IntakeResult(requestId, model.Name, draft, decision!, calls);
    }
}
