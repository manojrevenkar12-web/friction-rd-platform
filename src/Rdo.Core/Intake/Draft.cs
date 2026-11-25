namespace Rdo.Core.Intake;

/// <summary>A value the model read from the request, with the exact words it read it from.</summary>
public sealed record FieldClaim(string? Value, string? Evidence)
{
    public static readonly FieldClaim None = new(null, null);
}

/// <summary>What a model proposes for a free-text test request. Code decides whether it becomes an order.</summary>
public sealed record OrderDraft(
    FieldClaim Formulation,
    FieldClaim Application,
    FieldClaim Procedure,
    FieldClaim Samples,
    FieldClaim Deadline,
    IReadOnlyList<string> Questions);

/// <summary>Reads a request and proposes a draft. Claude in the service; a simulated reader in the lab.</summary>
public interface IIntakeModel
{
    string Name { get; }

    /// <param name="feedback">Problems code found in an earlier draft of this request; empty on the first read.</param>
    Task<OrderDraft> DraftAsync(string requestText, IReadOnlyList<string> feedback, CancellationToken cancellationToken);
}
