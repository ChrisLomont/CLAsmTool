using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Lomont.ClAsmTool
{
    public class Cpu6800 : ICpu
    {
        public void Initialize(Output output)
        {
            throw new NotImplementedException();
        }

        public Opcode FindOpcode(string mnemonic)
        {
            throw new NotImplementedException();
        }

        public string FixupMnemonic(Line line)
        {
            throw new NotImplementedException();
        }

        public void ParseOpcodeAndOperand(Assembler.AsmState state, Line line, Opcode op)
        {
            throw new NotImplementedException();
        }
    }
}
