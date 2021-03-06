﻿using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NetRpc.Contract;
using Polly;

namespace NetRpc
{
    internal sealed class ClientMethodRetryInvoker : IMethodInvoker
    {
        private readonly CallFactory _callFactory;
        private readonly ClientRetryAttribute? _parentRetryAttribute;
        private readonly ILogger _logger;

        public ClientMethodRetryInvoker(CallFactory callFactory, ClientRetryAttribute? parentRetryAttribute, ILogger logger)
        {
            _callFactory = callFactory;
            _parentRetryAttribute = parentRetryAttribute;
            _logger = logger;
        }

        public object? Invoke(MethodInfo targetMethod, object?[] args)
        {
            try
            {
                return CallAsync(targetMethod, args).Result;
            }
            catch (AggregateException e)
            {
                if (e.InnerException != null)
                {
                    var edi = ExceptionDispatchInfo.Capture(e.InnerException);
                    edi.Throw();
                }

                throw;
            }
        }

        public async Task InvokeAsync(MethodInfo targetMethod, object?[] args)
        {
            await CallAsync(targetMethod, args);
        }

        public async Task<T> InvokeAsyncT<T>(MethodInfo targetMethod, object?[] args)
        {
            var ret = await CallAsync(targetMethod, args);
            if (ret == null)
                return default!;
            return (T) ret;
        }

        private async Task<object?> CallAsync(MethodInfo targetMethod, object?[] args)
        {
            var (callback, token, stream, otherArgs) = GetArgs(args);
            token.ThrowIfCancellationRequested();

            var retryInfo = GetRetryInfo(targetMethod);

            //single call
            if (retryInfo == null)
            {
                var call = _callFactory.Create();
                return await call.CallAsync(targetMethod, false, callback, token, stream, otherArgs);
            }

            var proxyStream = WrapProxyStream(stream);

            //retry call
            var p = Policy
                .Handle<Exception>(e => retryInfo.ExceptionTypes.Any(i => i.IsInstanceOfType(e)))
                .WaitAndRetryAsync(retryInfo.Durations,
                    (exception, span, context) => 
                        _logger.LogWarning(exception, $"{context["name"]}, retry count:{context["count"]}, wait ms:{span.TotalMilliseconds}"));

            return await p.ExecuteAsync(async (context, t) =>
            {
                context["name"] = targetMethod.ToFullMethodName();
                bool isRetry = AddCount(context);
                if (isRetry) 
                    proxyStream?.Reset();
                var call = _callFactory.Create();
                return await call.CallAsync(targetMethod, isRetry, callback, t, proxyStream, otherArgs);
            }, new Context("call"), token);
        }

        private static bool AddCount(Context c)
        {
            if (!c.Contains("count"))
                c["count"] = 1;
            else
                c["count"] = (int) c["count"] + 1;
            
            return (int)c["count"] > 1;
        }

        private RetryInfo? GetRetryInfo(MethodInfo targetMethod)
        {
            var retry = targetMethod.GetCustomAttribute<ClientRetryAttribute>();
            if (retry != null)
                return GetRetryInfo(retry);

            if (_parentRetryAttribute != null)
                return GetRetryInfo(_parentRetryAttribute);

            return default;
        }

        private static RetryInfo GetRetryInfo(ClientRetryAttribute attribute)
        {
            var durations = attribute.SleepDurations.ToList().ConvertAll(i => TimeSpan.FromMilliseconds(i)).ToArray();
            return new RetryInfo {ExceptionTypes = attribute.ExceptionTypes, Durations = durations};
        }

        private static (Func<object?, Task>? callback, CancellationToken token, Stream? stream, object?[] otherArgs) GetArgs(object?[] args)
        {
            var objs = args.ToList();

            //callback
            Func<object?, Task>? retCallback = null;
            var found = objs.FirstOrDefault(i =>
                i != null &&
                i.GetType().IsFuncT());
            if (found != null)
            {
                retCallback = FuncHelper.ConvertFunc(found);
                objs.Remove(found);
            }

            //token
            var retToken = CancellationToken.None;
            found = objs.FirstOrDefault(i => i is CancellationToken);
            if (found != null)
            {
                retToken = (CancellationToken) found;
                objs.Remove(found);
            }

            //stream
            Stream? retStream = null;
            found = objs.FirstOrDefault(i => i is Stream);
            if (found != null)
            {
                retStream = (Stream) found;
                objs.Remove(found);
            }

            //otherArgs
            return (retCallback, retToken, retStream, objs.ToArray());
        }

        private static ProxyStream? WrapProxyStream(Stream? stream)
        {
            if (stream == null)
                return null;

            if (stream is ProxyStream ps)
            {
                ps.TryAttachCache();
                return ps;
            }

            ProxyStream proxyStream = new (stream);
            proxyStream.TryAttachCache();
            return proxyStream;
        }

        private class RetryInfo
        {
            public TimeSpan[] Durations { get; init; } = null!;

            public Type[] ExceptionTypes { get; init; } = null!;
        }
    }
}