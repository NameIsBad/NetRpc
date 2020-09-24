﻿using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NetRpc
{
    public sealed class ProxyStream : ReadStream
    {
        private readonly Stream _stream;
        private readonly long _length;
        private readonly bool _manualPosition;
        private long _p;

        public ProxyStream(Stream stream)
        {
            try
            {
                _length = stream.Length;
            }
            catch
            {
            }

            _stream = stream;
        }

        public ProxyStream(Stream stream, long length, bool manualPosition = false)
        {
            _stream = stream;
            _length = length;
            _manualPosition = manualPosition;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = new CancellationToken())
        {
            int readCount;
            try
            {
                readCount = await _stream.ReadAsync(buffer, cancellationToken);
                if (_manualPosition)
                    _p += readCount;
                await WriteCacheAsync(buffer, cancellationToken);
                await InvokeStartAsync();
                await OnProgressAsync(new SizeEventArgs(Position));
            }
            catch
            {
                await InvokeFinishAsync(new SizeEventArgs(Position));
                throw;
            }

            if (readCount == 0)
                await InvokeFinishAsync(new SizeEventArgs(Position));
            return readCount;
        }

        public override int Read(Span<byte> buffer)
        {
            int readCount;
            try
            {
                readCount = _stream.Read(buffer);
                if (_manualPosition)
                    _p += readCount;
                WriteCache(buffer);
                InvokeStartAsync().AsyncWait();
                OnProgressAsync(new SizeEventArgs(Position)).AsyncWait();
            }
            catch
            {
                InvokeFinishAsync(new SizeEventArgs(Position)).AsyncWait();
                throw;
            }

            if (readCount == 0)
                InvokeFinishAsync(new SizeEventArgs(Position)).AsyncWait();

            return readCount;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = new CancellationToken())
        {
            return _stream.WriteAsync(buffer, cancellationToken);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _stream.Write(buffer);
        }

        public override void Flush()
        {
            _stream.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int readCount;
            try
            {
                readCount = _stream.Read(buffer, offset, count);
                if (_manualPosition)
                    _p += readCount;
                WriteCache(buffer, offset, readCount);
                InvokeStartAsync().AsyncWait();
                OnProgressAsync(new SizeEventArgs(Position)).AsyncWait();
            }
            catch
            {
                InvokeFinishAsync(new SizeEventArgs(Position)).AsyncWait();
                throw;
            }

            if (readCount == 0)
                InvokeFinishAsync(new SizeEventArgs(Position)).AsyncWait();

            return readCount;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int readCount;
            try
            {
                readCount = await _stream.ReadAsync(buffer, offset, count, cancellationToken);
                if (_manualPosition)
                    _p += readCount;
                await WriteCacheAsync(buffer, offset, readCount, cancellationToken);
                await InvokeStartAsync();
                await OnProgressAsync(new SizeEventArgs(Position));
            }
            catch
            {
                await InvokeFinishAsync(new SizeEventArgs(Position));
                throw;
            }

            if (readCount == 0)
                await InvokeFinishAsync(new SizeEventArgs(Position));

            return readCount;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _stream.Write(buffer, offset, count);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return _stream.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return _stream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            _stream.SetLength(value);
        }

        public override bool CanRead => _stream.CanRead;

        public override bool CanSeek => _stream.CanSeek;

        public override bool CanWrite => _stream.CanWrite;

        // ReSharper disable once ConvertToAutoProperty
        public override long Length => _length;

        public override long Position
        {
            get
            {
                if (_manualPosition)
                    return _p; 
                return _stream.Position;
            }
            set
            {
                if (_manualPosition)
                    _p = value;
                else
                    _stream.Position = value;
            }
        }
    }
}