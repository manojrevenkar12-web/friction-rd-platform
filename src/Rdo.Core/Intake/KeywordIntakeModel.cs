using System.Text.RegularExpressions;
using Rdo.Core.Domain;

namespace Rdo.Core.Intake;

/// <summary>
/// A deterministic reader for demos and offline use: it finds codes, vehicles, procedure names and
/// dates by keyword. It is not a language model and makes no claim to read free text well; the
/// validator treats its drafts exactly like a model's.
/// </summary>
public sealed partial class KeywordIntakeModel(MasterData master) : IIntakeModel
{
    public string Name => "keyword-reader";

    [GeneratedRegex(@"FM[\s\-]?\d{4}", RegexOptions.IgnoreCase)]
    private static partial Regex CodeRegex();

    [GeneratedRegex(@"(?:KW|week)\s*\d{1,2}|\d{1,2}\.\d{1,2}\.\d{4}|\d{4}-\d{2}-\d{2}", RegexOptions.IgnoreCase)]
    private static partial Regex DateRegex();

    [GeneratedRegex(@"\b\d{1,2}\s*(?:Muster|pad sets?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SamplesRegex();

    public Task<OrderDraft> DraftAsync(string requestText, IReadOnlyList<string> feedback, CancellationToken cancellationToken)
    {
        var questions = new List<string>();
        var code = CodeRegex().Match(requestText);
        var formulation = code.Success ? new FieldClaim(RequestText.FormulationCodes(code.Value)[0], code.Value) : FieldClaim.None;

        var vehicle = master.Applications.Values.Select(a => a.Vehicle).Distinct()
            .FirstOrDefault(v => RequestText.Contains(requestText, v));
        var axle = RequestText.Axle(requestText);
        var app = vehicle is null || axle is null ? null : master.Applications.Values.FirstOrDefault(a => a.Vehicle == vehicle && a.Axle == axle);
        var application = app is null ? FieldClaim.None : new FieldClaim(app.Id, vehicle);

        var proc = master.Procedures.Values.Where(p => p.CustomerVariantOf is null)
            .Select(p => (p, alias: p.Aliases.FirstOrDefault(a => RequestText.Contains(requestText, a))))
            .Where(x => x.alias is not null && (app is null || x.p.VehicleClass == app.VehicleClass))
            .FirstOrDefault();
        var procedure = FieldClaim.None;
        if (proc.p is not null)
        {
            var variant = master.Procedures.Values.FirstOrDefault(p => p.CustomerVariantOf == proc.p.Id && p.Customer is not null && RequestText.Contains(requestText, p.Customer));
            procedure = new FieldClaim((variant ?? proc.p).Id, proc.alias);
        }

        var s = SamplesRegex().Match(requestText);
        var samples = s.Success ? new FieldClaim(RequestText.Samples(s.Value)?.ToString(System.Globalization.CultureInfo.InvariantCulture), s.Value) : FieldClaim.None;
        var d = DateRegex().Match(requestText);
        var parsed = d.Success ? RequestText.Deadline(d.Value, DateOnly.FromDateTime(DateTime.UtcNow)) : null;
        var deadline = parsed is null ? FieldClaim.None : new FieldClaim(parsed.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), d.Value);
        return Task.FromResult(new OrderDraft(formulation, application, procedure, samples, deadline, questions));
    }
}
