// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

using System.Threading;
using System.Threading.Tasks;

namespace EyeGrok.Net
{
    public sealed class HttpRequest
    {
        public string Method;
        public string Url;
        public string Body;
        public string Authorization;
        public string ContentType;
        public int TimeoutSeconds;
    }

    public sealed class HttpResponse
    {
        public int Status;
        public string Body;
        public bool NetworkError;
    }

    public interface IHttpTransport
    {
        Task<HttpResponse> Send(HttpRequest request, CancellationToken ct);
    }
}
