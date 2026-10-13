// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace EyeGrok.Net
{
    public sealed class UnityHttpTransport : IHttpTransport
    {
        public async Task<HttpResponse> Send(HttpRequest request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var req = new UnityWebRequest(request.Url, request.Method);
            req.timeout = request.TimeoutSeconds > 0 ? request.TimeoutSeconds : 15;
            req.downloadHandler = new DownloadHandlerBuffer();
            if (!string.IsNullOrEmpty(request.Body))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(request.Body));
                if (!string.IsNullOrEmpty(request.ContentType))
                    req.SetRequestHeader("Content-Type", request.ContentType);
            }
            if (!string.IsNullOrEmpty(request.Authorization))
                req.SetRequestHeader("Authorization", request.Authorization);
            try
            {
                var op = req.SendWebRequest();
                while (!op.isDone)
                {
                    if (ct.IsCancellationRequested)
                    {
                        req.Abort();
                        ct.ThrowIfCancellationRequested();
                    }
                    await Task.Yield();
                }
                var network = req.result == UnityWebRequest.Result.ConnectionError
                    || req.result == UnityWebRequest.Result.DataProcessingError;
                var status = (int)req.responseCode;
                var body = req.downloadHandler != null ? req.downloadHandler.text : "";
                if (req.result == UnityWebRequest.Result.ProtocolError)
                    network = false;
                return new HttpResponse { Status = status, Body = body, NetworkError = network || status == 0 };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return new HttpResponse { Status = 0, Body = "", NetworkError = true };
            }
            finally
            {
                req.Dispose();
            }
        }
    }
}
