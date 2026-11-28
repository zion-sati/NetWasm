// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Interop;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.Reachability;

// An export gives a JSException's JavaScript error back to JavaScript (JavaScriptConversion.ExportFailure). Only a
// program that builds a JSException holding an error has one to give back: one that reaches JSException's error
// constructor, which a failed import and an Error crossing as a value call. So the offer is rooted once the program has
// both that constructor and a JavaScript export boundary, whichever the closure reaches first. Each is a flag set once,
// so the rule is monotone, never rescans, and costs one comparison per reached method.
internal interface IJavaScriptExportFailureRooter
{
    void Root(ReachabilitySession session);
}

// Roots the offer once the program has both a JavaScript export boundary and JSException's error constructor.
internal sealed class JavaScriptExportFailureRooter(IJavaScriptConversionResolver conversions) : IJavaScriptExportFailureRooter
{
    public void Root(ReachabilitySession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!session.State.HasJavaScriptExportBoundary || !session.State.ProducesJavaScriptErrors)
        {
            return;
        }
        var offer = conversions.Resolve(JavaScriptConversion.ExportFailure);
        if (session.State.JavaScriptConversions.TryAdd(JavaScriptConversion.ExportFailure, offer))
        {
            session.EnqueueGenerated(offer);
        }
    }
}

internal interface IJavaScriptExportBoundaryRecorder
{
    void Record(ReachabilitySession session);
}

// The closure reached a JavaScript export boundary.
internal sealed class JavaScriptExportBoundaryRecorder(IJavaScriptExportFailureRooter rooter) : IJavaScriptExportBoundaryRecorder
{
    public void Record(ReachabilitySession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.State.HasJavaScriptExportBoundary)
        {
            return;
        }
        session.State.HasJavaScriptExportBoundary = true;
        rooter.Root(session);
    }
}

internal interface IJavaScriptErrorConstructorRecorder
{
    void Record(ReachabilitySession session, MethodInstanceModel method);
}

// The closure reached a method: JSException's error constructor makes the program one that builds JavaScript errors.
internal sealed class JavaScriptErrorConstructorRecorder(
    ITypeFinder types,
    IMethodRepository methods,
    IJavaScriptExportFailureRooter rooter) : IJavaScriptErrorConstructorRecorder
{
    private const string JSExceptionType = "System.Runtime.InteropServices.JavaScript.JSException";
    private static readonly string StringParameter =
        CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, isValueType: false).CanonicalName;
    private static readonly string HandleParameter = CliTypeIdentity.FromStackKind(CliValueKind.I4).CanonicalName;

    private EntityKey? _errorConstructor;
    private bool _resolved;

    public void Record(ReachabilitySession session, MethodInstanceModel method)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(method);
        if (session.State.ProducesJavaScriptErrors || !IsErrorConstructor(method.Definition))
        {
            return;
        }
        session.State.ProducesJavaScriptErrors = true;
        rooter.Root(session);
    }

    // JSException(string message, int error): its shape is checked first, so the type is looked up only for a
    // constructor that could be it.
    private bool IsErrorConstructor(MethodDefinitionModel method)
    {
        if (method.Name != ".ctor" || method.Signature.ParameterSignatureTypes is not [var message, var error] ||
            message.CanonicalName != StringParameter || error.CanonicalName != HandleParameter)
        {
            return false;
        }
        if (!_resolved)
        {
            _resolved = true;
            _errorConstructor = types.FindType(JSExceptionType).Methods
                .Select(methods.GetMethod)
                .Single(candidate => candidate.Name == ".ctor" &&
                    candidate.Signature.ParameterSignatureTypes is [var first, var second] &&
                    first.CanonicalName == StringParameter && second.CanonicalName == HandleParameter)
                .Key;
        }
        return method.Key == _errorConstructor;
    }
}
