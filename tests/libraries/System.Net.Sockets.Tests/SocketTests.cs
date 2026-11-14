using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.System.Net.Sockets.Tests;

// TCP, UDP and name lookup over the host's sockets, with both ends in the program: NetWasm's Node.js host supplies
// them as Wasmtime does, and desktop .NET agrees.
public sealed class SocketTests
{
    [Test]
    public async Task TcpCarriesBytesBothWays()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var accept = listener.AcceptTcpClientAsync();
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
            using var server = await accept;
            using var clientStream = client.GetStream();
            using var serverStream = server.GetStream();

            await clientStream.WriteAsync(Encoding.ASCII.GetBytes("ping"));
            await serverStream.WriteAsync(Encoding.ASCII.GetBytes("pong"));
            await Assert.That(await ReadAsync(serverStream, 4)).IsEqualTo("ping");
            await Assert.That(await ReadAsync(clientStream, 4)).IsEqualTo("pong");

            client.Client.Shutdown(SocketShutdown.Send);
            await Assert.That(await serverStream.ReadAsync(new byte[1])).IsEqualTo(0);
        }
        finally
        {
            listener.Stop();
        }
    }

    // A read returns what has arrived, even when that is less than it asked for.
    [Test]
    public async Task AReadReturnsTheBytesAlreadyBuffered()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var accept = listener.AcceptTcpClientAsync();
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
            using var server = await accept;
            using var stream = client.GetStream();
            await server.GetStream().WriteAsync(Encoding.ASCII.GetBytes("hello world"));
            await Assert.That(await ReadAsync(stream, 4)).IsEqualTo("hell");

            var buffer = new byte[64];
            var read = stream.ReadAsync(buffer).AsTask();
            var finished = await Task.WhenAny(read, Task.Delay(5000));
            await Assert.That(finished == read).IsTrue();
            await Assert.That(Encoding.ASCII.GetString(buffer, 0, read.Result)).IsEqualTo("o world");
        }
        finally
        {
            listener.Stop();
        }
    }

    [Test]
    public async Task ANonBlockingReceiveReportsWouldBlockUntilDataArrives()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var accept = listener.AcceptSocketAsync();
            using var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await client.ConnectAsync(listener.LocalEndpoint);
            using var server = await accept;
            client.Blocking = false;
            var buffer = new byte[8];
            SocketError? error = null;
            try { client.Receive(buffer); } catch (SocketException e) { error = e.SocketErrorCode; }
            await Assert.That(error).IsEqualTo(SocketError.WouldBlock);

            await server.SendAsync(Encoding.ASCII.GetBytes("ready"));
            var received = 0;
            for (var attempt = 0; attempt < 500 && received == 0; attempt++)
            {
                try { received = client.Receive(buffer); }
                catch (SocketException e) when (e.SocketErrorCode == SocketError.WouldBlock) { await Task.Delay(10); }
            }
            await Assert.That(Encoding.ASCII.GetString(buffer, 0, received)).IsEqualTo("ready");
        }
        finally
        {
            listener.Stop();
        }
    }

    [Test]
    public async Task UdpCarriesADatagram()
    {
        using var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var sender = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var target = (IPEndPoint)receiver.Client.LocalEndPoint!;
        await sender.SendAsync(Encoding.ASCII.GetBytes("datagram"), target);
        var result = await receiver.ReceiveAsync();
        await Assert.That(Encoding.ASCII.GetString(result.Buffer)).IsEqualTo("datagram");
        await Assert.That(result.RemoteEndPoint.Port).IsEqualTo(((IPEndPoint)sender.Client.LocalEndPoint!).Port);
    }

    [Test]
    public async Task NamesAndLiteralsResolve()
    {
        var literal = await Dns.GetHostAddressesAsync("127.0.0.1");
        await Assert.That(literal.Single()).IsEqualTo(IPAddress.Loopback);
        var localhost = await Dns.GetHostAddressesAsync("localhost");
        await Assert.That(localhost.Length > 0 && localhost.All(IPAddress.IsLoopback)).IsTrue();
    }

    private static async Task<string> ReadAsync(NetworkStream stream, int count)
    {
        var buffer = new byte[count];
        var read = 0;
        while (read < count)
        {
            var bytes = await stream.ReadAsync(buffer.AsMemory(read));
            if (bytes == 0) break;
            read += bytes;
        }
        return Encoding.ASCII.GetString(buffer, 0, read);
    }
}
