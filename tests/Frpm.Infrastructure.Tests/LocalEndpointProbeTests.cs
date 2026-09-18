using Frpm.Abstractions.Models;
using Frpm.Infrastructure.Runtime;
using System.Net;
using System.Net.Sockets;

namespace Frpm.Infrastructure.Tests;

public sealed class LocalEndpointProbeTests
{
    [Fact]
    public async Task TestAsync_ConnectsToListeningTcpEndpoint()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var result = await LocalEndpointProbe.TestAsync(
                new LocalEndpointTestRequest("127.0.0.1", port, "tcp"),
                CancellationToken.None);

            Assert.True(result.Reachable);
            Assert.True(result.Conclusive);
            Assert.Equal("127.0.0.1", result.TestedAddress);
            Assert.NotNull(result.ElapsedMilliseconds);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task TestAsync_MapsWildcardAddressToLoopback()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var result = await LocalEndpointProbe.TestAsync(
                new LocalEndpointTestRequest("0.0.0.0", port, "http"),
                CancellationToken.None);

            Assert.True(result.Reachable);
            Assert.Equal("127.0.0.1", result.TestedAddress);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task TestAsync_ReportsUdpAsInconclusive()
    {
        var result = await LocalEndpointProbe.TestAsync(
            new LocalEndpointTestRequest("127.0.0.1", 53, "udp"),
            CancellationToken.None);

        Assert.False(result.Reachable);
        Assert.False(result.Conclusive);
        Assert.Contains("UDP", result.Message);
    }

    [Fact]
    public async Task TestAsync_RejectsInvalidPortWithoutConnecting()
    {
        var result = await LocalEndpointProbe.TestAsync(
            new LocalEndpointTestRequest("127.0.0.1", 0, "tcp"),
            CancellationToken.None);

        Assert.False(result.Reachable);
        Assert.True(result.Conclusive);
        Assert.Contains("1–65535", result.Message);
    }
}
