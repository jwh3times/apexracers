using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Npgsql;

namespace ApexRacers.Tests.Publication;

/// <summary>A real loopback TCP relay: cut existing and new publisher-to-PostgreSQL traffic only.</summary>
internal sealed class RehearsalDatabaseLink(
    TcpListener listener, string upstreamHost, int upstreamPort, string connectionString) : IAsyncDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly ConcurrentDictionary<int, (TcpClient Incoming, TcpClient Upstream)> connections = new();
    private readonly List<Task> pumps = [];
    private Task accepting = Task.CompletedTask;
    private readonly Lock gate = new();
    private bool partitioned;
    private int sequence;

    public string ConnectionString { get; } = connectionString;
    public int Port { get; } = ((IPEndPoint)listener.LocalEndpoint).Port;

    public static RehearsalDatabaseLink Create(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var host = builder.Host ?? throw new InvalidOperationException("Rehearsal PostgreSQL host is required.");
        var upstreamPort = builder.Port;
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        builder.Host = "127.0.0.1";
        builder.Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var link = new RehearsalDatabaseLink(listener, host, upstreamPort, builder.ConnectionString);
        link.accepting = link.AcceptAsync();
        return link;
    }

    public void Partition()
    {
        lock (gate)
        {
            partitioned = true;
            foreach (var pair in connections.Values)
            {
                pair.Incoming.Dispose();
                pair.Upstream.Dispose();
            }
        }
    }

    public void Restore()
    {
        lock (gate)
            partitioned = false;
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var incoming = await listener.AcceptTcpClientAsync(lifetime.Token);
                lock (gate)
                {
                    if (partitioned)
                        incoming.Dispose();
                    else
                        pumps.Add(PumpAsync(incoming));
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (SocketException) when (lifetime.IsCancellationRequested) { }
    }

    private async Task PumpAsync(TcpClient incoming)
    {
        using var upstream = new TcpClient();
        using (incoming)
        {
            var id = Interlocked.Increment(ref sequence);
            try
            {
                await upstream.ConnectAsync(upstreamHost, upstreamPort, lifetime.Token);
                lock (gate)
                {
                    if (partitioned)
                        return;
                    connections[id] = (incoming, upstream);
                }
                var forward = incoming.GetStream().CopyToAsync(upstream.GetStream(), lifetime.Token);
                var backward = upstream.GetStream().CopyToAsync(incoming.GetStream(), lifetime.Token);
                await Task.WhenAny(forward, backward);
                incoming.Dispose();
                upstream.Dispose();
                await Task.WhenAll(forward, backward);
            }
            catch (Exception error) when (error is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
            {
                // Normal peer close, deliberate partition or fixture shutdown. Protocol outcomes
                // are asserted through the real HTTP/database interfaces, never this relay alone.
            }
            finally
            {
                connections.TryRemove(id, out _);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync();
        listener.Stop();
        Partition();
        await accepting.WaitAsync(TimeSpan.FromSeconds(10));
        Task[] draining;
        lock (gate)
            draining = pumps.ToArray();
        await Task.WhenAll(draining).WaitAsync(TimeSpan.FromSeconds(10));
        lifetime.Dispose();
    }
}
