using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Lomont.ClAsmTool
{
    /// <summary>
    /// Abstract CPU interface
    /// </summary>
    public interface ICpu
    {
        /// <summary>
        /// Call this first to initialize the item
        /// </summary>
        /// <param name="output"></param>
        void Initialize(Output output);

        /// <summary>
        /// find opcode, else return null
        /// </summary>
        /// <param name="mnemonic"></param>
        /// <returns></returns>
        Opcode FindOpcode(string mnemonic);

        /// <summary>
        /// Cleans some common opcodes to correct ones
        /// </summary>
        /// <param name="line"></param>
        /// <returns></returns>
        string FixupMnemonic(Line line);

        /// <summary>
        /// Handle the opcode parsing
        /// </summary>
        /// <param name="state"></param>
        /// <param name="line"></param>
        /// <param name="op"></param>
        void ParseOpcodeAndOperand(Assembler.AsmState state, Line line, Opcode op);

    }
}
