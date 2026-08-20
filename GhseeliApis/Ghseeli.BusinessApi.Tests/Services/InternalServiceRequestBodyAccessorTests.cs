using FluentAssertions;
using Ghseeli.BusinessApi.InternalServices;
using Microsoft.AspNetCore.Http;
using System.Text;

namespace Ghseeli.BusinessApi.Tests.Services;

/// <summary>
/// Verifies bounded internal request body buffering behavior.
/// </summary>
public class InternalServiceRequestBodyAccessorTests
{
    [Fact]
    public async Task GetBodyBytesAsync_WhenContentLengthExceedsLimit_DoesNotReadTheRequestBody()
    {
        var source = new ChunkedReadStream(new string('x', 25), maxChunkSize: 4);
        var context = new DefaultHttpContext();
        context.Request.Body = source;
        context.Request.ContentLength = 25;

        var action = () => InternalServiceRequestBodyAccessor.GetBodyBytesAsync(
            context,
            new InternalServiceAuthenticationOptions
            {
                MaxRequestBodyBytes = 10
            },
            CancellationToken.None);

        await action.Should().ThrowAsync<InternalRequestBodyTooLargeException>();
        source.TotalBytesRead.Should().Be(0);
    }

    [Fact]
    public async Task GetBodyBytesAsync_WhenUnknownLengthBodyExceedsLimit_StopsAtLimitPlusOneAndRestoresPosition()
    {
        var source = new ChunkedReadStream(new string('x', 25), maxChunkSize: 3);
        var context = new DefaultHttpContext();
        context.Request.Body = source;
        context.Request.ContentType = "application/json";

        var action = () => InternalServiceRequestBodyAccessor.GetBodyBytesAsync(
            context,
            new InternalServiceAuthenticationOptions
            {
                MaxRequestBodyBytes = 10
            },
            CancellationToken.None);

        await action.Should().ThrowAsync<InternalRequestBodyTooLargeException>();
        source.TotalBytesRead.Should().Be(11);
        context.Request.Body.CanSeek.Should().BeTrue();
        context.Request.Body.Position.Should().Be(0);
    }

    private sealed class ChunkedReadStream : Stream
    {
        private readonly byte[] _bytes;
        private readonly int _maxChunkSize;
        private int _position;

        public ChunkedReadStream(string content, int maxChunkSize)
        {
            _bytes = Encoding.UTF8.GetBytes(content);
            _maxChunkSize = maxChunkSize;
        }

        public int TotalBytesRead { get; private set; }

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

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= _bytes.Length)
            {
                return 0;
            }

            var toCopy = Math.Min(Math.Min(count, _maxChunkSize), _bytes.Length - _position);
            Array.Copy(_bytes, _position, buffer, offset, toCopy);
            _position += toCopy;
            TotalBytesRead += toCopy;
            return toCopy;
        }

        public override int Read(Span<byte> buffer)
        {
            if (_position >= _bytes.Length)
            {
                return 0;
            }

            var toCopy = Math.Min(Math.Min(buffer.Length, _maxChunkSize), _bytes.Length - _position);
            _bytes.AsSpan(_position, toCopy).CopyTo(buffer);
            _position += toCopy;
            TotalBytesRead += toCopy;
            return toCopy;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Read(buffer.Span));
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Read(buffer, offset, count));
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
