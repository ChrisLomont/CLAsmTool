using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Build.Tasks;

namespace Lomont.ClAsmTool
{
    //helps diff the Yellow and Blue roms to find changes
    class RomDiff
    {
        Assembler.AsmState state;
        List<Line> lines => state.Lines;
        byte[] rom;

        class Block
        {
            public int SourceAddress, DestAddress;
            public List<Line> Lines = new List<Line>();
            public byte[] Data;
            public int[] Deltas;
            public MatchType MatchType;
            /// <summary>
            /// Total bytes to match, i.e., length of block
            /// </summary>
            public int Length { get; set; }

            public override string ToString()
            {
                return $"{SourceAddress:X4}->{DestAddress:X4}: {MatchType}, {Length}";
            }
        }
        void Analyze()
        {
            var blocks = MakeBlocks();
            output.Info($"{blocks.Count} blocks");

            for (var i = 0; i < 10; ++i)
                output.Info($"{blocks[i]}");

            var address = 0;
            var index = 0;
            var placed = new List<Block>();
            while (index < blocks.Count)
            {
                var b = blocks[index];
                if (PlaceBlock(b, ref address))
                {
                    placed.Add(b);
                    blocks.Remove(b);
                }
                else
                    ++index; // try next
            }
            output.Info($"{placed.Count} placed");
            foreach (var block in placed)
            {
                output.Info($"{block}");
            }
        }

        // try to place block at address on up
        // give up if moved too far off
        bool PlaceBlock(Block block, ref int romAddress)
        {
            for (var shift = 0; shift < 10; ++shift)
            {
                var match = true;
                var tempAddr = romAddress + shift;
                for (var j = 0; j < block.Data.Length && match; ++j)
                {
                    if (block.Data[j] == rom[tempAddr])
                        tempAddr += block.Deltas[j];
                    else
                        match = false;
                }
                if (match)
                {
                    block.DestAddress = romAddress + shift;
                    romAddress = tempAddr;
                    return true;
                }
            }
            romAddress += block.Length; // skip anyways
            return false;
        }

        List<Block> MakeBlocks()
        {
            var tempLines = lines.Where(line =>
                !(line.Opcode == null || line.Opcode.Text.StartsWith(".") || line.Opcode.Text == "end")
            ).ToList();


            var i = 0;
            var blocks = new List<Block>();
            while (i < tempLines.Count)
            {
                var line = tempLines[i];

                if (IsCode(tempLines[i]))
                {
                    var b = new Block();
                    b.Lines.Add(tempLines[i]);
                    b.MatchType = MatchType.Code;
                    blocks.Add(b);
                    ++i;
                    while (i < tempLines.Count && IsCode(tempLines[i]))
                    {
                        b.Lines.Add(tempLines[i]);
                        ++i;
                    }
                    // pack data, one byte per line
                    b.Data = new byte[b.Lines.Count];
                    b.Deltas = new int[b.Lines.Count];
                    for (var j = 0; j < b.Lines.Count; ++j)
                    {
                        b.Data[j] = b.Lines[j].Data[0];
                        b.Deltas[j] = b.Lines[j].Data.Count;
                    }
                    b.Length = b.Deltas.Sum();
                    b.SourceAddress = b.Lines[0].Address;
                }

                else if (IsData(tempLines[i]))
                {
                    var b = new Block();
                    b.Lines.Add(tempLines[i]);
                    b.MatchType = MatchType.Data;
                    blocks.Add(b);
                    ++i;
                    while (i < tempLines.Count && IsData(tempLines[i]))
                    {
                        b.Lines.Add(tempLines[i]);
                        ++i;
                    }
                    // pack all data
                    var len = b.Lines.Sum(line1 => line1.Data.Count);
                    b.Data = new byte[len];
                    b.Deltas = new int[len];
                    for (var j = 0; j < b.Lines.Count; ++j)
                    {
                        Array.Copy(b.Lines[j].Data.ToArray(),0,b.Data,0,b.Lines[j].Length);
                        b.Data[j] = b.Lines[j].Data[0];
                        b.Deltas[j] = 1;
                    }
                    b.Length = len;
                    b.SourceAddress = b.Lines[0].Address;
                }
                else
                {
                    output.Error($"Unknown line type {tempLines[i]}");
                    ++i;
                }
            }
            return blocks;
        }

        bool IsData(Line line)
        {
            return GetMatchType(line) == MatchType.Data;



            var dataDirs = new[] { "fdb", "fcb", "fcc" };
            return dataDirs.Contains(line?.Opcode?.Text);
        }

        bool IsCode(Line line)
        {
            return GetMatchType(line) == MatchType.Code;
            return Opcodes6809.FindOpcode(Assembler.GetMnemonic(line)) != null;
        }

        enum Outcome
        {
            /// <summary>
            /// Perfect = same bytes, same place
            /// </summary>
            Perfect,
            /// <summary>
            /// Same bytes, shifted
            /// </summary>
            Shifted, 
            /// <summary>
            /// Same bytes mostly, some change
            /// </summary>
            Changed,
            /// <summary>
            /// Could not match
            /// </summary>
            Missing,
            /// <summary>
            /// Algorithm failure
            /// </summary>
            Unknown
        }

