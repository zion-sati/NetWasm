// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace System.ComponentModel.Design
{
    /// <summary>
    /// Represents a verb that can be executed by a component's designer.
    /// </summary>
    public partial class DesignerVerb : MenuCommand
    {
        /// <summary>
        /// Initializes a new instance of the <see cref='System.ComponentModel.Design.DesignerVerb'/> class.
        /// </summary>
        public DesignerVerb(string text, EventHandler handler) : this(text, handler, StandardCommands.VerbFirst) { }

        /// <summary>
        /// Initializes a new instance of the <see cref='System.ComponentModel.Design.DesignerVerb'/>
        /// class.
        /// </summary>
        public DesignerVerb(string text, EventHandler handler, CommandID startCommandID) : base(handler, startCommandID)
        {
            Properties["Text"] = text == null ? null : RemoveMnemonicParameters(text);
        }

        // Equivalent to upstream's fixed \(\&.\) pattern. Keep Regex outside CoreLib.
        private static string RemoveMnemonicParameters(string text)
        {
            var result = new StringBuilder(text.Length);
            for (var i = 0; i < text.Length;)
            {
                if (i + 3 < text.Length && text[i] == '(' && text[i + 1] == '&'
                    && text[i + 2] != '\n' && text[i + 3] == ')')
                {
                    i += 4;
                }
                else
                {
                    result.Append(text[i++]);
                }
            }
            return result.ToString();
        }

        /// <summary>
        /// Gets or sets the description of the menu item for the verb.
        /// </summary>
        public string Description
        {
            get
            {
                object? result = Properties["Description"];
                if (result == null)
                {
                    return string.Empty;
                }

                return (string)result;
            }
            set => Properties["Description"] = value;
        }

        /// <summary>
        /// Gets or sets the text to show on the menu item for the verb.
        /// </summary>
        public string Text
        {
            get
            {
                object? result = Properties["Text"];
                if (result == null)
                {
                    return string.Empty;
                }

                return (string)result;
            }
        }

        /// <summary>
        /// Overrides object's ToString().
        /// </summary>
        public override string ToString() => Text + " : " + base.ToString();
    }
}
