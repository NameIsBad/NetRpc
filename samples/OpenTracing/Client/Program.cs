﻿using System;
using System.IO;
using System.Threading.Tasks;
using DataContract;
using Grpc.Core;

namespace Client
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var p = NetRpc.RabbitMQ.NManager.CreateClientProxy<IService>(TestHelper.Helper.GetMQOptions());
            //var p = NManager.CreateClientProxy<IService>(new Channel("localhost", 50001, ChannelCredentials.Insecure));
            await p.Proxy.Call("msg");
            //await p.Proxy.Call("msg");

            //using (var s = File.OpenRead(@"D:\TestFile\130MB.exe"))
            //{
            //    var stream = await p.Proxy.Echo(s);
            //    MemoryStream ms = new MemoryStream();
            //    stream.CopyTo(ms);
            //}

            Console.WriteLine("--- end ---");
            Console.Read();
        }
    }
}