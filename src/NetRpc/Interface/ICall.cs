﻿using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace NetRpc
{
    internal interface ICall
    {
        Task<T> CallAsync<T>(MethodInfo methodInfo, Action<object> callback, CancellationToken token, Stream stream, params object[] args);
    }
}