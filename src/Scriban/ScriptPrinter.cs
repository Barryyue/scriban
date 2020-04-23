// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using Scriban.Helpers;
using Scriban.Parsing;
using Scriban.Runtime;
using Scriban.Syntax;

namespace Scriban
{
    /// <summary>
    /// Rewriter context used to write an AST/<see cref="ScriptNode"/> tree back to a text.
    /// </summary>
    public partial class ScriptPrinter
    {
        private readonly IScriptOutput _output;
        private readonly bool _isScriptOnly;
        private bool _isInCode;
        private bool _expectSpace;
        private bool _expectEndOfStatement;
        // Gets a boolean indicating whether the last character written has a whitespace.
        private bool _previousHasSpace;
        private bool _hasEndOfStatement;
        private FastStack<bool> _isWhileLoop;

        public ScriptPrinter(IScriptOutput output, ScriptPrinterOptions options = default(ScriptPrinterOptions))
        {
            _isWhileLoop = new FastStack<bool>(4);
            Options = options;
            if (options.Mode != ScriptMode.Default && options.Mode != ScriptMode.ScriptOnly)
            {
                throw new ArgumentException($"The rendering mode `{options.Mode}` is not supported. Only `ScriptMode.Default` or `ScriptMode.ScriptOnly` are currently supported");
            }

            _isScriptOnly = options.Mode == ScriptMode.ScriptOnly;
            _isInCode = _isScriptOnly;
            _output = output;
        }

        /// <summary>
        /// Gets the options for rendering
        /// </summary>
        public readonly ScriptPrinterOptions Options;

        public bool PreviousHasSpace => _previousHasSpace;

        public bool IsInWhileLoop => _isWhileLoop.Count > 0 && _isWhileLoop.Peek();

        public ScriptPrinter Write(ScriptNode node)
        {
            if (node != null)
            {
                bool pushedWhileLoop = false;
                if (node is ScriptLoopStatementBase)
                {
                    _isWhileLoop.Push(node is ScriptWhileStatement);
                    pushedWhileLoop = true;
                }
                try
                {
                    WriteBegin(node);
                    node.PrintTo(this);
                    WriteEnd(node);
                }
                finally
                {
                    if (pushedWhileLoop)
                    {
                        _isWhileLoop.Pop();
                    }
                }
            }
            return this;
        }

        public ScriptPrinter Write(string text)
        {
            _previousHasSpace = text.Length > 0 && char.IsWhiteSpace(text[text.Length - 1]);
            _output.Write(text);
            return this;
        }

        public ScriptPrinter ExpectEos()
        {
            if (!_hasEndOfStatement)
            {
                _expectEndOfStatement = true;
            }
            return this;
        }

        public ScriptPrinter ExpectSpace()
        {
            _expectSpace = true;
            return this;
        }

        public ScriptPrinter WriteListWithCommas<T>(IList<T> list) where T : ScriptNode
        {
            if (list == null)
            {
                return this;
            }
            for(int i = 0; i < list.Count; i++)
            {
                var value = list[i];
                Write(value);

                // If the value didn't have any Comma Trivia, we can emit it
                if (i + 1 < list.Count && !value.HasTrivia(ScriptTriviaType.Comma, false))
                {
                    Write(",");
                    ExpectSpace();
                }
            }
            return this;
        }

        public ScriptPrinter WriteEnterCode(int escape = 0)
        {
            Write("{");
            for (int i = 0; i < escape; i++)
            {
                Write("%");
            }
            Write("{");
            _expectEndOfStatement = false;
            _expectSpace = false;
            _hasEndOfStatement = true;
            _isInCode = true;
            return this;
        }

        public ScriptPrinter WriteExitCode(int escape = 0)
        {
            Write("}");
            for (int i = 0; i < escape; i++)
            {
                Write("%");
            }
            Write("}");

            _expectEndOfStatement = false;
            _expectSpace = false;
            _hasEndOfStatement = false;
            _isInCode = false;
            return this;
        }

        private void WriteBegin(ScriptNode node)
        {
            WriteTrivias(node, true);

            HandleEos(node);

            if (_hasEndOfStatement)
            {
                _hasEndOfStatement = false;
                _expectEndOfStatement = false;
            }

            // Add a space if this is required and no trivia are providing it
            if (node.CanHaveLeadingTrivia())
            {
                if (_expectSpace && !_previousHasSpace)
                {
                    Write(" ");
                }
                _expectSpace = false;
            }
        }

        private void WriteEnd(ScriptNode node)
        {
            WriteTrivias(node, false);

            if (node is ScriptPage)
            {
                if (_isInCode && !_isScriptOnly)
                {
                    WriteExitCode();
                }
            }
        }

        private void HandleEos(ScriptNode node)
        {
            if (node is ScriptStatement && !IsBlockOrPage(node) && _isInCode && _expectEndOfStatement)
            {
                if (!_hasEndOfStatement)
                {
                    if (!(node is ScriptEscapeStatement))
                    {
                        Write("; ");
                    }
                }
                _expectEndOfStatement = false; // We expect always a end of statement before and after
                _hasEndOfStatement = false;
            }
        }

        private static bool IsBlockOrPage(ScriptNode node)
        {
            return node is ScriptBlockStatement || node is ScriptPage;
        }

        private void WriteTrivias(ScriptNode node, bool before)
        {
            if (node.Trivias != null)
            {
                foreach (var trivia in (before ? node.Trivias.Before : node.Trivias.After))
                {
                    trivia.Write(this);
                    if (trivia.Type == ScriptTriviaType.NewLine || trivia.Type == ScriptTriviaType.SemiColon)
                    {
                        _hasEndOfStatement = true;
                        // If expect a space and we have a NewLine or SemiColon, we can safely discard the required space
                        if (_expectSpace)
                        {
                            _expectSpace = false;
                        }
                    }
                }
            }
        }
    }
}