        enum MatchType
        {
            Unknown,
            Data,
            Code
        }

        class Match
        {
            public Outcome Outcome;
            public MatchType MatchType;
            public Line Line;
            public int RomAddress;

            public override string ToString()
            {
                return $"{Outcome}: {Line.Address:X4}->{RomAddress:X4}  {Line} ";
            }
        }

        int[] matches =
        {
            // Yellow -> Blue
            0x2B33,0x2B7C
        };

        void FixupAddress(int sourceAddress, ref int addressRom)
        {
            for (var i = 0; i < matches.Length; i+=2)
                if (matches[i] == sourceAddress)
                    addressRom = matches[i + 1];
        }

        void Analyze12()
        {
            var addressRom = 0;
            var matches = new List<Match>();
            foreach (var line in lines)
            {
                    if (line.Opcode != null && !line.Opcode.Text.StartsWith(".") && line.Opcode.Text != "end")
                {
                    FixupAddress(line.Address, ref addressRom);
                    matches.Add(FindMatch(line, ref addressRom, 20));
                }
                // todo - need to match multiple instructions at a time
                // around 2B33 jumps 0x40 ahead or so
            }

            var dict = new Dictionary<MatchType, List<Match>>();

            foreach (var m in matches)
            {
                if (!dict.ContainsKey(m.MatchType))
                    dict.Add(m.MatchType, new List<Match>());
                dict[m.MatchType].Add(m);
            }

            foreach (var d in dict)
                output.Info($"{d.Key} has {d.Value.Count} entries");
            if (dict.ContainsKey(MatchType.Unknown))
                foreach (var k in dict[MatchType.Unknown])
                    output.Info($"Unknown {k.Line}");

            output.Info($"Perfect: {matches.Count(m => m.Outcome == Outcome.Perfect)}, first mismatch {matches.First(m => m.Outcome != Outcome.Perfect)}");

            output.Info("First few not perfect");
            var c = 0;
            var skipOps = new[]{"jsr", "jmp"};

            foreach (var m in matches)
            {
                if (m.Outcome != Outcome.Perfect)
                {
                    if (skipOps.Contains(m.Line.Opcode?.Text))
                        continue; // skip for now
                    output.Info($"{m}");
                    ++c;
                    if (c > 100)
                        break;
                }
            }


        }

        // Create the best match item
        // only feed in data and code lines
        Match FindMatch(Line line, ref int addressRom1, int lookahead)
        {
            if (line?.Label?.Text == "Filler_02")
                output.Info("Debug break");
            if (line?.Operand?.Text == "PlayerMove")
                output.Info("Debug break");

            var match = new Match
            {
                Line = line,
                Outcome = Outcome.Unknown,
                RomAddress = addressRom1,
                MatchType = MatchType.Unknown,
            };
            match.MatchType = GetMatchType(line);
            if (match.MatchType == MatchType.Unknown)
                return match;

            var found = false;
            var shift = -1; // cannot back up too much, especially if prev matched
            while (shift < lookahead && !found)
            {
                ++shift;
                if (shift + addressRom1 < 0 || 0x10000 <= shift + addressRom1 + line.Data.Count )
                    continue;
                found = true; // assume find this pass
                var(firstMatches, opcodeMatches, totalCount, matchCount) =
                    MatchCount(rom, addressRom1+shift, line.Data.ToArray(), 0, line.Data.Count);

                if (totalCount == matchCount && shift == 0)
                    match.Outcome = Outcome.Perfect;
                else if (opcodeMatches && match.MatchType == MatchType.Code)
                    match.Outcome = Outcome.Changed;
                else if (match.MatchType == MatchType.Data && matchCount > totalCount / 3 + 1)
                    match.Outcome = Outcome.Changed;
                else if (match.MatchType == MatchType.Data && match.Outcome == Outcome.Unknown)
                    match.Outcome = Outcome.Changed;
                else
                    found = false;
            }
            match.RomAddress = addressRom1 + shift;
            addressRom1 += line.Data.Count + shift;
            return match;
        }

        // return (first byte matches, opcode matches, total bytes in line, number of matches)
        // opcode matches if first byte matches and is not 0x10 or 0x11. If 0x10 or 0x11, first two need to match
        (bool firstMatches, bool opcodeMatches, int totalCount, int matchCount) MatchCount(byte[] array1, int offset1, byte[] array2, int offset2, int length)
        {
            var firstMatches = array1[offset1] == array2[offset2];
            var opcodeMatches = firstMatches && array1[offset1] != 0x10 && array1[offset1] != 0x11;
            if (firstMatches && (array1[offset1] == 0x10 || array1[offset1] == 0x11) && length > 1)
                opcodeMatches = array1[offset1 + 1] == array2[offset2 + 1];

            var count = 0;
            for (var i = 0; i < length; ++i)
                if (array1[offset1 + i] == array2[offset2 + i])
                    ++count;
            return (firstMatches, opcodeMatches, length, count);
        }


