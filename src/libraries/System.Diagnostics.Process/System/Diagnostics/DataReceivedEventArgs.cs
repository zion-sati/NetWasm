// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

namespace System.Diagnostics
{
    public delegate void DataReceivedEventHandler(object sender, DataReceivedEventArgs e);

    public class DataReceivedEventArgs : EventArgs
    {
        private readonly string? _data;

        internal DataReceivedEventArgs(string? data)
        {
            _data = data;
        }

        public string? Data
        {
            get { return _data; }
        }
    }
}
