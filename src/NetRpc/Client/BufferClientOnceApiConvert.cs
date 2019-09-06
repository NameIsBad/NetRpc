﻿using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace NetRpc
{
    internal sealed class BufferClientOnceApiConvert : IClientOnceApiConvert
    {
        private readonly IClientConnection _connection;

        private readonly BufferBlock<(byte[], BufferType)> _block =
            new BufferBlock<(byte[], BufferType)>(new DataflowBlockOptions {BoundedCapacity = Helper.StreamBufferCount});

        private BufferBlockStream _stream;

        public event EventHandler<EventArgsT<object>> ResultStream;
        public event EventHandler<EventArgsT<object>> Result;
        public event EventHandler<EventArgsT<object>> Callback;
        public event EventHandler<EventArgsT<object>> Fault;

        public BufferClientOnceApiConvert(IClientConnection connection)
        {
            _connection = connection;
            _connection.Received += ConnectionReceived;
        }

        public async Task StartAsync()
        {
            await _connection.StartAsync();
        }

        public Task SendCancelAsync()
        {
            return _connection.SendAsync(new Request(RequestType.Cancel).All);
        }

        public Task SendBufferAsync(byte[] body)
        {
            return _connection.SendAsync(new Request(RequestType.Buffer, body).All);
        }

        public Task SendBufferEndAsync()
        {
            return _connection.SendAsync(new Request(RequestType.BufferEnd).All);
        }

        public async Task<bool> SendCmdAsync(OnceCallParam callParam, MethodInfo methodInfo, Stream stream, bool isPost, CancellationToken token)
        {
            await _connection.SendAsync(new Request(RequestType.Cmd, callParam.ToBytes()).All, isPost);
            return true;
        }

        private BufferBlockStream GetRequestStream(long? length)
        {
            _stream = new BufferBlockStream(_block, length);

            void OnEnd(object sender, EventArgs e)
            {
                ((BufferBlockStream) sender).End -= OnEnd;
                Dispose();
            }

            _stream.End += OnEnd;
            return _stream;
        }

        private void ConnectionReceived(object sender, EventArgsT<byte[]> e)
        {
            var r = new Reply(e.Value);
            switch (r.Type)
            {
                case ReplyType.ResultStream:
                {
                    if (TryToObject(r.Body, out long? body))
                        OnResultStream(new EventArgsT<object>(GetRequestStream(body)));
                    else
                        OnFaultSerializationException();
                    break;
                }
                case ReplyType.CustomResult:
                {
                    if (TryToObject(r.Body, out CustomResult body))
                    {
                        if (body.HasStream)
                        {
                            var obj = body.Result.SetStream(GetRequestStream(body.StreamLength));
                            OnResultStream(new EventArgsT<object>(obj));
                        }
                        else
                        {
                            OnResult(new EventArgsT<object>(body.Result));
                            Dispose();
                        }
                    }
                    else
                        OnFaultSerializationException();

                    break;
                }
                case ReplyType.Callback:
                {
                    if (TryToObject(r.Body, out var body))
                        OnCallback(new EventArgsT<object>(body));
                    else
                        OnFaultSerializationException();
                    break;
                }
                case ReplyType.Fault:
                {
                    if (TryToObject(r.Body, out var body))
                    {
                        OnFault(new EventArgsT<object>(body));
                        Dispose();
                    }
                    else
                        OnFaultSerializationException();

                    break;
                }
                case ReplyType.Buffer:
                    _block.SendAsync((r.Body, BufferType.Buffer)).Wait();
                    break;
                case ReplyType.BufferCancel:
                    _block.SendAsync((default, BufferType.Cancel)).Wait();
                    Dispose();
                    break;
                case ReplyType.BufferFault:
                    _block.SendAsync((default, BufferType.Fault)).Wait();
                    Dispose();
                    break;
                case ReplyType.BufferEnd:
                    _block.SendAsync((r.Body, BufferType.End)).Wait();
                    Dispose();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void OnResult(EventArgsT<object> e)
        {
            Result?.Invoke(this, e);
        }

        private void OnCallback(EventArgsT<object> e)
        {
            Callback?.Invoke(this, e);
        }

        private void OnFault(EventArgsT<object> e)
        {
            Fault?.Invoke(this, e);
        }

        private void OnResultStream(EventArgsT<object> e)
        {
            ResultStream?.Invoke(this, e);
        }

        private static bool TryToObject(byte[] body, out object obj)
        {
            try
            {
                obj = body.ToObject<object>();
                return true;
            }
            catch
            {
                obj = default;
                return false;
            }
        }

        private static bool TryToObject<T>(byte[] body, out T obj)
        {
            if (TryToObject(body, out var obj2))
            {
                obj = (T) obj2;
                return true;
            }

            obj = default;
            return false;
        }

        private void OnFaultSerializationException()
        {
            OnFault(new EventArgsT<object>(new SerializationException("Deserialization failure when receive data.")));
            Dispose();
        }

        public void Dispose()
        {
            _connection?.Dispose();
        }
    }
}