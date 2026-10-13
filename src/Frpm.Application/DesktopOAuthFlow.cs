using System.Net;
using System.Net.Sockets;
using System.Text;
using Frpm.Abstractions.Models;
using Frpm.Abstractions.Services;

namespace Frpm.Application;

/// <summary>Receives a browser OAuth callback temporarily, without hosting a Web UI.</summary>
public sealed class DesktopOAuthFlow(IProviderOAuthService oauth, IProviderOAuthCallback completion)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task AuthorizeLoliaAsync(StartLoliaOAuthRequest request, Func<string, Task> openBrowser,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(request.CallbackUrl, UriKind.Absolute, out var callback)
            || callback.Scheme != "http" || callback.Host != "127.0.0.1" || callback.Port < 1)
            throw new InvalidOperationException("桌面 OAuth 回调必须使用 http://127.0.0.1:端口/oauth/lolia/callback。");
        if (!await _gate.WaitAsync(0, cancellationToken))
            throw new InvalidOperationException("已有 OAuth 授权正在进行，请先完成或取消。");

        string? state = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        using var listener = new TcpListener(IPAddress.Loopback, callback.Port);
        try
        {
            // Bind before opening the browser. This requires no HTTP URL ACL or administrator rights.
            listener.Start();
            var start = await oauth.StartLoliaAsync(request, timeout.Token);
            state = QueryValue(new Uri(start.AuthorizationUrl), "state")
                ?? throw new InvalidOperationException("OAuth 授权地址缺少状态参数。");
            await openBrowser(start.AuthorizationUrl);
            while (true)
            {
                using var client = await listener.AcceptTcpClientAsync(timeout.Token);
                using var connectionTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                connectionTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                await using var stream = client.GetStream();
                string? line;
                try { line = await ReadRequestAsync(stream, connectionTimeout.Token); }
                catch (OperationCanceledException) when (!timeout.IsCancellationRequested) { continue; }
                var parts = line?.Split(' ');
                if (parts is not { Length: 3 } || parts[0] != "GET"
                    || !Uri.TryCreate(callback.GetLeftPart(UriPartial.Authority) + parts[1], UriKind.Absolute, out var responseUri)
                    || responseUri.AbsolutePath != callback.AbsolutePath
                    || QueryValue(responseUri, "state") != state)
                {
                    await RespondAsync(stream, 400, "此请求不是当前 FRPM 授权回调。", timeout.Token);
                    continue;
                }
                if (QueryValue(responseUri, "error") is { } error)
                {
                    await RespondAsync(stream, 400, "授权未完成。请返回 FRPM 重试。", timeout.Token);
                    throw new InvalidOperationException($"OAuth 授权未完成：{error}");
                }
                var code = QueryValue(responseUri, "code");
                if (string.IsNullOrWhiteSpace(code))
                {
                    await RespondAsync(stream, 400, "授权回调缺少 code。", timeout.Token);
                    continue;
                }
                try
                {
                    await completion.CompleteLoliaAsync(code, state, timeout.Token);
                    await RespondAsync(stream, 200, "授权成功，账号已保存。可以关闭此窗口并返回 FRPM。", timeout.Token);
                    return;
                }
                catch
                {
                    await RespondAsync(stream, 400, "授权失败。请返回 FRPM 查看错误详情。", timeout.Token);
                    throw;
                }
            }
        }
        catch (SocketException ex)
        {
            throw new InvalidOperationException($"无法使用 OAuth 回调端口 {callback.Port}。请选择空闲端口，并在供应商 Client 中配置相同回调地址。", ex);
        }
        finally
        {
            listener.Stop();
            if (state is not null)
            {
                try { completion.CancelLolia(state); }
                catch (InvalidOperationException) { /* Completed authorizations have already consumed their state. */ }
            }
            _gate.Release();
        }
    }

    private static string? QueryValue(Uri uri, string name) => uri.Query.TrimStart('?').Split('&')
        .Select(part => part.Split('=', 2))
        .Where(part => part.Length == 2 && part[0] == name)
        .Select(part => Uri.UnescapeDataString(part[1].Replace('+', ' '))).FirstOrDefault();

    private static async Task<string?> ReadRequestAsync(Stream stream, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        var character = new char[1];
        var header = new StringBuilder();
        while (header.Length < 16384 && await reader.ReadAsync(character.AsMemory(), ct) > 0)
        {
            header.Append(character[0]);
            if (header.Length >= 4 && header.ToString(header.Length - 4, 4) == "\r\n\r\n")
                return header.ToString().Split("\r\n", 2)[0];
        }
        return null;
    }

    private static async Task RespondAsync(Stream stream, int status, string message, CancellationToken ct)
    {
        var body = Encoding.UTF8.GetBytes($"<!doctype html><html lang=\"zh-CN\"><meta charset=\"utf-8\"><title>FRPM 授权</title><body><h2>{WebUtility.HtmlEncode(message)}</h2></body></html>");
        var header = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {(status == 200 ? "OK" : "Bad Request")}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header, ct);
        await stream.WriteAsync(body, ct);
    }
}
