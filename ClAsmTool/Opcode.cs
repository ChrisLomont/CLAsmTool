using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Lomont.ClAsmTool
{
    public class OpcodeFormat
    {
        /// <summary>
        /// Bytes to assemble, minus any following items (see Length)
        /// </summary>
        public List<byte> Bytes { get; } = new List<byte>();

        /// <summary>
        /// Number of bytes this will take up
        /// values n less than 0 mean (-n) or more
        /// </summary>
        public int Length { get; set; }
    }

    public class Opcode
    {
        public string Mnemonic { get; set; }

        /// <summary>
        /// Map addressing mode to opcode formats
        /// </summary>
        public Dictionary<int, OpcodeFormat> Forms { get; } =
            new Dictionary<int, OpcodeFormat>();

        public override string ToString()
        {
            return $"{Mnemonic} ";
        }
    }
}
