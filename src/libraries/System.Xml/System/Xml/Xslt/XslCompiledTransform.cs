// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// NetWasm: dotnet/runtime 811225a4's XslCompiledTransform compiles the stylesheet to IL, which NetWasm cannot
// generate, so every Load and Transform overload validates its arguments as upstream does and then throws
// PlatformNotSupportedException (classified intentionally-unsupported in the API policy). The constructors
// and OutputSettings are upstream's; no stylesheet ever loads, so OutputSettings stays null. Interpreted
// XSLT is available through XslTransform.

using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Xml.XPath;

namespace System.Xml.Xsl
{
    [RequiresDynamicCode("XslCompiledTransform requires dynamic code because it generates IL at runtime.")]
    public sealed class XslCompiledTransform
    {
        public XslCompiledTransform() { }

        public XslCompiledTransform(bool enableDebug) { }

        /// <summary>
        /// Writer settings specified in the stylesheet
        /// </summary>
        public XmlWriterSettings? OutputSettings { get; private set; }

        //------------------------------------------------
        // Load methods
        //------------------------------------------------

        public void Load(XmlReader stylesheet)
        {
            ArgumentNullException.ThrowIfNull(stylesheet);
            throw CompiledTransformUnsupported();
        }

        public void Load(XmlReader stylesheet, XsltSettings? settings, XmlResolver? stylesheetResolver)
        {
            ArgumentNullException.ThrowIfNull(stylesheet);
            throw CompiledTransformUnsupported();
        }

        public void Load(IXPathNavigable stylesheet)
        {
            ArgumentNullException.ThrowIfNull(stylesheet);
            throw CompiledTransformUnsupported();
        }

        public void Load(IXPathNavigable stylesheet, XsltSettings? settings, XmlResolver? stylesheetResolver)
        {
            ArgumentNullException.ThrowIfNull(stylesheet);
            throw CompiledTransformUnsupported();
        }

        public void Load(string stylesheetUri)
        {
            ArgumentNullException.ThrowIfNull(stylesheetUri);
            throw CompiledTransformUnsupported();
        }

        public void Load(string stylesheetUri, XsltSettings? settings, XmlResolver? stylesheetResolver)
        {
            ArgumentNullException.ThrowIfNull(stylesheetUri);
            throw CompiledTransformUnsupported();
        }

        [RequiresUnreferencedCode("This method will get fields and types from the assembly of the passed in compiledStylesheet and call their constructors which cannot be statically analyzed")]
        public void Load(Type compiledStylesheet)
        {
            ArgumentNullException.ThrowIfNull(compiledStylesheet);
            throw CompiledTransformUnsupported();
        }

        [RequiresUnreferencedCode("This method will call into constructors of the earlyBoundTypes array which cannot be statically analyzed.")]
        public void Load(MethodInfo executeMethod, byte[] queryData, Type[]? earlyBoundTypes)
        {
            ArgumentNullException.ThrowIfNull(executeMethod);
            ArgumentNullException.ThrowIfNull(queryData);
            throw CompiledTransformUnsupported();
        }

        //------------------------------------------------
        // Transform methods which take an IXPathNavigable
        //------------------------------------------------

        public void Transform(IXPathNavigable input, XmlWriter results)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(results);
            throw CompiledTransformUnsupported();
        }

        public void Transform(IXPathNavigable input, XsltArgumentList? arguments, XmlWriter results)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(results);
            throw CompiledTransformUnsupported();
        }

        public void Transform(IXPathNavigable input, XsltArgumentList? arguments, TextWriter results)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(results);
            throw CompiledTransformUnsupported();
        }

        public void Transform(IXPathNavigable input, XsltArgumentList? arguments, Stream results)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(results);
            throw CompiledTransformUnsupported();
        }

        //------------------------------------------------
        // Transform methods which take an XmlReader
        //------------------------------------------------

        public void Transform(XmlReader input, XmlWriter results)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(results);
            throw CompiledTransformUnsupported();
        }

        public void Transform(XmlReader input, XsltArgumentList? arguments, XmlWriter results)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(results);
            throw CompiledTransformUnsupported();
        }

        public void Transform(XmlReader input, XsltArgumentList? arguments, TextWriter results)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(results);
            throw CompiledTransformUnsupported();
        }

        public void Transform(XmlReader input, XsltArgumentList? arguments, Stream results)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(results);
            throw CompiledTransformUnsupported();
        }

        //------------------------------------------------
        // Transform methods which take a uri
        //------------------------------------------------

        public void Transform(string inputUri, XmlWriter results)
        {
            ArgumentNullException.ThrowIfNull(inputUri);
            ArgumentNullException.ThrowIfNull(results);
            throw CompiledTransformUnsupported();
        }

        public void Transform(string inputUri, XsltArgumentList? arguments, XmlWriter results)
        {
            ArgumentNullException.ThrowIfNull(inputUri);
            ArgumentNullException.ThrowIfNull(results);
            throw CompiledTransformUnsupported();
        }

        public void Transform(string inputUri, XsltArgumentList? arguments, TextWriter results)
        {
            ArgumentNullException.ThrowIfNull(inputUri);
            ArgumentNullException.ThrowIfNull(results);
            throw CompiledTransformUnsupported();
        }

        public void Transform(string inputUri, XsltArgumentList? arguments, Stream results)
        {
            ArgumentNullException.ThrowIfNull(inputUri);
            ArgumentNullException.ThrowIfNull(results);
            throw CompiledTransformUnsupported();
        }

        public void Transform(string inputUri, string resultsFile)
        {
            ArgumentNullException.ThrowIfNull(inputUri);
            ArgumentNullException.ThrowIfNull(resultsFile);
            throw CompiledTransformUnsupported();
        }

        //------------------------------------------------
        // Main Transform overloads
        //------------------------------------------------

        public void Transform(XmlReader input, XsltArgumentList? arguments, XmlWriter results, XmlResolver? documentResolver)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(results);
            throw CompiledTransformUnsupported();
        }

        public void Transform(IXPathNavigable input, XsltArgumentList? arguments, XmlWriter results, XmlResolver? documentResolver)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(results);
            throw CompiledTransformUnsupported();
        }

        private static PlatformNotSupportedException CompiledTransformUnsupported() =>
            new PlatformNotSupportedException(SR.NetWasm_XslCompiledTransformUnsupported);
    }
}
