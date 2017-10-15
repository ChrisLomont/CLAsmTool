using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Schema;
using Microsoft.Build.Tasks;

namespace Lomont.ClAsmTool
{
    public class Assembler
    {


        public AsmState State { get; private set;  }
        public int Errors => State.Output.Errors;
        public int Warnings => State.Output.Warnings;


        /// <summary>
        /// Assemble the code.
        /// Return true on no errors, else false
        /// Inspect the State property to see returned items
        /// </summary>
        /// <param name="filename"></param>
        /// <param name="textOut"></param>
        /// <returns></returns>
        public bool Assemble(string filename, TextWriter textOut)
        {

            State = new AsmState();
            State.Output = new Output(textOut);

            State.Output.Info("");
            State.Output.Info("");
            State.Output.Info($"Assembling file {filename}");

            if (!File.Exists(filename))
            {
                State.Output.Error($"File {filename} does not exist.");
                return false;
            }

            ;

            State.Lines = Tokenizer.GetLines(File.ReadLines(filename), State.Output);


            // determine CPU
            foreach (var line in State.Lines)
                if (line?.Opcode?.Text.ToLower() == ".cpu")
                    State.Cpu = MakeCpu(line?.Operand?.Text,State.Output);
            if (State.Cpu == null)
            {
                State.Output.Info("CPU not detected, assuming 6809. Use '.cpu' directive to set.");
                State.Cpu = new Cpu6809(false);
                State.Cpu.Initialize(State.Output);
            }



            // clean all opcodes
            foreach (var line in State.Lines)
                if (line.Opcode != null)
                    line.Opcode.Text = State.Cpu.FixupMnemonic(line);

            if (State.Lines == null)
                return false;
            State.Output.Info($"{State.Lines.Count} lines tokenized.");

            // handle #ifdef, #undef, etc
            Preprocess();

            //foreach (var line in lines)
            //    WriteLine(line.ToString());

            // get structs, fill in sizes, validate
            if (!MakeStructs())
                return false;

            State.Output.Info($"{State.Symbols.GetStructs().Count} structs degrizzled.");

//            foreach (var s in structs)
//                WriteLine($"Struct {s}");

            var labelLines = State.Lines.Where(d => d.Label != null);
            foreach (var labelLine in labelLines)
                State.Symbols.AddSymbol(new Label(labelLine), State.Output);

            State.Output.Info($"{State.Symbols.Count} labels parsed.");

            if (!Assemble())
                return false;

            return CreateRom();
        }

        ICpu MakeCpu(string cpuText, Output output)
        {
            ICpu cpu = null;
            if (cpuText == "6809")
                cpu = new Cpu6809(false);
            else if (cpuText == "6800")
                cpu = new Cpu6800();
            else if (cpuText == "6309")
                cpu = new Cpu6809(true);
            cpu?.Initialize(output);
            return cpu;
        }

        void Preprocess()
        {
            var useLines = true;
            // track define and skipLines state
            var ifStack = new Stack<Tuple<string,bool>>();
            // lines to remove from analysis
            var remove = new List<Line>();
            // things currently defined
            var defines = new HashSet<string>();
            var showDefs = false; // for debugging
            foreach (var line in State.Lines)
            {
                if (!useLines)
                    remove.Add(line);
                if (line.Label == null)
                    continue;
                var label = line.Label.Text;

                if (label == "#define")
                {
                    if (useLines)
                    {
                        var text = line.Opcode.Text;
                        defines.Add(text);
                        if (showDefs)
                            State.Output.Info($"***: #define {text}->{useLines}");
                    }
                    remove.Add(line);
                }
                else if (label == "#undef")
                {
                    if (useLines)
                    {
                        var text = line.Opcode.Text;
                        defines.Remove(text);
                        if (showDefs)
                            State.Output.Info($"***: #undef {text}->{useLines}");
                    }
                    remove.Add(line);
                }
                else if (label == "#ifdef")
                {
                    var text = line.Opcode.Text;
                    ifStack.Push(new Tuple<string, bool>(text, useLines));
                    if (useLines)
                        useLines = defines.Contains(text);
                    if (showDefs)
                        State.Output.Info($"***: #ifdef {text}->{useLines}");
                    remove.Add(line);
                }
                else if (label == "#else")
                {
                    if (ifStack.Peek().Item2)
                        useLines = !useLines;
                    if (showDefs)
                        State.Output.Info($"***: #else -> {useLines}");
                    remove.Add(line);
                }
                else if (label == "#endif")
                {
                    if (!ifStack.Any())
                    {
                        Error(line, line.Label, "Unmatched #endif");
                        return;
                    }
                    var p = ifStack.Pop();
                    useLines = p.Item2;
                    if (showDefs)
                        State.Output.Info($"***: #endif -> {useLines}");
                    remove.Add(line);
                }

            }
            // todo - check if/end balanced
            if (ifStack.Any())
            {
                State.Output.Error("Missing #endif");
                return;
            }
            State.Output.Info($"Removing {remove.Count} lines due to preprocessor");
            foreach (var r in remove)
                State.Lines.Remove(r);

        }

