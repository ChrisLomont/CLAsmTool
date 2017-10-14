using System.Collections.Generic;

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

        // todo - fixups needed if this set to unspecified
        public Opcodes6809.AddressingMode AddrMode { get; set; } = Opcodes6809.AddressingMode.Unspecified;


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

        public List<Token> Tokens { get; set; }
        public bool NeedsFixup { get; set; }

    }
}
