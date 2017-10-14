using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Schema;

namespace Lomont.ClAsmTool
{
    static class Splitter
    {
        
        // output new roms
        /// <summary>
        /// Suffix, must start with '.'
        /// </summary>
        public static string OutputSuffix = ".out";
        public static void Split(Asm6809.AsmState state, string outputPath)
        {
            var output = state.Output;

            output.Info($"Splitting ROM into {state.RomDefinitions.Count} output files.");
            foreach (var romDef in state.RomDefinitions)
            {
                var filename = Path.Combine(outputPath, romDef.Filename + OutputSuffix);
                var rom = new byte[romDef.Size];
                Array.Copy(state.RomImage,romDef.Offset, rom,0,romDef.Size);
                File.WriteAllBytes(filename,rom);
                output.Info($"File {filename} created");
            }
            
        }
    }
}