        // order lines, check dense packed
        // make ROM image
        bool CreateRom()
        {
            var address = -1;
            var lastAddress = State.Lines.Max(line => line.Address + line.Length);
            State.RomImage = new byte[lastAddress];
            foreach (var line in State.Lines.Where(s=>s.Address != -1 && s.Length>0).OrderBy(s=>s.Address))
            {
                if (address == -1)
                    address = line.Address;
                if (address != line.Address)
                {
                    State.Output.Warning($"Address 0x{address:X4} not accounted for in ROM");
                    address = line.Address;
                }
                for (var j = 0; j < line.Data.Count; ++j)
                    State.RomImage[j + address] = line.Data[j];
                address += line.Length;
            }
            return true;
        }

        #region Assembler

        /// <summary>
        /// Return true on success
        /// Fills in State
        /// </summary>
        /// <returns></returns>
        bool Assemble()
        {
            // first pass creates addresses for items and does whatever it can figure out
            foreach (var line in State.Lines)
                if (!AssembleLine(line))
                    return false;

            // fixup items like labels, addresses, instruction order, etc
            State.Pass++;
            var fixedLines = 0;
            foreach (var line in State.Lines)
            {
                if (line.NeedsFixup)
                {
                    line.NeedsFixup = false;
                    State.Address = line.Address;
                    AssembleLine(line);
                    if (!line.NeedsFixup)
                        fixedLines++;
                    else
                    {
                        Error(line, line.Opcode, "Cannot fix line");
                        // try again for debugging 
                        line.NeedsFixup = false;
                        State.Address = line.Address;
                        AssembleLine(line);
                        break;
                    }
                }
            }
            State.Output.Info($"{fixedLines} lines fixed up");

            // compute the address past the end of the last instruction
            State.Address = State.Lines.Max(line => line.Address + line.Length);
            
            // warn on labels too close - can cause mis-assembly
            var labels = State.Symbols.GetLabels();
            for (var i = 0; i < labels.Count; ++i)
            {
                var label1 = labels[i];
                if (label1.Address == Label.UnknownAddress)
                    continue;
                for (var j = i + 1; j < labels.Count; ++j)
                {
                    var label2 = labels[j];
                    if (label2.Address == Label.UnknownAddress)
                        continue;
                    if (label1.Text == label2.Text && Math.Abs(label1.Address - label2.Address) < 512)
                        State.Output.Error($"Labels too close {label1.Text} at x{label1.Address:X4} and x{label2.Address:X4}");
                }


            }

            // debugging
            // labelManager.Dump("PlayerXsub",output);
            // labelManager.Dump("GruntSpeedMax",output);
            // labelManager.Dump("next1", output);
            // State.Symbols.Dump("j_DecimalToHexA", State.Output);
            //State.Symbols.Dump("DoubleLowColor", State.Output);
            State.Symbols.Dump("Player1Block", State.Output);

            foreach (var line in State.Lines)
            {
                if (line.Address == -1 && line.Opcode != null)
                    Error(line, line?.Label ?? line.Opcode, "Unassembled opcode");
                else if (line.Address != -1 && line.Length != line.Data.Count)
                    Error(line, line?.Label ?? line.Opcode, $"Line length {line.Length}, byte length {line.Data.Count}");
            }

            DumpStats();

            return true;
        }

        
        bool AssembleLine(Line line)
        {
            // assign label address
            if (State.Pass == 1 && line.Label != null && !State.Symbols.SetAddress(line, State.Address))
                Error(line,line.Label,"Cannot set label address");

            if (line.Opcode == null)
                return true; // nothing else to parse on line

            if (!OpcodeHandled(State, line) && !StructHandled(State, line) && !PseudoOpHandled(State, line) && !DirectiveHandled(State, line))
            {
                Error(line, line.Opcode, $"Could not assemble line {line}");
                return false;
            }
            if (line.Length>=0)
                State.Address += line.Length;
            return true;
        }

        void DumpStats()
        {
            State.Output.Info($"Final address 0x{State.Address:X4}");
            State.Output.Info($"Unfixed lines {State.Lines.Count(m=>m.NeedsFixup)} of {State.Lines.Count} lines");

            foreach (var label in State.Symbols.GetLabels())
            {
                if (label.Address == Label.UnknownAddress)
                    State.Output.Info($"?????? : {label.Text}");
                //else
                //    WriteLine($"0x{label.Address:X4} : {label.Text}");
                
            }



        }

