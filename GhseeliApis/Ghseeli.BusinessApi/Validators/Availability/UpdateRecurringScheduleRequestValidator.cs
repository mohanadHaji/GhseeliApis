using FluentValidation;
using Ghseeli.BusinessApi.DTOs.Availability;

namespace Ghseeli.BusinessApi.Validators.Availability;

public class UpdateRecurringScheduleRequestValidator
    : AbstractValidator<UpdateRecurringScheduleRequest>
{
    public UpdateRecurringScheduleRequestValidator()
    {
        Include(new CreateRecurringScheduleRequestValidator());
    }
}
