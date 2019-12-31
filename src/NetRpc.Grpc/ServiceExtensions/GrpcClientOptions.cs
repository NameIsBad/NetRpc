﻿#if NETCOREAPP3_1
using Grpc.Net.Client;
#endif
namespace NetRpc.Grpc
{
    public class GrpcClientOptions
    {
#if NETCOREAPP3_1
        public GrpcChannelOptions ChannelOptions { get; set; }
        public string Url { get; set; }
#else
        public string Host { get; set; }
        public int Port { get; set; }
        public string PublicKey { get; set; }
        public string SslTargetName { get; set; }
#endif
    }
}