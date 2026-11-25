using Rdo.Core.Domain;

namespace Rdo.Core.Intake;

public enum IntakeStatus
{
    Accepted,
    NeedsClarification,
    Rejected,
}

public sealed record IntakeDecision(
    IntakeStatus Status,
    TestOrder? Order,
    IReadOnlyList<string> Problems,
    IReadOnlyList<string> Questions,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Code decides whether a draft becomes an order. Every field must be backed by words that are in the
/// request and that say what the field says; the order must fit the master data. When the request does
/// not say something the bench needs, the requester is asked instead of the model guessing.
/// </summary>
public sealed class IntakeValidator(MasterData master, IntakeSettings settings)
{
    private static readonly string[] ReleaseWords = ["freigabe", "release", "homologation", "homologierung", "sop", "approval"];

    public IntakeDecision Validate(string text, OrderDraft draft, string orderId, DateTimeOffset now)
    {
        var problems = new List<string>();
        var questions = new List<string>(draft.Questions);
        var warnings = new List<string>();
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        // Formulation: the code in the evidence, the evidence in the text, and a released mix.
        Formulation? formulation = null;
        var codes = RequestText.FormulationCodes(text);
        if (draft.Formulation.Value is null)
        {
            questions.Add("Which formulation (FM number) should be tested?");
        }
        else if (!Backed(text, draft.Formulation) || !RequestText.FormulationCodes(draft.Formulation.Evidence!).Contains(draft.Formulation.Value, StringComparer.OrdinalIgnoreCase))
        {
            problems.Add($"formulation {draft.Formulation.Value} is not what the request says");
        }
        else if (!master.Formulations.TryGetValue(draft.Formulation.Value, out formulation))
        {
            problems.Add($"formulation {draft.Formulation.Value} is not in the master data");
        }
        else if (formulation.Status == FormulationStatus.Obsolete)
        {
            return Reject($"formulation {formulation.Code} is obsolete");
        }
        else if (formulation.Status == FormulationStatus.InDevelopment)
        {
            questions.Add($"{formulation.Code} is not released for testing yet. Should it be released first?");
        }

        if (codes.Count > 1)
        {
            questions.Add($"The request names {string.Join(" and ", codes)}. Which one is to be tested?");
        }

        // Application: vehicle and axle both stated in the request.
        Application? application = null;
        if (draft.Application.Value is null)
        {
            questions.Add("Which vehicle and axle is the pad for?");
        }
        else if (!master.Applications.TryGetValue(draft.Application.Value, out application))
        {
            problems.Add($"application {draft.Application.Value} is not in the master data");
        }
        else if (!Backed(text, draft.Application) || !RequestText.Contains(draft.Application.Evidence!, application.Vehicle))
        {
            problems.Add($"the request does not name the vehicle of {application.Id}");
            application = null;
        }
        else
        {
            var axle = RequestText.Axle(text);
            if (axle is null)
            {
                questions.Add($"Front or rear axle of the {application.Vehicle}?");
                application = null;
            }
            else if (!string.Equals(axle, application.Axle, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"the request says {axle} axle, the draft says {application.Axle}");
                application = null;
            }
        }

        if (formulation is not null && application is not null && !formulation.Applications.Contains(application.Id, StringComparer.OrdinalIgnoreCase))
        {
            warnings.Add($"{formulation.Code} is not specified for {application.Vehicle} {application.Axle} axle; tested there as requested");
        }

        // Procedure: named in the request; a customer's own variant when the request is for that customer.
        Procedure? procedure = null;
        if (draft.Procedure.Value is null)
        {
            questions.Add("Which test procedure should run?");
        }
        else if (!master.Procedures.TryGetValue(draft.Procedure.Value, out procedure))
        {
            problems.Add($"procedure {draft.Procedure.Value} is not in the master data");
        }
        else
        {
            var baseId = procedure.CustomerVariantOf ?? procedure.Id;
            var baseProc = master.Procedures[baseId];
            if (!Backed(text, draft.Procedure) || !baseProc.Aliases.Any(a => RequestText.Contains(draft.Procedure.Evidence!, a)))
            {
                problems.Add($"the request does not name procedure {baseProc.Name}");
                procedure = null;
            }
            else
            {
                var variant = master.Procedures.Values.FirstOrDefault(p =>
                    p.CustomerVariantOf == baseId && p.Customer is not null && RequestText.Contains(text, p.Customer));
                var expected = variant ?? baseProc;
                if (!string.Equals(expected.Id, procedure.Id, StringComparison.OrdinalIgnoreCase))
                {
                    problems.Add($"the request needs procedure {expected.Id}, the draft says {procedure.Id}");
                    procedure = null;
                }
            }
        }

        if (procedure is not null && application is not null && procedure.VehicleClass != application.VehicleClass)
        {
            problems.Add($"{procedure.Id} is for {procedure.VehicleClass} brakes, {application.Id} is {application.VehicleClass}");
        }

        // Samples: stated, or the procedure's minimum.
        var samples = procedure?.MinSamples ?? 1;
        if (draft.Samples.Value is not null)
        {
            var stated = Backed(text, draft.Samples) ? RequestText.Samples(draft.Samples.Evidence!) : null;
            if (stated is null || stated.ToString() != draft.Samples.Value)
            {
                problems.Add($"sample count {draft.Samples.Value} is not what the request says");
            }
            else
            {
                samples = stated.Value;
            }
        }

        if (procedure is not null && samples < procedure.MinSamples)
        {
            questions.Add($"{procedure.Id} needs at least {procedure.MinSamples} sample sets; the request has {samples}.");
        }

        // Deadline: parsed by code from the words the model quoted.
        DateOnly? deadline = null;
        if (draft.Deadline.Value is not null)
        {
            var parsed = Backed(text, draft.Deadline) ? RequestText.Deadline(draft.Deadline.Evidence!, today) : null;
            if (parsed is null || parsed.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) != draft.Deadline.Value)
            {
                problems.Add($"deadline {draft.Deadline.Value} is not what the request says");
            }
            else
            {
                deadline = parsed;
            }
        }
        else if (RequestText.Deadline(text, today) is not null)
        {
            problems.Add("the request gives a deadline the draft left out");
        }

        if (problems.Count > 0)
        {
            return new IntakeDecision(IntakeStatus.NeedsClarification, null, problems, [.. questions, "A coordinator should check this request; the draft did not match it."], warnings);
        }

        if (questions.Count > 0 || formulation is null || application is null || procedure is null)
        {
            return new IntakeDecision(IntakeStatus.NeedsClarification, null, problems, questions, warnings);
        }

        var release = ReleaseWords.Any(w => RequestText.Contains(text, w));
        var priority = deadline is not null && release ? Priority.Homologation : deadline is not null ? Priority.Development : Priority.Exploratory;
        DateTimeOffset? due = deadline is null ? null : new DateTimeOffset(deadline.Value.ToDateTime(new TimeOnly(16, 0)), TimeSpan.Zero);
        if (due is not null && (due.Value - now).TotalDays < settings.MinimumLeadDays)
        {
            warnings.Add($"deadline {deadline:yyyy-MM-dd} leaves less than {settings.MinimumLeadDays} days; the lab lead is told");
        }

        var order = new TestOrder(orderId, formulation.Code, application.Id, procedure.Id, samples, due, priority, now);
        return new IntakeDecision(IntakeStatus.Accepted, order, problems, questions, warnings);
    }

    private static bool Backed(string text, FieldClaim claim) => RequestText.Contains(text, claim.Evidence);

    private static IntakeDecision Reject(string why) => new(IntakeStatus.Rejected, null, [why], [], []);
}
