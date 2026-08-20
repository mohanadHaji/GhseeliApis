using Ghseeli.IntegrationContracts.BusinessCatalog;

namespace Ghseeli.BusinessApi.Services.Availability;

public sealed class ServiceAreaEvaluationResult
{
    public bool IsValid { get; init; }
    public AppointmentValidationIssue? Error { get; init; }
    public AppointmentServiceAreaFacts Facts { get; init; } = new();
}

public sealed class AvailabilityResolutionResult
{
    public bool IsValid { get; init; }
    public AppointmentValidationIssue? Error { get; init; }
    public AppointmentAvailabilityFacts Facts { get; init; } = new();
}

public sealed class CatalogSelectionValidationResult
{
    public IReadOnlyCollection<AppointmentValidationIssue> Errors { get; init; } =
        Array.Empty<AppointmentValidationIssue>();
    public IReadOnlyCollection<NormalizedAddonSelection> NormalizedSelections { get; init; } =
        Array.Empty<NormalizedAddonSelection>();
    public decimal AddonSubtotal { get; init; }
    public int DurationAdjustmentMinutes { get; init; }
}
