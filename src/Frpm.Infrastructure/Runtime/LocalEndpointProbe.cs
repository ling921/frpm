using Frpm.Abstractions.Models;
using System.Diagnostics;
using System.Net.Sockets;

namespace Frpm.Infrastructure.Runtime;

/// <summary>
/// 从 FRPM 服务端所在机器探测隧道的本地目标端点。
/// </summary>
internal static class LocalEndpointProbe
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    internal static async Task<LocalEndpointTestResult> TestAsync(
        LocalEndpointTestRequest request,
        CancellationToken cancellationToken)
    {
        var address = NormalizeAddress(request.Address);
        if (request.Port is not (> 0 and <= 65_535))
        {
            return new(false, true, address, request.Port, null, "本地端口必须在 1–65535 之间。");
        }

        if (TunnelInputRules.IsUdpType(request.TunnelType))
        {
            return new(false, false, address, request.Port, null, "UDP 没有连接握手，无法可靠确认本地服务是否可访问。");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        using var client = new TcpClient();
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await client.ConnectAsync(address, request.Port.Value, timeout.Token);
            stopwatch.Stop();
            return new(true, true, address, request.Port, stopwatch.ElapsedMilliseconds,
                $"连接成功（{stopwatch.ElapsedMilliseconds} ms）。");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(false, true, address, request.Port, null, $"连接超时（{Timeout.TotalSeconds:0} 秒）。");
        }
        catch (SocketException exception)
        {
            return new(false, true, address, request.Port, null, SocketMessage(exception));
        }
        catch (ArgumentException)
        {
            return new(false, true, address, request.Port, null, "本地地址格式无效。");
        }
    }

    private static string NormalizeAddress(string? value)
    {
        var address = value?.Trim() ?? "";
        if (address.Length == 0) return "127.0.0.1";
        if (address is "0.0.0.0" or "*") return "127.0.0.1";
        if (address is "::" or "[::]") return "::1";
        if (address.Length > 2 && address[0] == '[' && address[^1] == ']') return address[1..^1];
        return address;
    }

    private static string SocketMessage(SocketException exception) => exception.SocketErrorCode switch
    {
        SocketError.ConnectionRefused => "连接被拒绝，本地端口可能尚未监听。",
        SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain => "无法解析本地地址。",
        SocketError.NetworkUnreachable or SocketError.HostUnreachable => "本地地址不可达。",
        SocketError.TimedOut => $"连接超时（{Timeout.TotalSeconds:0} 秒）。",
        _ => $"连接失败：{exception.Message}"
    };
}
