using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Lomont.ClAsmTool
{
    static class Validator
    {
        public static string[] Descriptions =
        {
         "1 = first byte of each line",
         "2 = page of lines",
         "3 = every byte for each line",
         "4 = direct rom comparison"
        };


        /* test
         * 1 = first byte of each line
         * 2 = page of lines
         * 3 = every byte for each line
         * 4 = direct rom comparison
         */
        public static bool CheckDifferences(
            string romPath,
            Assembler.AsmState state,
            int debugLength, int test,
            int numErrors
        )
        {
            var lines = state.Lines;
            var output = state.Output;
            var rom = new byte[0x10000];
            if (!String.IsNullOrEmpty(romPath))
            {
                foreach (var romDef in state.RomDefinitions)
                {
                    var filename = Path.Combine(romPath, romDef.Filename);
                    output.Info($"Reading rom file {filename}");
                    var data = File.ReadAllBytes(filename);
                    if (data.Length != romDef.Size)
                    {
                        output.Error($"Rom {romDef.Filename} read is wrong size: should be 0x{romDef.Size}, was {data.Length}");
                        return false;
                    }
                    var sha1 = Hash(data);
                    if (sha1 != romDef.Sha1)
                    {
                        output.Error($"File {romDef.Filename} SHA-1 of {sha1} should be {romDef.Sha1}.");
                        return false;
                    }
                    Array.Copy(data, 0, rom, romDef.Offset, romDef.Size);
                }

                output.Info("All roms read for validation");
            }


            var errorCount = 0;
            if (test == 1)
            {
                // check first byte of each line
                for (var i = 0; i < lines.Count - 1; ++i)
                {
                    var l1 = lines[i];
                    var l2 = lines[i + 1];
                    if (l1.Address == -1 || l2.Address == -1)
                        continue;
                    if (!l1.Data.Any() || !l2.Data.Any())
                        continue;
                    if (l1.Address >= rom.Length)
                        continue;
                    if (rom[l1.Address] != l1.Data[0])
                    {
                        errorCount++;

                        if (errorCount < numErrors)
                        {
                            output.Info($"{l1.Address:X4}:  {l1.Label?.Text,20} {l1.Opcode.Text,-5} {l1.Operand?.Text}");
                            ShowError(l1.Address, 0, l1);
                            output.Info("");
                        }
                    }
                    //var a1 = l1.Address;
                    //var a2 = l2.Address;
                    //if (a2-a1 != )
                }
                return errorCount == 0;
            }
            else if (test == 2)
            {
                // show page of lines
                foreach (var line in lines)
                {
                    if (line.Address == -1 || !line.Data.Any())
                        continue;
                    //if (line.Address < debugLength - 100)
                    //    continue;
                    output.Info(
                        $"{line.Address:X4}:  {line.Label?.Text,20} {line.Opcode.Text,-5} {line.Operand?.Text}");
                    if (line.Address > debugLength)
                        return true;

                }
            }

            else if (test == 3)
            {

                foreach (var line in lines)
                {
                    var addr = line.Address;
                    if (addr != -1)
                    {
                        for (var i = 0; i < line.Data.Count; ++i)
                            if (rom[addr + i] != line.Data[i])
                            {
                                if (errorCount < numErrors)
                                    ShowError(addr, i, line);
                                ++errorCount;
                                break;
                            }
                    }
                }
                if (errorCount > 0)
                    output.Info($"{errorCount} total errors");
                return errorCount == 0;
            }
            else if (test == 4)
            {
                output.Info("Direct ROM comparison");
                if (rom.Length != state.RomImage.Length)

                {
                    output.Error($"ROM lengths different: correct is 0x{rom.Length}, assembled is {state.RomImage.Length}");
                    for (var i = 0; i < Math.Min(rom.Length, state.RomImage.Length); ++i)
                    {
                        if (rom[i] != state.RomImage[i])
                        {
                            output.Error($"First byte mismatch at {i:X4}");
                            break;
                        }
                    }
                    return false;
                }
                for (var i = 0; i < rom.Length; ++i)
                {
                    if (rom[i] != state.RomImage[i])
                    {
                        output.Error($"ROM images differ at address 0x{i:X4}");
                        if (errorCount++ > numErrors)
                            return false;
                    }
                }
            }

            void ShowError(int addr, int i, Line line)
            {
                {
                    output.Error($"Data mismatch at address 0x{addr:X4} {line}");

                    var sb = new StringBuilder();
                    sb.Append("   (Correct,ours): ");
                    for (var j = 0; j < line.Data.Count; ++j)
                    {
                        sb.Append($"({rom[addr + j]:X2},{line.Data[j]:X2}) ");
                    }
                    output.Error(sb.ToString());
                }

            }

            output.Info("Validation done.");
            return true;
        }

        /// <summary>
        /// Create SHA1 hash of data
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static string Hash(byte[] data)
        {
            using (SHA1Managed sha1 = new SHA1Managed())
            {
                var hash = sha1.ComputeHash(data);
                return String.Join("", hash.Select(b => b.ToString("x2")).ToArray()).ToUpper();
            }
        }

        public static void ShowChecksums(Assembler.AsmState asmState)
        {
            Console.WriteLine("TODO - ");
        }

    }
}
