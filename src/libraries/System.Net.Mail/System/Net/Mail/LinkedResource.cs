// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

using System.IO;
using System.Net.Mime;
using System.Text;

namespace System.Net.Mail
{
    public class LinkedResource : AttachmentBase
    {
        internal LinkedResource()
        { }

        public LinkedResource(string fileName) :
            base(fileName)
        { }

        public LinkedResource(string fileName, string? mediaType) :
            base(fileName, mediaType)
        { }

        public LinkedResource(string fileName, ContentType? contentType) :
            base(fileName, contentType)
        { }

        public LinkedResource(Stream contentStream) :
            base(contentStream)
        { }

        public LinkedResource(Stream contentStream, string? mediaType) :
            base(contentStream, mediaType)
        { }

        public LinkedResource(Stream contentStream, ContentType? contentType) :
            base(contentStream, contentType)
        { }

        public Uri? ContentLink
        {
            get
            {
                return ContentLocation;
            }

            set
            {
                ContentLocation = value;
            }
        }

        public static LinkedResource CreateLinkedResourceFromString(string content)
        {
            LinkedResource a = new LinkedResource();
            a.SetContentFromString(content, null, string.Empty);
            return a;
        }

        public static LinkedResource CreateLinkedResourceFromString(string content, Encoding? contentEncoding, string? mediaType)
        {
            LinkedResource a = new LinkedResource();
            a.SetContentFromString(content, contentEncoding, mediaType);
            return a;
        }

        public static LinkedResource CreateLinkedResourceFromString(string content, ContentType? contentType)
        {
            LinkedResource a = new LinkedResource();
            a.SetContentFromString(content, contentType);
            return a;
        }
    }
}