        MatchType GetMatchType(Line line)
        {

        if (line.Opcode == null)
            return MatchType.Unknown;
        if (Opcodes6809.FindOpcode(Assembler.GetMnemonic(line))!=null)
            return MatchType.Code;
            var dataDirs = new [] { "fdb","fcb","fcc"};
        if (dataDirs.Contains(line.Opcode.Text))
            return MatchType.Data;
            if (line.Data.Count > 0)
                return MatchType.Data;


            return MatchType.Unknown;
        }



        void AnalyzeOld()
        {
            var count = 0;
            var matched = 0;
            var unmatched = 0;
            var lineIndex = 0;
            while (true)
            {
                var tuple = FindTuple(lineIndex, 5);
                if (tuple == null)
                    break;
                var match = FindMatch(tuple, rom,20);
                if (match != -1)
                {
                    ++matched;
                    // do something
                }
                else
                {
                    if (unmatched < 20)
                        output.Info($"Unmatched at {lines[tuple[0]].Address:X4}");
                    ++unmatched;
                    // something else
                }
                lineIndex = tuple[1];
            }
            output.Info($"Matched {matched}, unmatched {unmatched}");

        }

        // try to match the lines, return offset into rom of first line
        // return -1 on no match
        int FindMatch(int[] tuple, byte[] rom, int size)
        {
            for (var delta = -size; delta < size; ++delta)
            {
                var matchCount = 0;
                for (var i = 0; i < tuple.Length; ++i)
                {
                    var opcode = lines[tuple[i]].Data[0]; // look for this byte
                    // todo - if x10 or x11, get 2 bytes to match
                    var addr = lines[tuple[i]].Address;   // around here
                    var dadd = addr + delta;
                    if (dadd < 0 || rom.Length <= dadd)
                        break; // out of bounds
                    if (rom[dadd] != opcode)
                        break;
                    ++matchCount;
                }
                if (matchCount == tuple.Length)
                    return lines[tuple[0]].Address + delta;
            }
            return -1;
        }

        // find so many lines in a row, starting at line index, that are all opcodes
        int [] FindTuple(int startIndex, int length)
        {
            var ans = new int[length];

            while (startIndex < lines.Count)
            {
                var match = true;
                for (var i = 0; i < length && match; ++i)
                {
                    var mnemonic = Assembler.GetMnemonic(lines[startIndex+i]);
                    var op = Opcodes6809.FindOpcode(mnemonic);
                    if (op == null)
                        match = false;
                    ans[i] = startIndex + i;
                }
                if (match)
                    return ans;
                ++startIndex;
            }
            return null;
        }


        Output output;
        // compare one Rom to the other
        public void DiffRoms(Assembler.AsmState state, string romPath)
        {
            output = state.Output;
            output.Info("");
            output.Info("***********  Analyze  ******************");
            this.state = state;
            rom = Load(romPath);
            if (rom == null)
                return;



            output.Info("***********  load blue rom *************");
            var blueAsm = new Assembler();
            var fn = @"C:\Users\Chris\OneDrive\Robotron\DisasmBlue\robotronB.asm";
            blueAsm.Assemble(fn, Console.Out);

            var i = 0;
            var j = 0;
            while (true)
            {
                while (!IsCode(lines[i]))
                    ++i;
                while (!IsCode(blueAsm.State.Lines[j]))
                    ++j;

                var l1 = lines[i];
                var l2 = blueAsm.State.Lines[j];
                if (l1.Opcode.Text != l2.Opcode.Text)
                { 
                    state.Output.Error($"First mismatch at {l1} != {l2}");
                    break;
                }
                ++i;
                ++j;
            }

//                Analyze();

            output.Info("***********  Analyze  ******************");
            output.Info("");
            output.Info("");
        }


        byte[] Load(string romPath)
        {
            var rom = new byte[0x10000]; // blue rom
            var filenames = new[]
            {
                "robotron.sb1",
                "robotron.sb2",
                "robotron.sb3",
                "robotron.sb4",
                "robotron.sb5",
                "robotron.sb6",
                "robotron.sb7",
                "robotron.sb8",
                "robotron.sb9",
                "", // A000
                "", // B000
                "", // C000
                "robotron.sba", // D000
                "robotron.sbb",
                "robotron.sbc",
            };
            var offset = 0;
            foreach (var f in filenames)
            {
                if (!String.IsNullOrEmpty(f) && !LoadRom(romPath, f, offset, rom))
                    return null;
                offset += 0x1000;
            }
            return rom;
        }

        bool LoadRom(string path, string filename, int offset, byte[] rom)
        {
            var fn = Path.Combine(path, filename);
            if (!File.Exists(fn))
            {
                output.Error($"Rom file {fn} does not exist");

                return false;
            }
            var data = File.ReadAllBytes(fn);
            Array.Copy(data,0,rom,offset,data.Length);
            return true;
        }
    }
}
