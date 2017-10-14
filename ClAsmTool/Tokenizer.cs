using System;
using System.Collections.Generic;
using System.Linq;

namespace Lomont.ClAsmTool
{

    public enum TokenType
    {
        Misc,
        Symbol,
        Delimeter,
        LineContinue,
        Whitespace,
        Comment,
        LineEnd
    }

    public class Token
    {
        public TokenType TokenType { get; set; }
        public string LineText { get; private set; }
        public int LineNumber { get; private set; }
        public int TokenStart { get; private set; }
        public int TokenLength { get; private set; }
        public string Text { get; set; }
        public Token(string lineText, int lineNumber, int tokenStart, int tokenLength, TokenType tokenType)
        {
            LineNumber = lineNumber;
            LineText = lineText;
            TokenLength = tokenLength;
            TokenStart = tokenStart;
            TokenType = tokenType;
            Text = LineText.Substring(TokenStart, TokenLength);
        }

        public override string ToString()
        {
            return $"({LineNumber},{TokenStart})={TokenType}:{Text}";
        }
    }

    class Tokenizer
    {
        // parse lines of assembler text to abstract tokenized lines
        // return null on error
        public static List<Line> GetLines(IEnumerable<string> textLines, Output output)
        {
            var lines = new List<Line>();
            var tokens = new List<Token>();
            var lineContinue = false;
            foreach (var token in Tokenize(textLines, output))
            {
                switch (token.TokenType)
                {
                    case TokenType.Comment:
                        break; // ignore
                    case TokenType.Delimeter:
                    case TokenType.Whitespace:
                    case TokenType.Misc:
                    case TokenType.Symbol:
                        tokens.Add(token);
                        break;
                    case TokenType.LineContinue:
                        lineContinue = true;
                        break;
                    case TokenType.LineEnd:
                        if (lineContinue == false)
                        {
                            AddLineIfPossible(tokens, lines, output);
                            tokens.Clear();
                        }
                        lineContinue = false;
                        break;
                    default:
                        throw new Exception($"Unknown token type {token}");
                }
            }
            if (tokens.Any())
                AddLineIfPossible(tokens, lines,output);

            return lines;
        }


        // conert the tokens to a line
        static void AddLineIfPossible(List<Token> tokens, List<Line> lines, Output output)
        {
            // walk tokens: delimeters, whitespace, misc, symbol

            Token label = null, opcode = null, operand = null;

            var operandTextAddition = "";

            foreach (var t in tokens)
            {
                switch (t.TokenType)
                {
                    case TokenType.Symbol:
                        if (t.TokenStart == 0)
                            label = t;
                        else if (t.TokenStart > 0 && opcode == null)
                            opcode = t;
                        else if (opcode != null)
                        {
                            if (operand == null)
                                operand = t;
                            else
                                operandTextAddition += t.Text;
                        }
                        break;
                    case TokenType.Whitespace:
                        if (operand != null)
                            operandTextAddition += t.Text;
                        break;
                    case TokenType.Delimeter:
                    case TokenType.Misc:
                        if (opcode != null)
                        {
                            if (operand == null)
                                operand = t;
                            else
                                operandTextAddition += t.Text;
                        }
                        break;
                    default:
                        output.Error($"Unsupported token {t.TokenType} in AddLine");
                        break;
                }
            }

            if (operand != null)
            {
                operand.Text += operandTextAddition;
                operand.Text = Normalize(operand.Text);
            }

            if (label != null || opcode != null || operand != null)
                lines.Add(new Line(label,opcode,operand));
        }

        static string Normalize(string operandText)
        {
            return operandText.Replace("\t"," ").Trim(new []{' ','\t','\r','\n'});
        }

        static IEnumerable<Token> Tokenize(IEnumerable<string> lines, Output output)
        {
            var lineNumber = 0;
            foreach (var line in lines)
            {
                var linePos = 0; // char pos on current line
                ++lineNumber;
                while (linePos < line.Length)
                {
                    var matchLength = 0; // length of a match
                    if (Matcher.MatchWhitespace(line, linePos, out matchLength))
                        yield return new Token(line, lineNumber, linePos, matchLength, TokenType.Whitespace);
                    else if (Matcher.MatchLineContinue(line, linePos, out matchLength))
                        yield return new Token(line, lineNumber, linePos, matchLength, TokenType.LineContinue);
                    else if (Matcher.MatchDelimiter(line, linePos, out matchLength))
                        yield return new Token(line, lineNumber, linePos, matchLength, TokenType.Delimeter);
                    else if (Matcher.MatchComment(line, linePos, out matchLength))
                        yield return new Token(line, lineNumber, linePos, matchLength, TokenType.Comment);
                    else if (Matcher.MatchSymbol(line, linePos, out matchLength))
                        yield return new Token(line, lineNumber, linePos, matchLength, TokenType.Symbol);
                    else if (Matcher.MatchMisc(line, linePos, out matchLength))
                        yield return new Token(line, lineNumber, linePos, matchLength, TokenType.Misc);
                    else
                    {
                        throw new Exception(
                            $"Tokenizer made no progress on line {lineNumber} position {linePos}: {line}");
                    }
                    linePos += matchLength;

                }
                // ensure every line has an end
                yield return new Token(line, lineNumber, line.Length, 0, TokenType.LineEnd);
            }
        }
    }
}