        bool StructHandled(AsmState state, Line line)
        {
            var structName = line.Opcode.Text;
            var str = state.Symbols.GetStructs().FirstOrDefault(s => s.Text == structName);
            if (str != null)
            {
                line.Address = state.Address;
                var operands = line.Operand?.Text??"";
                line.Length = str.ByteLength;
                if (String.IsNullOrEmpty(operands))
                {
                    Error(line,line.Opcode,"Empty struct operand");
                    return false;
                }
                // odd special case of zeroed fields
                var cleaned = operands.Replace("<", "").Replace(">", "").Replace("\t", "").Replace(" ", "");
                var zeroOut = cleaned == "0"; // special case - if this, zero out structure
                cleaned = ConvertStringsToNumbers(cleaned);
                var items = cleaned.Split(',');
                if (str.ByteLengths.Count != items.Length && !zeroOut)
                {
                    Error(line,line.Operand,$"Struct def has {str.ByteLengths.Count} fields, operand has {items.Length}");
                    return false;
                }
                var bytesAlready = line.Data.Count;
                var value = 0;
                for (var i = 0; i < str.ByteLengths.Count; ++i)
                {
                    if (zeroOut || Evaluator.Evaluate(state, line, items[i], out value))
                        line.AddValue(value, str.ByteLengths[i]);
                    else
                    {
                        line.NeedsFixup = true;
                        var len = line.Data.Count;
                        if (len > bytesAlready) // restore state
                            line.Data.RemoveRange(bytesAlready,len-bytesAlready);
                        break; // will return to here and try again
                    }
                }
                return true;
            }
            return false;


        }

        /// <summary>
        /// Parse any embedded strings of form "ABC" to numbers 65,66,67
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        string ConvertStringsToNumbers(string text)
        {
            while (text.Contains("\""))
            {
                var start = text.IndexOf("\"", StringComparison.Ordinal);
                var end = text.IndexOf("\"", start + 1, StringComparison.Ordinal);
                if (end > start)
                {
                    var len = end - start;
                    var str = text.Substring(start + 1, len - 1);
                    var sb=new StringBuilder();
                    foreach (var c in str)
                        sb.Append($"{(int)c},");
                    var repl = sb.ToString();
                    repl = repl.Substring(0, repl.Length - 1);
                    text = text.Replace("\""+str+"\"", repl);
                }
            }
            return text;
        }

        void Error(Line line, Token token, string message)
        {
            State.Output.Error(line, token, message);
        }

        bool DirectiveHandled(AsmState state, Line line)
        {
            int value;
            switch (line.Opcode.Text.ToLower())
            {
                case ".setdp": // set direct page
                    line.Address = state.Address;
                    line.Length = 0;
                    if (Evaluator.Evaluate(state, line, line.Operand.Text, out value))
                        state.dpRegister = value; // todo - move to CPU class
                    else
                        Error(line, line.Operand, "Cannot evaluate DP");
                    break;
                case ".rom": // add rom definition
                        line.Address = state.Address;
                        line.Length = 0;
                        var fields = line.Operand.Text.Split(new[] {' '}, StringSplitOptions.RemoveEmptyEntries)
                        .Select(r => r.Trim().ToLower())
                        .ToList();
                    if (fields.Count == 4 && 
                            Int32.TryParse(fields[1], out var size) && 
                            Int32.TryParse(fields[3],
                            NumberStyles.AllowHexSpecifier | NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                            out var offset))

                    {
                        var filename = fields[0];
                        var sha1 = fields[2];
                        state.RomDefinitions.Add(new AsmState.RomDef(filename, size, offset, sha1));
                    }
                    else
                        Error(line, line.Operand, "Cannot parse .ROM directive.");
                    break;
                case ".cpu": // set CPU type todo
                case ".meta": // simple comment
                    line.Address = state.Address;
                    line.Length = 0;
                    break;
                case ".org": // set address
                    line.Address = state.Address;
                    line.Length = 0;
                    if (Evaluator.Evaluate(state, line, line.Operand.Text, out value))
                        state.Address = value;
                    else
                        Error(line, line.Operand, "Cannot evaluate address");
                    break;
                default:
                    return false;
            }
            return true;
        }

        bool PseudoOpHandled(AsmState state, Line line)
        {
            switch (line.Opcode.Text.ToLower())
            {
                case "fcc": // string data
                    line.Address = state.Address;
                    var num = ConvertStringsToNumbers(line.Operand.Text);
                    WriteData(state,line,num,1);
                    break;
                case "fcb": // byte data
                    line.Address = state.Address;
                    WriteData(state, line, line.Operand.Text, 1);
                    break;
                case "fdb": // double byte data
                    line.Address = state.Address;
                    WriteData(state, line, line.Operand.Text, 2);
                    break;
                case "end": // end of assembly
                    line.Address = state.Address;
                    line.Length = 0;
                    break;
                default:
                    return false;
            }
            return true;
        }

