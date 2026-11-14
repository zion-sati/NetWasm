using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.System.Net.Mail.Tests;

// SmtpClient as on Linux: plain SMTP over sockets (0.12's) and pickup-directory delivery over the file system.
public sealed class SmtpClientTests
{
    [Test]
    public async Task PickupDirectoryDeliveryWritesTheMessage()
    {
        var directory = Path.Combine(Path.GetTempPath(), "netwasm-mail-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var client = new SmtpClient
            {
                DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory,
                PickupDirectoryLocation = directory,
            };
            using var message = new MailMessage("from@example.test", "to@example.test", "Pickup", "Delivered to a directory.");
            await client.SendMailAsync(message);

            var files = Directory.GetFiles(directory, "*.eml");
            var text = files.Length == 1 ? File.ReadAllText(files[0]) : "";

            await Assert.That(files.Length).IsEqualTo(1);
            await Assert.That(text.Contains("Subject: Pickup")).IsTrue();
            await Assert.That(text.Contains("Delivered to a directory.")).IsTrue();
            await Assert.That(text.Contains("To: to@example.test")).IsTrue();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // Network delivery reaches the platform's sockets; a port nothing listens on refuses the connection.
    [Test]
    public async Task SendingToAPortNothingListensOnFails()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        using var client = new SmtpClient("127.0.0.1", port);
        using var message = new MailMessage("from@example.test", "to@example.test", "Refused", "No server.");
        Exception? failure = null;
        try
        {
            await client.SendMailAsync(message);
        }
        catch (Exception e)
        {
            failure = e;
        }

        await Assert.That(failure is SmtpException).IsTrue();
        var refused = false;
        for (var inner = failure; inner is not null; inner = inner.InnerException)
        {
            refused |= inner is SocketException { SocketErrorCode: SocketError.ConnectionRefused };
        }
        await Assert.That(refused).IsTrue();
    }

#if !NETWASM
    // The server runs in this process, which on one thread cannot answer the synchronous exchange upstream's
    // SendMailAsync ends a message with (the writer's Close); NetWasm's compiler tests send to a server outside
    // the program instead.
    [Test]
    public async Task MessagesAreSentToAnSmtpServer()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var commands = new List<string>();
        var data = new StringBuilder();
        var server = ServeAsync(listener, commands, data);
        try
        {
            // SmtpClient ends the session (QUIT) when it is disposed, so the server finishes after that.
            using (var client = new SmtpClient("127.0.0.1", port))
            using (var message = new MailMessage("from@example.test", "to@example.test", "Network", "Sent over SMTP."))
            {
                await client.SendMailAsync(message);
            }
            await server;
        }
        finally
        {
            listener.Stop();
        }

        await Assert.That(commands[0].StartsWith("EHLO ", StringComparison.Ordinal)).IsTrue();
        await Assert.That(commands.Contains("MAIL FROM:<from@example.test>")).IsTrue();
        await Assert.That(commands.Contains("RCPT TO:<to@example.test>")).IsTrue();
        await Assert.That(commands.Contains("DATA")).IsTrue();
        await Assert.That(data.ToString().Contains("Subject: Network")).IsTrue();
        await Assert.That(data.ToString().Contains("Sent over SMTP.")).IsTrue();
    }

    // A minimal SMTP server: one session, accepting every command.
    private static async Task ServeAsync(TcpListener listener, List<string> commands, StringBuilder data)
    {
        using var connection = await listener.AcceptTcpClientAsync();
        using var stream = connection.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII);
        using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
        await writer.WriteLineAsync("220 test.example ESMTP");
        while (await reader.ReadLineAsync() is { } line)
        {
            commands.Add(line);
            if (line.StartsWith("EHLO", StringComparison.Ordinal))
            {
                await writer.WriteLineAsync("250 test.example");
            }
            else if (line == "DATA")
            {
                await writer.WriteLineAsync("354 Send data");
                while (await reader.ReadLineAsync() is { } body && body != ".")
                {
                    data.AppendLine(body);
                }
                await writer.WriteLineAsync("250 Queued");
            }
            else if (line == "QUIT")
            {
                await writer.WriteLineAsync("221 Bye");
                return;
            }
            else
            {
                await writer.WriteLineAsync("250 OK");
            }
        }
    }
#endif
}
