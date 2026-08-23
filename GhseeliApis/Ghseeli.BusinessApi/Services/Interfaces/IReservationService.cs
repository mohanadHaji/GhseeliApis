using Ghseeli.IntegrationContracts.BusinessCatalog;

namespace Ghseeli.BusinessApi.Services.Interfaces;

public interface IReservationService
{
    Task<CreateReservationResponse> CreateAsync(
        CreateReservationRequest request,
        CancellationToken cancellationToken);
}

public sealed class ReservationRejectedException : Exception
{
    public ReservationRejectedException(
        string code,
        string message,
        IReadOnlyCollection<AppointmentValidationIssue>? errors = null)
        : base(message)
    {
        Code = code;
        Errors = errors ?? Array.Empty<AppointmentValidationIssue>();
    }

    public string Code { get; }
    public IReadOnlyCollection<AppointmentValidationIssue> Errors { get; }
}
