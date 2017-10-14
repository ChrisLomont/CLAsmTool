using System;
using System.IO;

namespace Lomont.ClAsmTool
{
    /// <summary>
    /// Abstract method to write things to output somewhere
    /// </summary>
    public class Output
    {
        public int Errors   { get; private set; }
        public int Warnings { get; private set; }

        public Output(TextWriter output)
        {
            this.output = output;
        }

        private TextWriter output;

        void Write(string format, params object[] parameters)
        {
            output?.Write(format, parameters);
        }

        void WriteLine(string format, params object[] parameters)
        {
            Write(format + Environment.NewLine, parameters);
        }

        public void Error(string errorMessage)
        {
            ++Errors;
            WriteLine("ERROR: " + errorMessage);
        }

        public void Error(Line line, Token token, string errorMessage)
        {
            var address = line.Address;
            if (address == -1)
                address = 0;
            Error(    $"{address:X4}: Line {token.LineNumber}, position {token.TokenStart} : {errorMessage}.");
            WriteLine($"       Line: {line}, Token {token}");
        }

        public void Info(string format, params object [] parameters)
        {
            WriteLine(format,parameters);
        }

        public void Warning(string format, params object[] parameters)
        {
            ++Warnings;
            WriteLine("WARNING: " + format, parameters);
        }
    }
}
