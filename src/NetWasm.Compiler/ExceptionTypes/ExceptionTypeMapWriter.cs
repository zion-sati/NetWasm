using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace NetWasm.Compiler.ExceptionTypes;

public interface IExceptionTypeMapWriter
{
    ExceptionTypeMapArtifact Write(
        IEnumerable<ReachableExceptionType> entries,
        string buildId);
}

public sealed class ExceptionTypeMapWriter(
    IArtifactDigestCalculator digests) : IExceptionTypeMapWriter
{
    private readonly IArtifactDigestCalculator _digests = digests ??
        throw new ArgumentNullException(nameof(digests));

    public const string MediaType =
        "application/vnd.netwasm.exception-types+json;version=2";
    public const string FileNameHint = "application.netwasm.exceptions.json";

    public ExceptionTypeMapArtifact Write(
        IEnumerable<ReachableExceptionType> entries,
        string buildId)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentException.ThrowIfNullOrWhiteSpace(buildId);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(
                   stream,
                   new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 2);
            writer.WriteString("buildId", buildId);
            writer.WriteStartArray("entries");
            foreach (var entry in entries
                         .OrderBy(entry => entry.TypeId)
                         .ThenBy(entry => entry.CanonicalIdentity, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteNumber("typeId", entry.TypeId);
                writer.WriteString("canonicalIdentity", entry.CanonicalIdentity);
                writer.WriteString("displayName", entry.DisplayName);
                writer.WriteString("assemblyIdentity", entry.AssemblyIdentity);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        var bytes = stream.ToArray();
        return new(
            bytes,
            MediaType,
            FileNameHint,
            _digests.Calculate(bytes));
    }
}
