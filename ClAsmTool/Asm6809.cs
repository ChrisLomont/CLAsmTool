using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Build.Tasks;

namespace Lomont.ClAsmTool
{
    class Asm6809
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

        void Preprocess()
        {
            var skipLines = false;
            // track define and skipLines state
            var ifStack = new Stack<Tuple<string,bool>>();
            // lines to remove from analysis
            var remove = new List<Line>();
            // things currently defined
            var defines = new HashSet<string>();
            foreach (var line in State.Lines)
            {
                if (skipLines)
                    remove.Add(line);
                if (line.Label == null)
                    continue;
                var label = line.Label.Text;

                if (label == "#define")
                {
                    var text = line.Opcode.Text;
                    defines.Add(text);
                    remove.Add(line);
                }
                else if (label == "#undef")
                {
                    var text = line.Opcode.Text;
                    defines.Remove(text);
                    remove.Add(line);
                }
                else if (label == "#ifdef")
                {
                    var text = line.Opcode.Text;
                    ifStack.Push(new Tuple<string, bool>(text,skipLines));
                    skipLines = !defines.Contains(text);
                    remove.Add(line);
                }
                else if (label == "#endif")
                {
                    if (!ifStack.Any())
                    {
                        Error(line,line.Label,"Unmatched #endif");
                        return;
                    }
                    var p = ifStack.Pop();
                    skipLines = p.Item2;
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
            Opcodes6809.MakeOpcodes(State.Output);

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
                        state.dpRegister = value;
                    else
                        Error(line, line.Operand, "Cannot evaluate DP");
                    break;
                case ".rom": // set CPU type
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
                case ".cpu": // set CPU type
                case ".meta":
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
            var mnemonic = line?.Opcode?.Text.ToLower()??"";
            // IDA-PRO uses some incorrect mnemonics left over from 6800 days
            if (mnemonic == "oraa") mnemonic = "ora";
            if (mnemonic == "orab") mnemonic = "orb";
            if (mnemonic == "ldaa") mnemonic = "lda";
            if (mnemonic == "ldab") mnemonic = "ldb";
            if (mnemonic == "staa") mnemonic = "sta";
            if (mnemonic == "stab") mnemonic = "stb";
            return mnemonic;
        }

        bool OpcodeHandled(AsmState state, Line line)
        {
            var mnemonic = GetMnemonic(line);
            var op = Opcodes6809.FindOpcode(mnemonic);
            if (op != null)
            {
                line.Address = state.Address;
                ParseOpcodeAndOperand(state, line, op);
                return true;
            }
            return false;
        }

        void ParseOpcodeAndOperand(AsmState state, Line line, Opcodes6809.Opcode op)
        {
            /* rules:
             * 1. empty operand  => Inherent mode
             * 2. starts with #  => Immediate
             * 3. relative       => Immediate (short and long branching, etc)
             * 4. all registers (for push,pull,exg,tfr)  => Immediate (Register sub format)
             * 5. expr, eval to DP range, or starts with '<' => Direct
             * 6. expr           => Extended 
             * 7. ,R or n,R or r,R or ,++R-- form, or [] form => Indexed (last Indexed Indirect, PC relative)
             */

            var addrMode = Opcodes6809.AddressingMode.Direct;
            var operand = line.Operand;
            var operandText = operand?.Text ?? "";
            var mnemonic = GetMnemonic(line);
            var (requiresRegisterList, isRegisterList) = CheckRegisterList(mnemonic, operandText);
            if (requiresRegisterList && !isRegisterList)
            {
                Error(line, operand, "Opcode requires register list");
                return;
            }

            var beginIndirect = operandText.StartsWith("[");
            var endIndirect = operandText.EndsWith("]");
            var indirect = beginIndirect && endIndirect;
            if (beginIndirect ^ endIndirect)
            {
                Error(line, line.Operand, "Illegal operand mode: unmatched brackets.");
                return;
            }


            var isBranch = Opcodes6809.IsBranch(mnemonic);
            var indexed = indirect || (operandText.Contains(",") && ! requiresRegisterList);

            if (String.IsNullOrEmpty(operand?.Text))
                addrMode = Opcodes6809.AddressingMode.Inherent;  // rule 1
            else if (operandText.StartsWith("#"))
                addrMode = Opcodes6809.AddressingMode.Immediate; // rule 2
            else if (isBranch)
                addrMode = Opcodes6809.AddressingMode.Immediate; // rule 3
            else if (requiresRegisterList)
                addrMode = Opcodes6809.AddressingMode.Immediate; // rule 4
            else if (!indexed && operandText.StartsWith("<"))    // todo - more modes - close value, for example
                addrMode = Opcodes6809.AddressingMode.Direct;    // rule 5 
            else if (indexed)
                addrMode = Opcodes6809.AddressingMode.Indexed;   // rule 6
            else 
                addrMode = Opcodes6809.AddressingMode.Extended;  // rule 7

            // now have addrMode, isBranch, requiresRegisterList, isRegisterList, indexed to decide


            // encode instruction and operand bytes
            if (!op.Forms.ContainsKey(addrMode))
            {
                Error(line, line.Operand, "Illegal operand mode");
                return;
            }
            var opMode                = op.Forms[addrMode];
            line.AddrMode = addrMode;
            line.Data.Clear(); // in case double called
            line.Data.AddRange(opMode.Bytes);
            line.Length   = Math.Abs(opMode.Length);
            // todo - lengths should be modified below

            int value;
            switch (addrMode)
            {
                case Opcodes6809.AddressingMode.Inherent:
                    // no bytes to add
                    break;
                case Opcodes6809.AddressingMode.Direct:
                {
                    var cleanedOperand = operandText;
                    if (cleanedOperand.StartsWith("<"))
                        cleanedOperand = cleanedOperand.Substring(1);
                    if (Evaluator.Evaluate(state, line,cleanedOperand, out value))
                        line.AddValue(value, 1);
                }
                    break;
                case Opcodes6809.AddressingMode.Immediate:
                    if (operandText.StartsWith("#"))
                    {
                        var len = opMode.Length - opMode.Bytes.Count;
                        if (Evaluator.Evaluate(state, line, operandText.Substring(1), out value))
                            line.AddValue(value, len);
                    }
                    else if (isBranch)
                    {
                        if (Evaluator.Evaluate(state, line, operandText, out value))
                        {
                            var relative = -(state.Address-value + line.Length);
                            var bitsRequired = BitsRequired(relative);
                            var longBranch = mnemonic.StartsWith("l");
                            if (longBranch)
                                line.AddValue(relative, 2);
                            else if (bitsRequired <= 8)
                                line.AddValue(relative, 1);
                            else
                            { // loop may not exist yet. track pass, and error on second one
                                line.NeedsFixup = true; 
                                if (state.Pass == 2)
                                    Error(line, operand, $"Operand {relative} out of 8 bit target range");
                                return;
                            }
                        }
                    }
                    else if (requiresRegisterList)
                    {
                        var stackInst = new [] {"pulu","pshu","puls","pshs" };
                        var tfrInst = new[] {"adcr","addr","andr","cmpr","eorr","exg","orr","sbcr","subr","tfr","tfm" };
                        var opError = false;
                        if (stackInst.Any(s=>s==mnemonic))
                        {
                            // order bit7=PC,S/U,Y,X,DP,B,A,CC=bit0
                            var order = new[] { "cc", "a", "b", "dp", "x", "y", "u", "pc" };
                            var regText = operandText; // assume s register
                            if (mnemonic.EndsWith("s"))
                                regText = operandText.Replace("s", "u"); 
                                                                         
                            var regs = regText.Split(',').Select(r=>r.Trim().ToLower());
                            if (regs.All(r => order.Contains(r)))
                            {
                                var val = regs.Select(r => 1 << Array.IndexOf(order, r))
                                    .Aggregate(0, (cur, nxt) => cur | nxt);
                                line.AddValue(val, 1);
                            }
                            else
                                opError = true;
                        }
                        else if (tfrInst.Any(s => s == mnemonic))
                        {
                            // d = 0000, ... f = 1111
                            var order = new[]
                                {"d", "x", "y", "u", "s", "pc", "w", "v", "a", "b", "cc", "dp", "0", "0", "e", "f"};
                            var regs = operandText.Split(',').Select(r => r.Trim().ToLower()).ToList();
                            if (regs.Count == 2 && regs.All(r => order.Contains(r)))
                            {
                                var val = regs.Select(r => Array.IndexOf(order, r))
                                    .Aggregate(0, (cur, nxt) => (cur <<4)| nxt);
                                line.AddValue(val, 1);
                            }
                            else
                                opError = true;

                        }
                        else
                            opError = true;

                        if (opError)
                            Error(line, operand, "Operand must be a list of registers");
                    }
                    break;
                case Opcodes6809.AddressingMode.Extended:
                    if (Evaluator.Evaluate(state, line, operandText, out value))
                        line.AddValue(value,2);
                    break; 
                case Opcodes6809.AddressingMode.Indexed:
                    HandleIndexedOperand(state, line, indirect);
                    break;
                default:
                    Error(line, line.Operand, $"Unknown addressing mode {addrMode}");
                    break;

                    // 1rrY0110 rr=01, Y=0 => 1010_0110=A6
            }
        }

        // return true if the mnemonic requires a register list and if there is one
        (bool requiresRegisterList, bool isRegisterList)
            CheckRegisterList(string mnemonic, string operandText)
        {
            var required = Opcodes6809.RequiresRegisterList(mnemonic);
            var regs1 = "abdxysu";
            var regs2 = "pc dp cc";
            // todo 6309 adds a few regs
            var all = 
                operandText.Split(',').Select(r => r.Trim().ToLower()).All(
                r=>(r.Length == 1 && regs1.Contains(r)) || (r.Length == 2 && regs2.Contains(r))
                );
            return (required, all);
        }

        // parse indexed addressing  mode
        void HandleIndexedOperand(AsmState state, Line line, bool indirect)
        {
            var text = line.Operand.Text;

            var indirectMask = 0;
            if (indirect)
            {
                text = text.Substring(1, text.Length - 2); // remove indexing, add back later
                indirectMask = 0b00010000;
            }

            int value;
            var parts = text.Split(',');
            if (parts.Length == 1 && indirect)
            {
                line.AddValue(0b10011111,1);
                line.Length += 2;
                if (Evaluator.Evaluate(state,line,parts[0], out value))
                    line.AddValue(value,2);
            }
            else if (parts.Length == 2)
            {
                var first = parts[0].Trim().ToLower();
                var second = parts[1].Trim().ToLower();
                var inc2 = second.EndsWith("++");
                var inc1 = second.EndsWith("+") && !inc2;
                var dec2 = second.StartsWith("--");
                var dec1 = second.StartsWith("-") && !dec2;
                var dec = dec1 || dec2;
                var inc = inc1 | inc2;
                if (inc && dec)
                {
                    Error(line, line.Operand, "Illegal operand. Cannot both inc and dec register");
                    return;
                }
                if (inc1)
                    second = second.Substring(0, second.Length - 1);
                if (inc2)
                    second = second.Substring(0, second.Length - 2);
                if (dec1)
                    second = second.Substring(1);
                if (dec2)
                    second = second.Substring(2);

                var secondRegOk = "xyus".Contains(second);
                int[] dstMasks = { 0b0_00_00000, 0b0_01_00000, 0b0_10_00000, 0b0_11_00000 };
                var dstMask = secondRegOk?dstMasks["xyus".IndexOf(second, StringComparison.Ordinal)]:0;


                if (second == "w" /* todo and 6309 enabled*/ )
                {
                    var maskW = indirect ? 0b00010000 : 0b00001111;

                    /* cases:
                     * blank,!dec&&!inc -> 0b10000000, 0 bytes
                     * val,!dec&!inc    -> 0b10100000, 2 bytes
                     * blank,inc2       -> 0b11000000, 0 bytes
                     * blank,dec2       -> 0b11100000, 0 bytes
                     */
                    var blank = first == "";
                    if (blank && !dec && !inc)
                        line.AddValue(0b10000000 | maskW,1);
                    else if (!blank && !dec && !inc)
                    {
                        line.Length += 2;
                        line.AddValue( 0b10100000| maskW, 1);
                        if (Evaluator.Evaluate(state, line, first, out value))
                            line.AddValue( value, 2);

                    }
                    else if (blank && inc2)
                        line.AddValue( 0b11000000 | maskW, 1);
                    else if (blank && dec2)
                        line.AddValue( 0b11100000 | maskW, 1);
                    else
                        Error(line, line.Operand, "Invalid operand");
                }
                else if (second == "pc" && !inc && !dec)
                {
                    if (Evaluator.Evaluate(state, line, first, out value))
                    {
                        if (BitsRequired(value)<=8)
                        {
                            line.Length += 1;
                            line.AddValue( 0b10001100 | indirectMask, 1);
                            line.AddValue( value, 1);
                        }
                        else
                        {
                            line.Length += 2;
                            line.AddValue( 0b10001101 | indirectMask, 1);
                            line.AddValue( value, 2);
                        }
                    }
                }
                else if (first.Length == 1 && "abefdw".Contains(first) && !inc && !dec && secondRegOk)
                { // todo - efw are 6309 only
                    int[] srcMasks = { 0b0110, 0b0101, 0b0111, 0b1010, 0b1011, 0b1110, };
                    var srcMask = srcMasks["abefdw".IndexOf(first,StringComparison.Ordinal)];
                    line.AddValue(0b1000_0000 | srcMask | dstMask | indirectMask,1);
                }
                else if (first.Length == 0 && (inc || dec))
                {
                    if (inc1)
                        line.AddValue( 0b10000000 | dstMask | indirectMask, 1);
                    else if (inc2)
                        line.AddValue( 0b10000001 | dstMask | indirectMask, 1);
                    else if (dec1)
                        line.AddValue( 0b10000010 | dstMask | indirectMask, 1);
                    else if (dec2)
                        line.AddValue( 0b10000011 | dstMask | indirectMask, 1);
                }
                else if (secondRegOk && !inc && !dec)
                {
                    if (first.Length == 0)
                        line.AddValue( 0b10000100 | dstMask | indirectMask, 1);
                    else
                    {
                        if (Evaluator.Evaluate(state, line, parts[0].Trim(), out value))
                        {
                            var bitLen = BitsRequired(value);
                            // todo - 5 bitlen ok, but not used in Robotron 2084
                            if (bitLen <= 5 && !indirect)
                            {
                                //Data mismatch at address 0x0269  ldb Step, y
                                //(Correct, ours): (E6, E6)(A4, 20)
                                // step = 3
                                //todo
                                //    0rrnnnnn  y=01
                                // 0010_0011
                                //
                                // 8 bit 1rrY1000
                                // 1010_1000
                                var bit5 = value & ((1 << 5) - 1);
                                line.AddValue( 0b0000000 | bit5 | dstMask, 1);
                            }
                            else if (bitLen <= 8)
                            {
                                line.Length += 1;
                                // 1rrY1000, rr=00, Y=1  1001_1000 = 98
                                line.AddValue( 0b1000_1000 | dstMask | indirectMask, 1);
                                line.AddValue( value, 1);
                            }
                            else
                            {
                                line.Length += 2;
                                line.AddValue(0b1000_1001 | dstMask | indirectMask, 1);
                                line.AddValue( value, 2);
                            }
                        }
                    }
                }
                else
                    Error(line, line.Operand,"Illegal operand format");
            }
            else // parts length != 1 and != 2
                Error(line, line.Operand, $"Illegal operand {line.Operand}");
        }

        // return 5, 8,  16 depending on how many bits needed for value
        // works on 16 bit value
        int BitsRequired(int value1)
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
            // 6809 CPU by default
            public bool Allow6309 = false; // todo - implement

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