        // write fdb or fcb data
        void WriteData(AsmState state, Line line, string text, int itemLength)
        {
            var items = text.Split(',').Select(t => t.Trim()).ToList();
            line.Length = itemLength * items.Count;
            foreach (var item in items)
            {
                if (Evaluator.Evaluate(state, line, item, out int value))
                    line.AddValue(value,itemLength);
                else
                {
                    line.Data.Clear();
                    line.NeedsFixup = true;
                    break;
                }
            }
        }

        public static string GetMnemonic(Line line)
        {
            return line?.Opcode?.Text.ToLower() ?? "";
        }

        bool OpcodeHandled(AsmState state, Line line)
        {
            var mnemonic = GetMnemonic(line);
            var op = State.Cpu.FindOpcode(mnemonic);
            if (op != null)
            {
                line.Address = state.Address;
                State.Cpu.ParseOpcodeAndOperand(state, line, op);
                return true;
            }
            return false;
        }


        // return 5, 8,  16 depending on how many bits needed for value
        // works on 16 bit value
        public static int BitsRequired(int value1)
        {
            short value = (short)value1;
            if (-16 <= value && value <= 15)
                return 5;
            if (-128 <= value && value <= 127)
                return 8;
            return 16;
        }

        
        public class AsmState
        {
            public ICpu Cpu;

            // assembly pass 1+
            public int Pass = 1;

            public SymbolManager Symbols { get;  }= new SymbolManager();

            // address to assemble to
            public int Address = 0;

            // value of direct page register
            public int dpRegister = 0;

            public List<Line> Lines { get; set; }
            public Output Output { get; set; }

            public struct RomDef
            {
                public RomDef(string filename, int size, int offset, string sha1)
                {
                    Filename = filename;
                    Offset = offset;
                    Sha1 = sha1.ToUpper();
                    Size = size;
                }
                public string Filename { get;  }
                public int Offset { get;  }
                public int Size { get;  }
                public string Sha1 { get;  }
                public override string ToString()
                {
                    return $"{Filename} : {Offset:X4} {Size:X4} {Sha1}";
                }
            }

            public List<RomDef> RomDefinitions { get;  } = new List<RomDef>();

            /// <summary>
            /// Complete ROM image
            /// </summary>
            /// todo
            public byte [] RomImage { get; set; }
        }

#endregion

#region Structs

        private const string STRUCT_TEXT = "struc";
        private const string END_STRUCT_TEXT = "ends";

        // parse out all structs
        // return true on success, else false
        bool MakeStructs()
        {
            var lines = State.Lines;
            var len = lines.Count;
            // get start,end pairs
            var pairs = new List<Tuple<int, int>>();
            for (var i = 0; i < len; ++i)
            {
                if (lines[i]?.Opcode?.Text == STRUCT_TEXT)
                {
                    var j = i + 1;
                    while (j < len && lines[j]?.Opcode.Text != END_STRUCT_TEXT)
                        ++j;
                    if (j < len && lines[j]?.Opcode.Text == END_STRUCT_TEXT)
                        pairs.Add(new Tuple<int, int>(i, j));
                    else
                    {
                        Error(lines[i], lines[i].Label, $"Struct {lines[i]} not closed");
                        return false;
                    }
                }
            }

            var structs = new List<Struct>();
            // parse pairs
            foreach (var p in pairs)
            {
                var (start, end) = (p.Item1, p.Item2);
                var nameToken = lines[start].Label;
                if (nameToken == null)
                {
                    Error(lines[start], nameToken, $"Missing struct label {lines[start]}");
                    return false;
                }

                var s = new Struct(lines[start]);
                for (var i = start + 1; i < end; ++i)
                    s.AddField(lines[i]);

                if (s.FieldCount <= 0)
                {
                    Error(lines[start], nameToken, $"Struct has no fields {lines[start]}");
                    return false;
                }
                structs.Add(s);
            }

            // ensure unique names
            for (var i = 0; i < structs.Count; ++i)
            for (var j = i + 1; j < structs.Count; ++j)
            {
                if (structs[i].Text == structs[j].Text)
                {
                    Error(structs[i].Line, structs[i].Line.Label, "Struct name cannot be duplicated.");
                    i++;
                    break;
                }

            }

            foreach (var s in structs)
                State.Symbols.AddSymbol(s, State.Output);

            // resolve substructs
            if (!State.Symbols.MakeLengths(State.Output))
                return false;


            // remove struct lines
            //var structLines = new List<Line>();
            pairs.Reverse(); // take higher lines out first
            foreach (var p in pairs)
            {
                var (start, end) = (p.Item1, p.Item2);
                var num = end - start + 1;
                //structLines.AddRange(lines.Skip(start).Take(num));
                lines.RemoveRange(start, num);
            }

            return true;
        }

        #endregion

    }
}
