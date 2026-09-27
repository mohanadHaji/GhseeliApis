using FluentAssertions;
using GhseeliApis.DTOs.Reviews;
using GhseeliApis.Validators.Reviews;

namespace GhseeliApis.Tests.Validators.Reviews;

/// <summary>
/// Verifies completed-booking review request validation.
/// </summary>
public sealed class BusinessReviewValidatorsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task Put_rejects_rating_outside_one_to_five(int rating)
    {
        var result = await new PutBusinessReviewRequestValidator().ValidateAsync(
            new PutBusinessReviewRequest { Rating = rating });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == "Rating");
    }

    [Fact]
    public async Task Put_rejects_comment_over_one_thousand_characters()
    {
        var result = await new PutBusinessReviewRequestValidator().ValidateAsync(
            new PutBusinessReviewRequest
            {
                Rating = 5,
                Comment = new string('x', 1001)
            });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == "Comment");
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 51)]
    public async Task Public_list_rejects_invalid_pagination(int page, int pageSize)
    {
        var result = await new GetBusinessReviewsRequestValidator().ValidateAsync(
            new GetBusinessReviewsRequest { Page = page, PageSize = pageSize });

        result.IsValid.Should().BeFalse();
    }
}
