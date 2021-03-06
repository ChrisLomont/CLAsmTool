﻿using System.Collections.Generic;

namespace Lomont.ClAsmTool
{
    public class Line
    {
        public Token Label{get; private set; }
        public Token Opcode{get; private set; }
        public Token Operand{get; private set; }

        public int Address { get; set; } = -1;
        public int Length { get; set; } = -1;
        public List<byte> Data { get; } = new List<byte>();

        public int AddressingMode { get; set; } = 0;

        public Line(Token label, Token opcode, Token operand)
        {
            Label = label;
            Opcode = opcode;
            Operand = operand;
        }

        // add value to instruction bytes in line
        // count is # of bytes. Count = 0 means imply 1 or 2 bytes based on size
        public void AddValue(int value, int count)
        {
            if ((count == 0 && -128 <= value && value <= 127) || count == 1)
                Data.Add((byte)value);
            else
            {
                uint v = (uint)value;
                Data.Add((byte)(v >> 8));
                Data.Add((byte)(v & 255));
            }
        }


        public override string ToString()
        {
            var label    = Label?.Text ?? "";
            var opcode   = Opcode?.Text ?? "";
            var operand  = Operand?.Text ?? "";
            return $"{label} {opcode} {operand}";
        }

        // tokens making this line
        public List<Token> Tokens { get; set; }

        /// <summary>
        /// true if this line needs more work to assemble
        /// </summary>
        public bool NeedsFixup { get; set; }

    }
}
