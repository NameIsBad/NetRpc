﻿using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NetRpc.Contract;
using RabbitMQ.Base;
using RabbitMQ.Client;

namespace NetRpc.RabbitMQ
{
    public class RabbitMQClientConnection : IClientConnection
    {
        private readonly MQOptions _opt;
        private readonly RabbitMQOnceCall _call;

        public RabbitMQClientConnection(IConnection connect, MQOptions opt, ILogger logger)
        {
            _opt = opt;
            _call = new RabbitMQOnceCall(connect, opt.RpcQueue, logger);
            _call.ReceivedAsync += CallReceived;
        }

        private async Task CallReceived(object sender, global::RabbitMQ.Base.EventArgsT<ReadOnlyMemory<byte>?> e)
        {
            if (e.Value == null)
                await OnReceivedAsync(new EventArgsT<ReadOnlyMemory<byte>>(NullReply.All));
            else
                await OnReceivedAsync(new EventArgsT<ReadOnlyMemory<byte>>(e.Value.Value));
        }

        public void Dispose()
        {
            _call.Dispose();
        }

#if NETSTANDARD2_1 || NETCOREAPP3_1
        public ValueTask DisposeAsync()
        {
            Dispose();
            return new ValueTask();
        }
#endif

        public ConnectionInfo ConnectionInfo => new ConnectionInfo
        {
            Host = _opt.Host,
            Port = _opt.Port,
            Description = _opt.ToString(),
            ChannelType = ChannelType.RabbitMQ
        };

        public event AsyncEventHandler<EventArgsT<ReadOnlyMemory<byte>>>? ReceivedAsync;

        public event EventHandler<EventArgsT<Exception>>? ReceiveDisconnected;

        public Task SendAsync(ReadOnlyMemory<byte> buffer, bool isEnd = false, bool isPost = false)
        {
            return _call.SendAsync(buffer, isPost);
        }

        public async Task StartAsync(string? authorizationToken)
        {
            await _call.CreateChannelAsync();
        }

        private Task OnReceivedAsync(EventArgsT<ReadOnlyMemory<byte>> e)
        {
            return ReceivedAsync.InvokeAsync(this, e);
        }
    }
}