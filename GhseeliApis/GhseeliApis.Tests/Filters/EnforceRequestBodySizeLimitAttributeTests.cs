using FluentAssertions;
using GhseeliApis.Filters;
using GhseeliApis.Services.Checkout;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using System.Text;

namespace GhseeliApis.Tests.Filters;

/// <summary>
/// Verifies request-size enforcement is scoped to buffering the request body.
/// </summary>
public class EnforceRequestBodySizeLimitAttributeTests
{
    [Fact]
    public async Task OnResourceExecutionAsync_WhenDownstreamThrowsIOException_DoesNotRewriteItAsPayloadTooLarge()
    {
        var context = CreateContext("{}", acceptLanguage: "ar");
        var filter = new EnforceRequestBodySizeLimitAttribute(
            65_536,
            CheckoutPricingProblemCodes.RequestBodyTooLarge);

        var action = () => filter.OnResourceExecutionAsync(
            context,
            () => throw new IOException("downstream failure"));

        await action.Should().ThrowAsync<IOException>()
            .WithMessage("downstream failure");
        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task OnResourceExecutionAsync_WhenBufferedBodyExceedsLimit_ReturnsLocalizedProblemDetails()
    {
        var context = CreateContext(new string('x', 65_538), acceptLanguage: "he");
        var filter = new EnforceRequestBodySizeLimitAttribute(
            65_536,
            CheckoutPricingProblemCodes.RequestBodyTooLarge);

        await filter.OnResourceExecutionAsync(
            context,
            () => throw new InvalidOperationException("The downstream pipeline must not run."));

        var result = context.Result.Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(StatusCodes.Status413PayloadTooLarge);
        result.ContentTypes.Should().Contain("application/problem+json");
        var problem = result.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Status.Should().Be(StatusCodes.Status413PayloadTooLarge);
        problem.Extensions["code"].Should().Be(CheckoutPricingProblemCodes.RequestBodyTooLarge);
        problem.Extensions["language"].Should().Be("he");
        problem.Title.Should().Be("גוף הבקשה גדול מדי.");
    }

    private static ResourceExecutingContext CreateContext(string body, string acceptLanguage)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Body = new NonSeekableReadStream(Encoding.UTF8.GetBytes(body));
        httpContext.Request.Headers.AcceptLanguage = acceptLanguage;
        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor());

        return new ResourceExecutingContext(
            actionContext,
            [],
            []);
    }

    private sealed class NonSeekableReadStream(byte[] content) : Stream
    {
        private readonly MemoryStream _inner = new(content);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            _inner.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
