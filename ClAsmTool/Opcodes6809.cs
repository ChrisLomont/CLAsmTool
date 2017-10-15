using System;
using System.Collections.Generic;
using System.Linq;

namespace Lomont.ClAsmTool
{
    public static class Opcodes6809
    {
        enum AddressingMode
        {
            Unspecified,

            Inherent,  // ABX, DAA, etc.
            Immediate, // LDA #$2000
            Extended,  // (and extended indirect) LDA CAT, LDB ITEMADDR, LDB [BOB]
            Direct,    // LDA $20, LDD <CAT
            // Register,  // TFR X,Y, EXG A,B, PULU X,Y,D
            Indexed,   // (0-offset,const offset, acc offset, auto inc/dec, indexed indirect)
            // Relative   // (short, long, pc relative), LDA CAT, PCR; LEAX TBL, PCR; LDA [CAT,PCR], BNE LOOP
        }


        // find opcode, else return null
        public static Opcode FindOpcode(string mnemonic)
        {
            mnemonic = mnemonic.ToLower();
            foreach (var op in Opcodes)
                if (op.Mnemonic == mnemonic)
                    return op;
            return null;
        }

        /// <summary>
        /// Cleans some common opcodes to correct ones
        /// </summary>
        /// <param name="line"></param>
        /// <returns></returns>
        public static string FixupMnemonic(Line line)
        {
            var mnemonic = line?.Opcode?.Text.ToLower() ?? "";
            // IDA-PRO uses some incorrect mnemonics left over from 6800 days
            if (mnemonic == "oraa") mnemonic = "ora";
            if (mnemonic == "orab") mnemonic = "orb";
            if (mnemonic == "ldaa") mnemonic = "lda";
            if (mnemonic == "ldab") mnemonic = "ldb";
            if (mnemonic == "staa") mnemonic = "sta";
            if (mnemonic == "stab") mnemonic = "stb";
            if (FindOpcode(mnemonic) == null)
                return line?.Opcode?.Text ?? "";
            return mnemonic;
        }


       public static void ParseOpcodeAndOperand(Assembler.AsmState state, Line line, Opcode op)
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
            var mnemonic = Assembler.GetMnemonic(line);
            var (requiresRegisterList, isRegisterList) = CheckRegisterList(mnemonic, operandText);
            if (requiresRegisterList && !isRegisterList)
            {
                state.Output.Error(line, operand, "Opcode requires register list");
                return;
            }

            var beginIndirect = operandText.StartsWith("[");
            var endIndirect = operandText.EndsWith("]");
            var indirect = beginIndirect && endIndirect;
            if (beginIndirect ^ endIndirect)
            {
                state.Output.Error(line, line.Operand, "Illegal operand mode: unmatched brackets.");
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
            if (!op.Forms.ContainsKey((int)addrMode))
            {
                state.Output.Error(line, line.Operand, "Illegal operand mode");
                return;
            }
            var opMode                = op.Forms[(int)addrMode];
            line.AddressingMode = (int)addrMode;
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
                            var bitsRequired = Assembler.BitsRequired(relative);
                            var longBranch = mnemonic.StartsWith("l");
                            if (longBranch)
                                line.AddValue(relative, 2);
                            else if (bitsRequired <= 8)
                                line.AddValue(relative, 1);
                            else
                            { // loop may not exist yet. track pass, and error on second one
                                line.NeedsFixup = true; 
                                if (state.Pass == 2)
                                    state.Output.Error(line, operand, $"Operand {relative} out of 8 bit target range");
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
                            state.Output.Error(line, operand, "Operand must be a list of registers");
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
                    state.Output.Error(line, line.Operand, $"Unknown addressing mode {addrMode}");
                    break;

                    // 1rrY0110 rr=01, Y=0 => 1010_0110=A6
            }
        }

        // return true if the mnemonic requires a register list and if there is one
        static (bool requiresRegisterList, bool isRegisterList)
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
        static void HandleIndexedOperand(Assembler.AsmState state, Line line, bool indirect)
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
                    state.Output.Error(line, line.Operand, "Illegal operand. Cannot both inc and dec register");
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
                        state.Output.Error(line, line.Operand, "Invalid operand");
                }
                else if (second == "pc" && !inc && !dec)
                {
                    if (Evaluator.Evaluate(state, line, first, out value))
                    {
                        if (Assembler.BitsRequired(value)<=8)
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
                            var bitLen = Assembler.BitsRequired(value);
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
                    state.Output.Error(line, line.Operand,"Illegal operand format");
            }
            else // parts length != 1 and != 2
                state.Output.Error(line, line.Operand, $"Illegal operand {line.Operand}");
        }


        #region Opcodes



        public static Opcode [] Opcodes { get; private set; }

        public static void MakeOpcodes(Output output)
        {
            // tables start with " ____";
            var tblsToParse = new int[] {1, 2, 3, 4, 5, 6, 8};
            // word lengths
            // 9 - most tables
            // 5 - transfer ops
            // 8 - 6309 logical mem ops
            var okWordLengths = new int[] {9, 5, 8};

            var opcodes = new List<Opcode>();
            var tableIndex = 0;
            var parseTable = false;
            foreach (var line in cpuDefs)
            {
                if (line.StartsWith(" ____"))
                {
                    ++tableIndex;
                    parseTable = tblsToParse.Contains(tableIndex);
                }
                if (!parseTable)
                    continue;
                if (line[0] == '|')
                {
                    var words = line.Split('|');
                    words = words.Select(w => w.Trim()).ToArray();
                    if (!okWordLengths.Contains(words.Length) || String.IsNullOrEmpty(words[1]))
                        continue;
                    var mnem = words[1].ToLower();
                    if (mnem == "mnem" || mnem.Length > 5)
                        continue;

                    if (words.Length == 5)
                    {
                        // transfer ops
                        var op = MakeOpcode(mnem);
                        op.Forms.Add((int)AddressingMode.Direct, Parse(words[3]));
                        opcodes.Add(op);
                    }
                    else if (words.Length == 8)
                    {
                        // 6309 logical mem ops
                        var op = MakeOpcode(mnem);
                        op.Forms.Add((int)AddressingMode.Direct, Parse(words[3]));
                        op.Forms.Add((int)AddressingMode.Indexed, Parse(words[4]));
                        op.Forms.Add((int)AddressingMode.Extended, Parse(words[5]));
                        opcodes.Add(op);
                    }

                    else if (words[3].StartsWith("LB"))
                    {
                        // two branch ops
                        var opcode = MakeOpcode(mnem);
                        opcode.Forms.Add((int)AddressingMode.Immediate, Parse(words[2], BranchLength(mnem)));
                        opcodes.Add(opcode);

                        mnem = words[3].ToLower();
                        opcode = MakeOpcode(mnem);
                        opcode.Forms.Add((int)AddressingMode.Immediate, Parse(words[4], BranchLength(mnem)));
                        opcodes.Add(opcode);
                    }
                    else
                    {
                        var opcode = MakeOpcode(mnem);
                        var immed = words[2].ToLower();
                        var direct = words[3].ToLower();
                        var indexed = words[4].ToLower();
                        var extended = words[5].ToLower();
                        var inherent = words[6].ToLower();

                        if (!String.IsNullOrEmpty(immed))
                            opcode.Forms.Add((int)AddressingMode.Immediate, Parse(immed));
                        if (!String.IsNullOrEmpty(direct))
                            opcode.Forms.Add((int)AddressingMode.Direct, Parse(direct));
                        if (!String.IsNullOrEmpty(indexed))
                            opcode.Forms.Add((int)AddressingMode.Indexed, Parse(indexed));
                        if (!String.IsNullOrEmpty(extended))
                            opcode.Forms.Add((int)AddressingMode.Extended, Parse(extended));
                        if (!String.IsNullOrEmpty(inherent))
                            opcode.Forms.Add((int)AddressingMode.Inherent, Parse(inherent));
                        opcodes.Add(opcode);
                    }


                }
            }

            // todo - merge duplicates
            //for (var i =0; i < opcodes.Count; ++i)
            //for (var j = i + 1; j < opcodes.Count; ++j)
            //{
            //    if (opcodes[i].Mnemonic == opcodes[j].Mnemonic)
            //        WriteLine($"Duplicate opcode {opcodes[i].Mnemonic}");
            //
            //}

            Opcodes = opcodes.OrderBy(o=>o.Mnemonic).ToArray();

            Opcode MakeOpcode(string mnem)
            {
                var opcode = new Opcode();
                if (mnem[0] == '*')
                {
                    // todo opcode.Only6309 = true;
                    mnem = mnem.Substring(1);
                }
                opcode.Mnemonic = mnem;
                return opcode;
            }




            OpcodeFormat Parse(string entry, int branchLength = -1)
            {
                var op = new OpcodeFormat();
                var w = entry.Split(new char[] {' '}, StringSplitOptions.RemoveEmptyEntries);
                if (branchLength == -1 && w.Length != 3)
                {
                    output.Error($"Unknown opcode parse {entry} should be 3 entries");
                    return op;
                }
                if (w[0].StartsWith("!"))
                {
                    w[0] = w[0].Substring(1);
                    op.Bytes.Add(0x10);
                }
                if (entry.StartsWith("@"))
                {
                    w[0] = w[0].Substring(1);
                    op.Bytes.Add(0x11);
                }
                var val = ParseHex(w[0]);
                if (val < 0 || 255 < val)
                {
                    output.Error($"Invalid opcode hex {w[0]}");
                    return op;
                }
                op.Bytes.Add((byte)val);
                if (branchLength == -1)
                    op.Length = ParseHex(w[2]);
                else
                    op.Length = branchLength;

                return op;
            }

            int BranchLength(string mnem)
            {
                // "3 - BSR and LBSR cycles in table ->              | B??    |   3   2 |         ",
                // "4 - (L)BHS and (L)BCC are the same               | LB??   |! 5/6  4 |         ",
                // "5 - (L)BCS and (L)BLO are the same               | BRA    |   3   2 |         ",
                // "S - Signed                                       | LBRA   |  5/4  3 |         ",
                // "U - Unsigned                                     | BSR    |  7/6  2 |         ",
                // "M - siMple - tests single condition code.        | LBSR   |  9/7  3 |         ",
                if (mnem == "lbsr" || mnem == "lbra")
                    return 3;
                if (mnem[0] == 'b')
                    return 2;
                if (mnem.StartsWith("lb"))
                    return 4;
                output.Error($"Unknown branch length {mnem}");
                return 2;
            }

            // Parse hex from a string, of form
            // 0xnnn 0Xnnn or simply nnn
            // returns -n if string followd by '+'
            int ParseHex(string hexString)
            {
                var neg = false;
                if (hexString.EndsWith("+"))
                {
                    neg = true;
                    hexString = hexString.Substring(0, hexString.Length - 1);
                }
                var val = Convert.ToInt32(hexString, 16);
                if (neg) val = -val;
                return val;
            }

        }


        static string[] cpuDefs =
        {
            "                   6809/6309 Assembly and Mnemonic Information                ",
            "  Compiled and edited by Chris Lomont, www.lomont.org. Version 1.2 May 2007   ",
            "                                                                              ",
            "* denotes 6309 only instruction, ~/~ is cycle counts on 6809/6309, # is bytes,",
            " ~ and # can be increased by addressing and other factors, see throughout     ",
            "!  prefix opcode with 10, @  prefix opcode with 11, e.g., !8B is opcode 10 8B ",
            "CCodes condition codes (6809 only for now): * affected, - not, ? indeterminate",
            "         I is interrupt flag: E = bit 7, F=FIRQ bit6, I IRQ bit 4, notes later",
            "           Indexed cycle counts and byte length may modified by mode          ",
            " ____________________________________________________________________________ ",
            "|  Mnem  |   Immed.  |   Direct  |   Indexed  |  Extended |  Inherent |CCodes|",
            "|        |           |           |            |           |           | 53210|",
            "|        | OP  ~/~  #| OP  ~/~  +| OP  ~/~  # | OP  ~/~  #| OP  ~/~  #|IHNZVC|",
            "|--------+-----------+-----------+------------+-----------+-----------+------|",
            "| ABX    |           |           |            |           | 3A  3/1  1|------|",
            "| ADCA   | 89   2   2| 99  4/3  2| A9  4+   2+| B9  5/4  3|           |-*****|",
            "| ADCB   | C9   2   2| D9  4/3  2| E9  4+   2+| F9  5/3  3|           |-*****|",
            "|*ADCD   | !89 5/4  4|!99  7/5  3|!A9 7+/6+ 3+|!B9  8/6  4|           |      |",
            "|--------+-----------+-----------+------------+-----------+-----------+------|",
            "| ADDA   | 8B   2   2| 9B  4/3  2| AB  4+   2+| BB  5/4  3|           |-*****|",
            "| ADDB   | CB   2   2| DB  4/3  2| EB  4+   2+| FB  5/4  3|           |-*****|",
            "| ADDD   | C3  4/3  3| D3  6/4  2| E3 6+/5+ 2+| F3  7/5  3|           |-*****|",
            "|*ADDE   |@8B   3   3|@9B  5/4  3|@AB  5+   3+|@BB  6/5  4|           |      |",
            "|*ADDF   |@CB   3   3|@DB  5/4  3|@EB  5+   3+|@FB  6/5  4|           |      |",
            "|*ADDW   |!8B  5/4  4|!9B  7/5  3|!AB 7+/6+ 3+|!BB  8/6  4|           |      |",
            "|--------+-----------+-----------+------------+-----------+-----------+------|",
            "|*AIM    |           | 02   6   3| 62   7+  3+| 72   7   4|           |      |",
            "|--------+-----------+-----------+------------+-----------+-----------+------|",
            "| ANDA   | 84   2   2| 94  4/3  2| A4   4+  2+| B4  5/4  3|           |--**0-|",
            "| ANDB   | C4   2   2| D4  4/3  2| E4   4+  2+| F4  5/4  3|           |--**0-|",
            "| ANDCC  | 1C   3   2|           |            |           |           |?????1|",
            "|*ANDD   |!84  5/4  4|!94  7/5  3|!A4 7+/6+ 3+|!B4  8/6  4|           |      |",
            "|--------+-----------+-----------+------------+-----------+-----------+------|",
            "| ASLA   |           |           |            |           | 48  2/1  1|--****|",
            "| ASLB   |           |           |            |           | 58  2/1  1|--****|",
            "|*ASLD   |           |           |            |           |!48  3/2  2|      |",
            "| ASL    |           | 08  6/5  2| 68  6+   2+| 78  7/6  3|           |--****|",
            "|--------+-----------+-----------+------------+-----------+-----------+------|",
            "| ASRA   |           |           |            |           | 47  2/1  1|--****|",
            "| ASRB   |           |           |            |           | 57  2/1  1|--****|",
            "|*ASRD   |           |           |            |           |!47  3/2  2|      |",
            "| ASR    |           | 07  6/6  2| 67  6+   2+| 77  7/6  3|           |--****|",
            "|--------+-----------+-----------+------------+-----------+-----------+------|",
            "| BITA   | 85   2   2| 95  4/3  2| A5   4+  2+| B5  5/4  3|           |--**0-|",
            "| BITB   | C5   2   2| D5  4/3  2| E5   4+  2+| F5  5/4  3|           |--**0-|",
            "|*BITD   |!85  5/4  4|!95  7/5  3|!A5 7+/6+ 3+|!B5  8/6  4|           |      |",
            "|*BITMD  |@3C   4   3|           |            |           |           |      |",
            "|--------+-----------+-----------+------------+-----------+-----------+------|",
            "| CLRA   |           |           |            |           | 4F  2/1  1|--0100|",
            "| CLRB   |           |           |            |           | 5F  2/1  1|--0100|",
            "|*CLRD   |           |           |            |           |!4F  3/2  2|      |",
            "|*CLRE   |           |           |            |           |@4F  3/2  2|      |",
            "|*CLRF   |           |           |            |           |@5F  3/2  2|      |",
            "|*CLRW   |           |           |            |           |!5F  3/2  2|      |",
            "| CLR    |           | 0F  6/5  2| 6F   6+  2+| 7F  7/6  3|           |--0100|",
            "|--------+-----------+-----------+------------+-----------+-----------+------|",
            "| CMPA   | 81   2   2| 91  4/3  2| A1   4+  2+| B1  5/4  3|           |--****|",
            "| CMPB   | C1   2   2| D1  4/3  2| E1   4+  2+| F1  5/4  3|           |--****|",
            "| CMPD   |!83  5/4  4|!93  7/5  3|!A3 7+/6+ 3+|!B3  8/6  4|           |--****|",
            "|*CMPE   |@81   3   3|@91  5/4  3|@A1   5+  3+|@B1  6/5  4|           |--****|",
            "|*CMPF   |@C1   3   3|@D1  5/4  3|@E1   5+  3+|@F1  6/5  4|           |--****|",
            "| CMPS   |@8C  5/4  4|@9C  7/5  3|@AC 7+/6+ 3+|@BC  8/6  4|           |--****|",
            "| CMPU   |@83  5/4  4|@93  7/5  3|@A3 7+/6+ 3+|@B3  8/6  4|           |--****|",
            "|*CMPW   |!81  5/4  4|!91  7/5  3|!A1 7+/6+ 3+|!B1  8/6  4|           |--****|",
            "| CMPX   | 8C  4/3  3| 9C  6/4  2| AC 6+/5+ 2+| BC  7/5  3|           |--****|",
            "| CMPY   |!8C  5/4  4|!9C  7/5  3|!AC 7+/6+ 3+|!BC  8/6  4|           |--****|",
            " ---------------------------------------------------------------------------- ",
            "                                                                              ",
            " ____________________________________________________________________________ ",
            "|  Mnem  |   Immed.  |   Direct  |   Indexed  |  Extended |  Inherent |CCodes|",
            "|        |           |           |            |           |           | 53210|",
            "|        | OP  ~/~  #| OP  ~/~  +| OP  ~/~  # | OP  ~/~  #| OP  ~/~  #|IHNZVC|",
            "|--------+-----------+-----------+------------+-----------+-----------+------|",
            "| COMA   |           |           |            |           | 43  2/1  1|--**01|",
            "| COMB   |           |           |            |           | 53  2/1  1|--**01|",
            "|*COMD   |           |           |            |           |!43  3/2  2|      |",
            "|*COME   |           |           |            |           |@43  3/2  2|      |",
            "|*COMF   |           |           |            |           |@53  3/2  2|      |",
            "|*COMW   |           |           |            |           |!53  3/2  2|      |",
            "| COM    |           | 03  6/5  2| 63   6+  2+| 73  7/6  3|           |--**01|",
            "|--------+-----------+-----------+------------+-----------+-----------+------|",
            "| CWAI   | 3C 22/20 2|           |            |           |           |E?????|",
            "|--------+-----------+-----------+------------+-----------+-----------+------|",
            "| DAA    |           |           |            |           | 19  2/1  1|--****|",
            "|--------+-----------+-----------+------------+-----------+-----------+------|",
            "| DECA   |           |           |            |           | 4A  2/1  1|--***-|",
            "| DECB   |           |           |            |           | 5A  2/1  1|--***-|",
            "|*DECD   |           |           |            |           |!4A  3/2  2|      |",
            "|*DECE   |           |           |            |           |@4A  3/2  2|      |",
            "|*DECF   |           |           |            |           |@5A  3/2  2|      |",
            "|*DECW   |           |           |            |           |!5A  3/2  2|      |",
            "| DEC    |           | 0A  6/5  2| 6A   6+  2+| 7A  7/6  3|           |--***-|",
            "|--------+-----------+-----------+------------+-----------+-----------+------|",
            "|*DIVD   |@8D   25  3|@9D 27/26 3|@AD  27+  3+|@BD 28/27 4|           |      |",
            "|*DIVQ   |@8E   34  4|@9E 36/35 3|@AE  36+  3+|@BE 37/36 4|           |      |",
            "|--------+-----------+-----------+------------+-----------+-----------+------|",
            "|*EIM    |           | 05   6   3| 65   7+  3+| 75   7   4|           |      |",
            "|--------+-----------+-----------+------------+-----------+-----------+------|",
            "| EORA   | 88   2   2| 98  4/3  2| A8   4+  2+| B8  5/4  3|           |--**0-|",
            "| EORB   | C8   2   2| D8  4/3  2| E8   4+  2+| F8  5/4  3|           |--**0-|",
            "|*EORD   |!88  5/4  4|!98  7/5  3|!A8 7+/6+ 3+|!B8  8/6  4|           |      |",
            "|--------+-----------+-----------+------------+-----------+-----------+------|",
            "| EXG    | 1E  8/5  2|           |            |           |           |------|",
            "|--------+-----------+-----------+------------+-----------+-----------+------|",
            "| INCA   |           |           |            |           | 4C  2/1  1|--***-|",
            "| INCB   |           |           |            |           | 5C  2/1  1|--***-|",
            "|*INCD   |           |           |            |           |!4C  3/2  2|      |",
            "|*INCE   |           |           |            |           |@4C  3/2  2|      |",
            "|*INCF   |           |           |            |           |@5C  3/2  2|      |",
            "|*INCW   |           |           |            |           |!5C  3/2  2|      |",
            "| INC    |           | 0C  6/5  2| 6C   6+  2+| 7C  7/6  3|           |--***-|",
            "|--------+-----------+-----------+------------+-----------+-----------+------|",
            "| JMP    |           | 0E  3/2  2| 6E   3+  2+| 7E  4/3  3|           |------|",
            "|--------+-----------+-----------+------------+-----------+-----------+------|",
            "| JSR    |           | 9D  7/6  2| AD 7+/6+ 2+| BD  8/7  3|           |------|",
            "|--------+-----------+-----------+------------+-----------+-----------+------|",
            "| LDA    | 86   2   2| 96  4/3  2| A6   4+  2+| B6  5/4  3|           |--**0-|",
            "| LDB    | C6   2   2| D6  4/3  2| E6   4+  2+| F6  5/4  3|           |--**0-|",
            "| LDD    | CC   3   3| DC  5/4  2| EC   5+  2+| FC  6/5  3|           |--**0-|",
            "|*LDE    |@86   3   3|@96  5/4  3|@A6   5+  3+|@B6  6/5  4|           |      |",
            "|*LDF    |@C6   3   3|@D6  5/4  3|@E6   5+  3+|@F6  6/5  4|           |      |",
            "|*LDQ    | CD   5   5|!DC  8/7  3|!EC   8+  3+|!FC  9/8  4|           |      |",
            "| LDS    |!CE   4   4|!DE  6/5  3|!EE   6+  3+|!FE  7/6  4|           |--**0-|",
            "| LDU    | CE   3   3| DE  5/4  2| EE   5+  2+| FE  6/5  3|           |--**0-|",
            "|*LDW    |!86   4   4|!96  6/5  3|!A6   6+  3+|!B6  7/6  4|           |      |",
            "| LDX    | 8E   3   3| 9E  5/4  2| AE   5+  2+| BE  6/5  3|           |--**0-|",
            "| LDY    |!8E   4   4|!9E  6/5  3|!AE   6+  3+|!BE  7/6  4|           |--**0-|",
            "|*LDMD   |@3D   5   3|           |            |           |           |      |",
            "|--------+-----------+-----------+------------+-----------+-----------+------|",
            "| LEAS   |           |           | 32   4+  2+|           |           |---i--|",
            "| LEAU   |           |           | 33   4+  2+|           |           |---i--|",
            "| LEAX   |           |           | 30   4+  2+|           |           |---i--|",
            "| LEAY   |           |           | 31   4+  2+|           |           |---i--|",
            " ---------------------------------------------------------------------------- ",
            " ____________________________________________________________________________ ",
            "|  Mnem  |   Immed.  |   Direct  |   Indexed  |  Extended |  Inherent |CCodes|",
            "|        |           |           |            |           |           | 53210|",
            "|        | OP  ~/~  #| OP  ~/~  +| OP  ~/~  # | OP  ~/~  #| OP  ~/~  #|IHNZVC|",
            "|--------+-----------+-----------+------------+-----------+-----------+------|",
            "| LSLA/LSLB/LSLD/LSL  Same as ASL                                     |--0***|",
            "|--------+-----------+-----------+------------+-----------+-----------+------|",
            "| LSRA   |           |           |            |           | 44  2/1  1|--0***|",
            "| LSRB   |           |           |            |           | 54  2/1  1|--0***|",
            "|*LSRD   |           |           |            |           |!44  3/2  2|      |",
            "|*LSRW   |           |           |            |           |!54  3/2  2|      |",
            "| LSR    |           | 04  6/5  2| 64   6+  2+| 74  7/6  3|           |--0***|",
            "|--------+-----------+-----------+------------+-----------+-----------+------|",
            "| MUL    |           |           |            |           | 3D 11/10 1|---*-*|",
            "|*MULD   |@8F   28  4|@9F 30/29 3|@AF  30+  3+|@BF 31/30 4|           |      |",
            "|--------+-----------+-----------+------------+-----------+-----------+------|",
            "| NEGA   |           |           |            |           | 40  2/1  1|-?****|",
            "| NEGB   |           |           |            |           | 50  2/1  1|-?****|",
            "|*NEGD   |           |           |            |           |!40  3/2  2|      |",
            "| NEG    |           | 00  6/5  2| 60   6+  2+| 70  7/6  3|           |-?****|",
            "|--------+-----------+-----------+------------+-----------+-----------+------|",
            "| NOP    |           |           |            |           | 12  2/1  1|------|",
            "|--------+-----------+-----------+------------+-----------+-----------+------|",
            "|*OIM    |           | 01   6   3| 61   7+  3+| 71   7   4|           |      |",
            "|--------+-----------+-----------+------------+-----------+-----------+------|",
            "| ORA    | 8A   2   2| 9A  4/3  2| AA   4+  2 | BA  5/4  3|           |--**0-|",
            "| ORB    | CA   2   2| DA  4/3  2| EA   4+  2 | FA  5/4  3|           |--**0-|",
            "| ORCC   | 1A  3/2  2|           |            |           |           |??????|",
            "|*ORD    |!8A  5/4  4|!9A  7/5  3|!AA 7+/6+ 3+|!BA  8/6  4|           |      |",
            "|--------+-----------+-----------+------------+-----------+-----------+------|",
            "| PSHS   | 34 5+/4+ 2|           |            |           |           |------|",
            "| PSHU   | 36 5+/4+ 2|           |            |           |           |------|",
            "|*PSHSW  |!38   6   2|           |            |           |           |      |",
            "|*PSHUW  |!3A   6   2|           |            |           |           |      |",
            "|--------+-----------+-----------+------------+-----------+-----------+------|",
            "| PULS   | 35 5+/4+ 2|           |            |           |           |??????|",
            "| PULU   | 37 5+/4+ 2|           |            |           |           |??????|",
            "|*PULSW  |!39   6   2|           |            |           |           |      |",
            "|*PULUW  |!3B   6   2|           |            |           |           |      |",
            "|--------+-----------+-----------+------------+-----------+-----------+------|",
            "| ROLA   |           |           |            |           | 49  2/1  1|--****|",
            "| ROLB   |           |           |            |           | 59  2/1  1|--****|",
            "|*ROLD   |           |           |            |           |!49  3/2  2|      |",
            "|*ROLW   |           |           |            |           |!59  3/2  2|      |",
            "| ROL    |           | 09  6/5  2| 69   6+  2+| 79  7/6  3|           |--****|",
            "|--------+-----------+-----------+------------+-----------+-----------+------|",
            "| RORA   |           |           |            |           | 46  2/1  1|--****|",
            "| RORB   |           |           |            |           | 56  2/1  1|--****|",
            "|*RORD   |           |           |            |           |!46  3/2  2|      |",
            "|*RORW   |           |           |            |           |!56  3/2  2|      |",
            "| ROR    |           | 06  6/5  2| 66   6+  2+| 76  7/6  3|           |--****|",
            "|--------+-----------+-----------+------------+-----------+-----------+------|",
            "| RTI    |           |           |            |           | 3B  6/17 1|-*****|",
            "|        |           |           |            |           |    15/17  |      |",
            "|--------+-----------+-----------+------------+-----------+-----------+------|",
            "| RTS    |           |           |            |           | 39  5/4  1|------|",
            "|--------+-----------+-----------+------------+-----------+-----------+------|",
            "| SBCA   | 82   2   2| 92  4/3  2| A2   4+  2+| B2  5/4  3|           |--****|",
            "| SBCB   | C2   2   2| D2  4/3  2| E2   4+  2+| F2  5/2  3|           |--****|",
            "|*SBCD   |!82  5/4  4|!92  7/5  3|!A2 7+/6+ 3+|!B2  8/6  4|           |      |",
            "|--------+-----------+-----------+------------+-----------+-----------+------|",
            "| SEX    |           |           |            |           | 1D  2/1  1|--**--|",
            "|*SEXW   |           |           |            |           | 14   4   1|      |",
            " ---------------------------------------------------------------------------- ",
            "                                                                              ",
            "                                                                              ",
            "                                                                              ",
            " ____________________________________________________________________________ ",
            "|  Mnem  |   Immed.  |   Direct  |   Indexed  |  Extended |  Inherent |CCodes|",
            "|        |           |           |            |           |           | 53210|",
            "|        | OP  ~/~  #| OP  ~/~  +| OP  ~/~  # | OP  ~/~  #| OP  ~/~  #|IHNZVC|",
            "|--------+-----------+-----------+------------+-----------+-----------+------|",
            "| STA    |           | 97  4/3  2| A7   4+  2+| B7  5/4  3|           |--**0-|",
            "| STB    |           | D7  4/3  2| E7   4+  2+| F7  5/4  3|           |--**0-|",
            "| STD    |           | DD  5/4  2| ED   5+  2+| FD  6/5  3|           |--**0-|",
            "|*STE    |           |@97  5/4  3|@A7   5+  3+|@B7  6/5  4|           |      |",
            "|*STF    |           |@D7  5/4  3|@E7   5+  3+|@F7  6/5  4|           |      |",
            "|*STQ    |           |!DD  8/7  3|!ED   8+  3+|!FD  9/8  4|           |      |",
            "|*STS    |           |!DF  6/5  3|!EF   6+  3+|!FF  7/6  4|           |      |",
            "| STU    |           | DF  5/4  2| EF   5+  2+| FF  6/5  3|           |--**0-|",
            "|*STW    |           |!97  6/5  3|!A7   6+  3+|!B7  7/6  4|           |      |",
            "| STX    |           | 9F  5/4  2| AF   5+  2+| BF  6/5  3|           |--**0-|",
            "| STY    |           |!9F  6/5  3|!AF   6+  3+|!BF  7/6  4|           |--**0-|",
            "|--------+-----------+-----------+------------+-----------+-----------+------|",
            "| SUBA   | 80   2   2| 90  4/3  2| A0   4+  2+| B0  5/4  3|           |--****|",
            "| SUBB   | C0   2   2| D0  4/3  2| E0   4+  2+| F0  5/4  3|           |--****|",
            "| SUBD   | 83  4/3  3| 93  6/4  2| A3 6+/5+ 2+| B3  7/5  3|           |--****|",
            "|*SUBE   |@80   3   3|@90  5/4  3|@A0   5+  3+|@B0  6/5  4|           |      |",
            "|*SUBF   |@C0   3   3|@D0  5/4  3|@E0   5+  3+|@F0  6/5  4|           |      |",
            "|*SUBW   |!80  5/4  4|!90  7/5  3|!A0 7+/6+ 3+|!B0  8/6  4|           |      |",
            "|--------+-----------+-----------+------------+-----------+-----------+------|",
            "| SWI    |           |           |            |           | 3F 19/21 1|1-----|",
            "| SWI2   |           |           |            |           |!3F 20/22 2|E-----|",
            "| SWI3   |           |           |            |           |@3F 20/22 2|E-----|",
            "|--------+-----------+-----------+------------+-----------+-----------+------|",
            "| SYNC   |           |           |            |           | 13 2+/1+ 1|------|",
            "|--------+-----------+-----------+------------+-----------+-----------+------|",
            "| TFR    | 1F  6/4  2|           |            |           |           |------|",
            "|--------+-----------+-----------+------------+-----------+-----------+------|",
            "|*TIM    |           | 0B   6   3| 6B   7+  3+| 7B   5   4|           |      |",
            "|--------+-----------+-----------+------------+-----------+-----------+------|",
            "| TSTA   |           |           |            |           | 4D  2/1  1|--**0-|",
            "| TSTB   |           |           |            |           | 5D  2/1  1|--**0-|",
            "|*TSTD   |           |           |            |           |!4D  3/2  2|      |",
            "|*TSTE   |           |           |            |           |@4D  3/2  2|      |",
            "|*TSTF   |           |           |            |           |@5D  3/2  2|      |",
            "|*TSTW   |           |           |            |           |!5D  3/2  2|      |",
            "| TST    |           | 0D  6/4  2| 6D 6+/5+ 2+| 7D  7/5  3|           |--**0-|",
            " ---------------------------------------------------------------------------- ",
            "                                                                              ",
            "                        Bit Transfer/Manipulation                             ",
            "AND,AND NOT, OR,OR NOT, ...: instr, post byte, memory location                ",
            "                                  Post-Byte                                   ",
            " _____________________           --------------------------                   ",
            "|  Mnem  |   Direct   |         | 7  6 | 5  4  3 | 2  1  0 |                  ",
            "|        |            |          --------------------------                   ",
            "|        | OP  ~/~  # |           Bits 7 and 6: Register                      ",
            "|--------+------------|             00 - CC   10 - B                          ",
            "|*BAND   |@30  7/6  4 |             01 - A    11 - Unused                     ",
            "|*BIAND  |@31  7/6  4 |           Bits 5, 4 and 3: Source Bit                 ",
            "|*BOR    |@32  7/6  4 |           Bits 2, 1 and 0: Destination bit            ",
            "|*BIOR   |@33  7/6  4 |                                                       ",
            "|*BEOR   |@34  7/6  4 |         Source/Destination Bit in binary form:        ",
            "|*BIEOR  |@35  7/6  4 |           0 - 000   2 - 010   4 - 100    6 - 110      ",
            "|*LDBT   |@36  7/6  4 |           1 - 001   3 - 011   5 - 101    7 - 111      ",
            "|*STBT   |@37  8/7  4 |                                                       ",
            " ---------------------                                                        ",
            "                                                                              ",
            "    Both the source and destination bit portions of the post-byte are looked  ",
            "at by the 6309 as the actual bit NUMBER to transfer/store. Use the binary     ",
            "equivalent of the numbers (0 thru 7) and position them into the bit area of   ",
            "the post byte. Ex: BAND A,1,3,240.                                            ",
            "                                                                              ",
            "                                                                              ",
            "                            Branch Instructions                               ",
            " __________________________________________________________________________   ",
            "| Mnem |    | Mnem |    | Description        | Condition           | Notes |  ",
            "|      |    |      |    |                    |                     |       |  ",
            "|      | OP |      | OP |                    |                     |   1   |  ",
            "|------+----|------+----+--------------------+---------------------+-------|  ",
            "| BCC  | 24 | LBCC |!24 | Carry Clear        |        !C           | M,U,4 |  ",
            "| BCS  | 25 | LBCS |!25 | Carry Set          |         C           | M,U,5 |  ",
            "| BEQ  | 27 | LBEQ |!27 | Equal              |         Z           | M,S,U |  ",
            "| BGE  | 2C | LBGE |!2C | Greater Or Equal   |     N*V + !N*!V     | S     |  ",
            "| BGT  | 2E | LBGT |!2E | Greater Than       |  N*V*!Z + !N*!V*!Z  | S     |  ",
            "| BHI  | 22 | LBHI |!22 | Higher             |       !C*!Z         | U     |  ",
            "| BHS  | 2F | LBHS |!2F | Higher Or Same     |        !C           | U,4   |  ",
            "| BLE  | 2F | LBLE |!2F | Less Than Or Equal |  Z + N*!V + !N*V    | S     |  ",
            "| BLO  | 25 | LBLO |!25 | Lower              |         C           | U,5   |  ",
            "| BLS  | 23 | LBLS |!23 | Lower Or Same      |       C + Z         | U     |  ",
            "| BLT  | 2D | LBLT |!2D | Less Than          |    N*!V + !N*V      | S     |  ",
            "| BMI  | 2B | LBMI |!2B | Minus (Negative)   |         N           | M     |  ",
            "| BNE  | 26 | LBNE |!26 | Not Equal          |        !Z           | M,S,U |  ",
            "| BPL  | 2A | LBPL |!2A | Plus (Positive)    |        !N           | M     |  ",
            "| BRA  | 20 | LBRA | 16 | Always             |         1           | O,2   |  ",
            "| BRN  | 21 | LBRN |!21 | Never              |         0           | O     |  ",
            "| BSR  | 8D | LBSR | 17 | Subroutine         |         1           | O,3   |  ",
            "| BVC  | 28 | LBVC |!28 | Overflow Clear     |        !V           | M,S   |  ",
            "| BVS  | 29 | LBVS |!29 | Overflow Set       |         V           | M,S   |  ",
            " --------------------------------------------------------------------------   ",
            "                                                                              ",
            "Short branches (column 1,2) have a signed byte destination [-128,127] range.  ",
            "L prefixed long branches (column 3,4) have a signed word [-32768,32767] range.",
            "Condition codes are untouched by branches.                                    ",
            "                                                  __________________          ",
            "Notes:                                           |  Mnem  | Immed.  |         ",
            "1 - Except notes 2,3, generic branch 6809/6309   |        |         |         ",
            "    cycles and byte lengths are in the table ->  |        |  ~/~  # |         ",
            "2 - BRA and LBRA cycles in table ->              |--------+---------|         ",
            "3 - BSR and LBSR cycles in table ->              | B??    |   3   2 |         ",
            "4 - (L)BHS and (L)BCC are the same               | LB??   |! 5/6  4 |         ",
            "5 - (L)BCS and (L)BLO are the same               | BRA    |   3   2 |         ",
            "S - Signed                                       | LBRA   |  5/4  3 |         ",
            "U - Unsigned                                     | BSR    |  7/6  2 |         ",
            "M - siMple - tests single condition code.        | LBSR   |  9/7  3 |         ",
            "O - other                                         ------------------          ",
            "                                                                              ",
            "                                                                              ",
            "           Register Descriptions,   * Indicates new registers in 6309 CPU.    ",
            " _________________________________________________________________________    ",
            "| X  - 16 bit index register         |PC - 16 bit program counter register|   ",
            "| Y  - 16 bit index register         |*V  - 16 bit variable register      |   ",
            "| U  - 16 bit user-stack pointer     |*0  - 8/16 bit zero register        |   ",
            "| S  - 16 bit system-stack pointer   |V and 0 only inter-register instrcts|   ",
            "|------------------------------------+------------------------------------|   ",
            "| A  - 8 bit accumulator             |                                    |   ",
            "| B  - 8 bit accumulator             |    Accumulator structure map:      |   ",
            "|*E  - 8 bit accumulator             |      ----- ----- ----- -----       |   ",
            "|*F  - 8 bit accumulator             |     |  A  |  B  |  E  |  F  |      |   ",
            "| D  - 16 bit concatenated reg.(A B) |      -----------+-----------       |   ",
            "|*W  - 16 bit concatenated reg.(E F) |     |     D     |     W     |      |   ",
            "|*Q  - 32 bit concatenated reg.(D W) |      -----------------------       |   ",
            "|------------------------------------|     |           Q           |      |   ",
            "|*MD - 8 bit mode/error register     |      -----------------------       |   ",
            "| CC - 8 bit condition code register |      31   24    15    8     0      |   ",
            "| DP - 8 bit direct page register    |               bit                  |   ",
            " -------------------------------------------------------------------------    ",
            "Note: The 6309 is static, so the V register is saved across powerups! Others? ",
            "                                                                              ",
            "                                                                              ",
            "                   Transfer/Exchange and Inter-Register Post Byte             ",
            "                                                                              ",
            "    Inter-Register Instructions          _______________|_______________      ",
            " __________________________________     |   |   |   |   |   |   |   |   |     ",
            "|  Mnem  |   Forms    |  Register  |    |     SOURCE    |  DESTINATION  |     ",
            "|        |            |            |    |___|___|___|___|___|___|___|___|     ",
            "|        |            | OP  ~/~  + |        HI NIBBLE   |  LOW NIBBLE         ",
            "|--------+------------+------------|                                          ",
            "|*ADCR   | R0,R1      |!31   4   3 |             Register Field               ",
            "|*ADDR   | R0,R1      |!30   4   3 |         (source or destination)          ",
            "|*ANDR   | R0,R1      |!34   4   3 |                                          ",
            "|*CMPR   | R0,R1      |!37   4   3 |      0000 - D (A:B)    1000 - A          ",
            "|*EORR   | R0,R1      |!36   4   3 |      0001 - X          1001 - B          ",
            "| EXG    | R0,R1      | 1E  8/5  2 |      0010 - Y          1010 - CCR        ",
            "|*ORR    | R0,R1      |!35   4   3 |      0011 - U          1011 - DPR        ",
            "|*SBCR   | R0,R1      |!33   4   3 |      0100 - S          1100 - 0          ",
            "|*SUBR   | R0,R1      |!32   4   3 |      0101 - PC         1101 - 0          ",
            "| TFR    | R0,R1      | 1F  6/4  2 |      0110 - W          1110 - E          ",
            "|*TFM    | R0+,R1+    |@38  6+3n 3 |      0111 - V          1111 - F          ",
            "|*TFM    | R0-,R1-    |@39  6+3n 3 |                                          ",
            "|*TFM    | R0+,R1     |@3A  6+3n 3 |  TFM is Transfer Memory: repeats W times,",
            "|*TFM    | R0,R1+     |@3B  6+3n 3 |  decrementing W, changing Ri as asked. n ",
            " ----------------------------------   in cycles is number of bytes moved.     ",
            "Illegal to use CC, DP, W, V, 0, or PC as source or destination register.      ",
            "                                                                              ",
            "The  results  of all  Inter-Register operations are passed into R1 with       ",
            "the exception of EXG which exchanges the values of registers and the TFR      ",
            "block transfers. The register field codes %1100 and %1101 are both zero       ",
            "registers.  They can be used as source or destination.                        ",
            "                                                                              ",
            "                        Logical Memory Operations                             ",
            "    AND,EOR,OR,TEST Immediate to memory: instr, post byte, operand            ",
            " _________________________________________________________________________    ",
            "|  Mnem  |   Immed.   |   Direct   |   Indexed  |  Extended  |  Inherent  |   ",
            "|        |            |            |            |            |            |   ",
            "|        | OP  ~/~  # | OP  ~/~  # | OP  ~/~  # | OP  ~/~  # | OP  ~/~  # |   ",
            "|--------+------------+------------+------------+------------+------------|   ",
            "|*AIM    |            | 02   6   3 | 62   7+  3+| 72   7   4 |            |   ",
            "|*EIM    |            | 05   6   3 | 65   7+  3+| 75   7   4 |            |   ",
            "|*OIM    |            | 01   6   3 | 61   7+  3+| 71   7   4 |            |   ",
            "|*TIM    |            | 0B   6   3 | 6B   7+  3+| 7B   5   4 |            |   ",
            " -------------------------------------------------------------------------    ",
            "                                                                              ",
            "                                                                              ",
            "                 Push/Pull Post byte                                          ",
            "       -------------------------------                                        ",
            "      | 7 | 6 | 5 | 4 | 3 | 2 | 1 | 0 |      All 2 byte registers are pushed  ",
            "       -------------------------------       low byte, then high byte. Stack  ",
            "       |   |   |   |   |   |   |   |____CC   grows down. The PSH(s,u) and     ",
            "Push/  |   |   |   |   |   |   |________A    PUL(s,u) instructions require    ",
            "Pull   |   |   |   |   |   |____________B    one additional cycle for each    ",
            "Order  |   |   |   |   |________________DP   byte pushed or pulled. A+B=D,    ",
            "of     |   |   |   |____________________X    E+F=W, W+D=Q, pushes low then    ",
            "Stack  |   |   |________________________Y    high order. In 6309 mode         ",
            "       |   |____________________________S/U  interrupt stores 2 more bytes    ",
            "       |________________________________PC   (E,F) on stack, and pops on RTI. ",
            "                                                                              ",
            " Push order --> PC, U/S, Y, X, DP, *F, *E/*W, B/D/*Q, A, CC <-- Pull order    ",
            " On IRQ, all regs pushed. On 6309 mode, *W pushed after DP, before D.         ",
            " FIRQ pushes only CC by default. On 6309 mode with FIRQ operating as IRQ,     ",
            " pushes W also. PS(U/S)W PUL(U/S)W saves/loads the W register.                ",
            "                                                                              ",
            "                                                                              ",
            "                                                                              ",
            "                                                                              ",
            "                                                                              ",
            "                                                                              ",
            "        Indexed and Indirect Addressing Modes and Post byte Information       ",
            " ____________________________________________________________________________ ",
            "|                               |    Indexed     |          |    Indirect    |",
            "|-------------------------------+------+-----+---|----------|----------------|",
            "|         Type  |     Forms     | Asm  | +/+ | + | PostByte | Asm    | + | + |",
            "|               |               | form | ~/~ | # | OP code  | form   | ~ | # |",
            "|---------------+---------------+------+-----+---|----------|--------+---+---|",
            "| Constant      | No offset     |  ,R  |  0  | 0 | 1rrY0100 | [ ,R]  | 3 | 0 |",
            "| offset        | 5 bit offset  | n,R  |  1  | 0 | 0rrnnnnn |        |   |   |",
            "| from          | 8 bit offset  | n,R  |  1  | 1 | 1rrY1000 | [n,R]  | 4 | 1 |",
            "| register R    | 16 bit offset | n,R  | 4/3 | 2 | 1rrY1001 | [n,R]  | 7 | 2 |",
            "|---------------+---------------+------+-----+---|----------|--------+---+---|",
            "| Accumulator   | A - Register  | A,R  |  1  | 0 | 1rrY0110 | [A,R]  | 4 | 0 |",
            "| offset        | B - Register  | B,R  |  1  | 0 | 1rrY0101 | [B,R]  | 4 | 0 |",
            "| from R (2's   | E - Register  | E,R  |  1  | 0 |*1rrY0111 | [E,R]  | 1 | 0 |",
            "| complement    | F - Register  | F,R  |  1  | 0 |*1rrY1010 | [F,R]  | 1 | 0 |",
            "| offset)       | D - Register  | D,R  | 4/2 | 0 | 1rrY1011 | [D,R]  | 4 | 0 |",
            "|               | W - Register  | W,R  | 4/1 | 0 |*1rrY1110 | [W,R]  | 4 | 0 |",
            "|---------------+---------------+------+-----+---|----------|--------+---+---|",
            "| Auto          | Increment 1   | ,R+  | 2/1 | 0 | 1rrY0000 |        |   |   |",
            "| increment and | Increment 2   | ,R++ | 3/2 | 0 | 1rrY0001 | [,R++] | 6 | 0 |",
            "| decrement of  | Decrement 1   | ,-R  | 2/1 | 0 | 1rrY0010 |        |   |   |",
            "| register R    | Decrement 2   | ,--R | 3/2 | 0 | 1rrY0011 | [,--R] | 6 | 0 |",
            "|---------------+---------------+------+-----+---|----------|--------+---+---|",
            "| 2's complement| 8 bit offset  | n,PC |  1  | 1 | 1xxY1100 | [n,PC] | 4 | 1 |",
            "| offset from PC| 16 bit offset | n,PC | 5/3 | 2 | 1xxY1101 | [n,PC] | 8 | 2 |",
            "|---------------+---------------+------+-----+---|----------|--------+---+---|",
            "| Indirect      | 16 bit address|      |     |   | 10011111 | [n]    | 5 | 2 |",
            "|---------------+---------------+------+-----+---|----------|--------+---+---|",
            "| Rel to W      | No Offset     | ,W   |  0  | 0 |*100ZZZZZ | [,W]   | 0 | 0 |",
            "| 2's comp      | 16 bit offset | n,W  | 5/2 | 2 |*101ZZZZZ | [n,W]  | 5 | 2 |",
            "| AutoIncr W    | Increment 2   | ,W++ | 3/1 | 0 |*110ZZZZZ | [,W++] | 3 | 0 |",
            "| AutoDecr W    | Decrement 2   | ,--W | 3/1 | 0 |*111ZZZZZ | [,--W] | 3 | 0 |",
            " ------------------------------------------------|----------|---------------- ",
            "* 6309 only. rr: 00 = X, 01 = Y, 10 = U 11 = S.  xx: Doesn't care, leave 0.   ",
            "Mode:Y = 0 index, Y = 1 indirect; ZZZZZ = 01111 index, ZZZZZ = 10000 indirect.",
            "+ and + indicates the additional number of cycles and bytes for the variation.",
            "~     #                                                                       ",
            "                                                                              ",
            "                         Condition Code Register (CC)                         ",
            "                     -------------------------------                          ",
            "                    | E | F | H | I | N | Z | V | C |                         ",
            "                     -------------------------------                          ",
            "    Entire flag(7)____|   |   |   |   |   |   |   |____Carry flag(0)          ",
            "      FIRQ mask(6)________|   |   |   |   |   |________Overflow(1)            ",
            "     Half carry(5)____________|   |   |   |____________Zero(2)                ",
            "       IRQ mask(4)________________|   |________________Negative(3)            ",
            "                                                                              ",
            "                                                                              ",
            "                 Mode and Error Register (MD, 6309 only)                      ",
            "                     -------------------------------                          ",
            "                    | ? | ? |   |   |   |   | ? | ? |                         ",
            "                     -------------------------------                          ",
            "    Div by Zero(7)____|   |   |   |   |   |   |   |____Emulation Mode(0)      ",
            "     Illegal Op(6)________|   |   |   |   |   |________FIRQ Mode(1)           ",
            "         Unused(5)____________|   |   |   |____________Unused(2)              ",
            "         Unused(4)________________|   |________________Unused(3)              ",
            "                                                                              ",
            "                                                                              ",
            "MD register: works like the CC register.                                      ",
            "Bits 0,1 write only, bits 6,7 read only.                                      ",
            "Bit 0: Emulation mode: if 0, 6809 emulation mode, if 1, 6309 native mode      ",
            "Bit 1: FIRQ Mode     : if 0, FIRQ as normal 6809, if 1, FIRQ operate as IRQ   ",
            "Bits 2-5 unused.                                                              ",
            "Bit 6: Set to 1 if illegal instruction occurred                               ",
            "Bit 7: Set to 1 if divide by 0 occurred                                       ",
            "FIRQ saves only CC, unless in IRQ mode, then all registers in push order saved",
            "                                                                              ",
            "6309/6809 Instructions (by opcode grid, transposed): (*prefix means 6309 only)",
            "All unused opcodes are both undefined and illegal                             ",
            "                                                                              ",
            @"L\H 0x  1x   2x  3x   4x   5x  6x  7x  8x   9x   Ax   Bx   Cx   Dx   Ex   Fx  ",
            "x0  NEG pref BRA LEAX NEG NEG NEG NEG SUBA SUBA SUBA SUBA SUBB SUBB SUBB SUBB ",
            "x1 *OIM pref BRN LEAY        *OIM*OIM CMPA CMPA CMPA CMPA CMPB CMPB CMPB CMPB ",
            "x2 *AIM NOP  BHI LEAS        *AIM*AIM SBCA SBCA SBCA SBCA SBCB SBCB SBCB SBCB ",
            "x3  COM SYNC BLS LEAU COM COM COM COM SUBD SUBD SUBD SUBD ADDD ADDD ADDD ADDD ",
            "x4  LSR*SEXW BCC PSHS LSR LSR LSR LSR ANDA ANDA ANDA ANDA ANDB ANDB ANDB ANDB ",
            "x5 *EIM      BCS PULS        *EIM*EIM BITA BITA BITA BITA BITB BITB BITB BITB ",
            "x6  ROR LBRA BNE PSHU ROR ROR ROR ROR LDA  LDA  LDA  LDA  LDB  LDB  LDB  LDB  ",
            "x7  ASR LBSR BEQ PULU ASR ASR ASR ASR      STA  STA  STA       STB  STB  STB  ",
            "x8  ASL      BVC      ASL ASL ASL ASL EORA EORA EORA EORA EORB EORB EORB EORB ",
            "x9  ROL DAA  BVS RTS  ROL ROL ROL ROL ADCA ADCA ADCA ADCA ADCB ADCB ADCB ADCB ",
            "xA  DEC ORCC BPL ABX  DEC DEC DEC DEC ORA  ORA  ORA  ORA  ORB  ORB  ORB  ORB  ",
            "xB *TIM      BMI RTI         *TIM*TIM ADDA ADDA ADDA ADDA ADDB ADDB ADDB ADDB ",
            "xC  INC ANDC BGE CWAI INC INC INC INC CMPX CMPX CMPX CMPX LDD  LDD  LDD  LDD  ",
            "xD  TST SEX  BLT MUL  TST TST TST TST BSR  JSR  JSR  JSR  STD  STD  STD  STD  ",
            "xE  JMP EXG  BGT              JMP JMP LDX  LDX  LDX  LDX  LDU  LDU  LDU  LDU  ",
            "xF  CLR TFR  BLE SWI  CLR CLR CLR CLR      STX  STX  STX       STU  STU  STU  ",
            "NOTES:                 A   B   i   e        d    i    e    m    d    d    e   ",
            "                                                                              ",
            "opcodes prefixed by 10                                                        ",
            @"L\H 0x 1x 2x    3x     4x    5x  6x 7x 8x    9x    Ax    Bx   Cx  Dx  Ex  Fx  ",
            "x0        LBRA *ADDR  *NEGD           *SUBW *SUBW *SUBW *SUBW                 ",
            "x1        LBRN *ADCR                  *CMPW *CMPW *CMPW *CMPW                 ",
            "x2        LBHI *SUBR                  *SBCD *SBCD *SBCD *SBCD                 ",
            "x3        LBLS *SBCR  *COMD *COMW      CMPD  CMPD  CMPD  CMPD                 ",
            "x4        LBHS *ANDR  *LSRD *LSRW     *ANDD *ANDD *ANDD *ANDD                 ",
            "x5        LBLO *ORR                   *BITD *BITD *BITD *BITD                 ",
            "x6        LBNE *EORR  *RORD *RORW     *LDW  *LDW  *LDW  *LDW                  ",
            "x7        LBEQ *CMPR  *ASRD                 *STW  *STW  *STW                  ",
            "x8        LBVC *PSHSW *ASLD           *EORD *EORD *EORD *EORD                 ",
            "x9        LBVS *PULSW *ROLD *ROLW     *ADCD *ADCD *ADCD *ADCD                 ",
            "xA        LBPL *PSHUW *DECD *DECW     *ORD  *ORD  *ORD  *ORD                  ",
            "xB        LBMI *PULUW                 *ADDW *ADDW *ADDW *ADDW    *LDQ         ",
            "xC        LBGE        *INCD *INCW      CMPY  CMPY  CMPY  CMPY    *STQ         ",
            "xD        LBLT        *TSTD *TSTW                             LDS LDS LDS LDS ",
            "xE        LBGT                         LDY   LDY   LDY   LDY      STS STS STS ",
            "xF        LBLE  SWI2  *CLRD *CLRW            STY   STY   STY                  ",
            "NOTES:                   h    h         m     d     i     e    m   d    i   e ",
            "                                                                              ",
            "opcodes prefixed by 11                                                        ",
            @"L\H 0x 1x 2x  3x    4x    5x  6x 7x  8x    9x   Ax   Bx   Cx   Dx   Ex   Fx   ",
            "x0           *BAND                  *SUBE *SUBE*SUBE*SUBE*SUBF*SUBF*SUBF*SUBF ",
            "x1           *BIAND                 *CMPE *CMPE*CMPE*CMPE*CMPF*CMPF*CMPF*CMPF ",
            "x2           *BOR                                                             ",
            "x3           *BIOR *COME *COMF       CMPU  CMPU CMPU CMPU                     ",
            "x4           *BEOR                                                            ",
            "x5           *BIEOR                                                           ",
            "x6           *LDBT                  *LDE  *LDE *LDE *LDE *LDF *LDF *LDF *LDF  ",
            "x7           *STBT                        *STE *STE *STE                      ",
            "x8           *TFM                                                             ",
            "x9           *TFM                                                             ",
            "xA           *TFM  *DECE *DECF                                                ",
            "xB           *TFM                   *ADDE *ADDE*ADDE*ADDE*ADDF*ADDF*ADDF*ADDF ",
            "xC           *BITMD*INCE *INCF       CMPS  CMPS CMPS CMPS                     ",
            "xD           *LDMD *TSTE *TSTF      *DIVD *DIVD*DIVD*DIVD                     ",
            "xE                                  *DIVQ *DIVQ*DIVQ*DIVQ                     ",
            "xF            SWI2 *CLRE *CLRF      *MULD *MULD*MULD*MULD                     ",
            "NOTES:                                m     d     i   e    m     d    i   e   ",
            "                                                                              ",
            "Notes:                                                                        ",
            "A - operate on register A    e - extended addressing  m - immediate addressing",
            "B - operate on register B    h - inherent addressing                          ",
            "d - direct addressing        i - indexed addressing                           ",
            " ___________________________________________________________________________  ",
            "|Mnemon.|Description     |Notes       |Mnemon.|Description     |Notes       | ",
            "|-------+----------------+------------+-------+----------------+------------+ ",
            "|ABX    |Add to Index Reg|X=X+B       |LBcc nn|Long cond Branch|If cc LBRA  | ",
            "|ADCa  s|Add with Carry  |a=a+s+C     |LBRA nn|Long Br. Always |PC=nn       | ",
            "*ADCD  s|Add with Carry  |D=D+s+C     |LBSR nn|Long Br. Sub    |-[S]=PC,LBRA| ",
            "*ADCR rr| add carry      |r2=r2+r1+C  |LDa   s|Load acc.       |a=s         | ",
            "|ADDa  s|Add             |a=a+s       |LDD   s|Load D acc.     |D=s         | ",
            "*ADDe  s|Add             |a=a+s       *LDe   s|Load e acc.     |e=s         | ",
            "|ADDD  s|Add to D acc.   |D=D+s       *LDQ   s|Load Q acc.     |Q=s         | ",
            "*ADDR rr|Add registers   |r2=r2+r1    *LDMD  s|Load MD acc.    |MD=s        | ",
            "|ANDa  s|Logical AND     |a=a&s       |LDS   s|Load S pointer  |S=s         | ",
            "|ANDCC s|Logic AND w CCR |CC=CC&s     |LDU   s|Load U pointer  |U=s         | ",
            "*ANDD  s|Logical AND     |D=D&s       |LDi   s|Load index reg  |i=s (Y ~s=7)| ",
            "*ANDR rr|Logical AND regs|r2=r2&r1    |LEAp  s|Load Eff Address|p=EAs(X=0-3)| ",
            "|ASL   d|Arith Shift Left|d=d*2       |LSL   d|Logical Shift L |d={C,d,0}<- | ",
            "|ASLa   |Arith Shift Left|a=a*2       |LSLa   |Logical Shift L |a={C,a,0}<- | ",
            "*ASLD   |Arith Shift Left|D=D*2       *LSLD   |Logical Shift L |D={C,D,0}<- | ",
            "|ASR   d|Arith Shift Rght|d=d/2       |LSR   d|Logical Shift R |d=->{d,0}   | ",
            "|ASRa   |Arith Shift Rght|a=a/2       |LSRa   |Logical Shift R |d=->{d,0}   | ",
            "*ASRD   |Arith Shift Rght|D=D/2       *LSRD   |Logical Shift R |D=->{W,0}   | ",
            "|BCC   m|Branch Carry Clr|If C=0      *LSRW   |Logical Shift R |W=->{W,0}   | ",
            "|BCS   m|Branch Carry Set|If C=1      |MUL    |Multiply        |D=A*B       | ",
            "|BEQ   m|Branch Equal    |If Z=1      *MULD  s| Multiply       |Q=D*s       | ",
            "|BGE   m|Branch >=       |If NxV=0    |NEG   d|Negate          |d=-d        | ",
            "|BGT   m|Branch >        |If Zv{NxV}=0|NEGa   |Negate acc      |a=-a        | ",
            "|BHI   m|Branch Higher   |If CvZ=0    *NEGD   |Negate acc      |D=-D        | ",
            "|BHS   m|Branch Higher,= |If C=0      |NOP    |No Operation    |            | ",
            "|BITa  s|Bit Test acc    |a&s         |ORa   s|Logical incl OR |a=avs       | ",
            "*BITD  s|Bit Test acc    |D&s         |ORCC  n|Inclusive OR CC |CC=CCvn     | ",
            "*BITMD s|Bit Test acc    |MD&s        *ORD   s|Logical incl OR |D=Dvs       | ",
            "|BLE   m|Branch <=       |If Zv{NxV}=1*ORR rr |Logical incl OR |r1=r1vr2    | ",
            "|BLO   m|Branch Lower    |If C=1      |PSHS  r|Psh reg(s)(!= S)|-[S]={r,...}| ",
            "|BLS   m|Branch Lower,=  |If CvZ=1    |PSHU  r|Psh reg(s)(!= U)|-[U]={r,...}| ",
            "|BLT   m|Branch <        |If NxV=1    *PSHSW  |Psh reg W       |-[S]=W      | ",
            "|BMI   m|Branch Minus    |If N=1      *PSHUW  |Psh reg W       |-[U]=W      | ",
            "|BNE   m|Branch Not Equal|If Z=0      |PULS  r|Pul reg(s)(!= S)|{r,...}=[S]+| ",
            "|BPL   m|Branch Plus     |If N=0      |PULU  r|Pul reg(s)(!= U)|{r,...}=[U]+| ",
            "|BRA   m|Branch Always   |PC=m        *PULSW  |Pul reg W       |W=[S]+      | ",
            "|BRN   m|Branch Never    |NOP         *PULUW  |Pul reg W       |W=[U]+      | ",
            "|BSR   m|Branch to Sub   |-[S]=PC,BRA |ROL   d|Rotate Left     |d={C,d}<-   | ",
            "|BVC   m|Branch Over. Clr|If V=0      |ROLa   |Rotate Left acc.|a={C,a}<-   | ",
            "|BVS   m|Branch Over. Set|If V=1      *ROLD   |Rotate Left acc.|D={C,D}<-   | ",
            "|CLR   d|Clear           |d=0         *ROLW   |Rotate Left acc.|W={C,W}<-   | ",
            "|CLRa   |Clear acc.      |a=0         |ROR   d|Rotate Right    |d=->{C,d}   | ",
            "*CLRD   |Clear acc.      |D=0         |RORa   |Rotate Right acc|a=->{C,a}   | ",
            "*CLRe   |Clear acc.      |e=0         *RORD   |Rotate Right acc|D=->{C,W}   | ",
            "|CMPa  s|Compare         |a-s         *RORW   |Rotate Right acc|W=->{C,W}   | ",
            "|CMPD  s|Compare D acc.  |D-s         |RTI    |Return from Int |{regs}=[S]+ | ",
            "*CMPe  s|Compare e acc.  |e-s         |RTS    |Return from Sub |PC=[S]+     | ",
            "*CMPR rr|Compare regs    |r1-r2       |SBCa  s|Sub with Carry  |a=a-s-C     | ",
            "|CMPS  s|Compare S ptr   |S-s         *SBCD  s|Sub with Carry  |D=D-s-C     | ",
            "|CMPU  s|Compare U ptr   |U-s         *SBCR rr|Sub with Carry  |r1=r1-r2-C  | ",
            "|CMPi  s|Compare         |i-s (Y ~s=8)|SEX    |Sign Extend     |D=B extended| ",
            "|COM   d|Complement      |d=~d        *SEXW   |Sign Extend     |Q=W extended| ",
            "|COMa   |Complement acc. |a=~a        |STa   d|Store accumultor|d=a         | ",
            "*COMD   |Complement acc. |D=~D        |STD   d|Store Double acc|D=a         | ",
            "*COMe   |Complement acc. |e=~e        *STe   d|Store accumultor|d=e         | ",
            "|CWAI  n|AND CC, Wait int|CC=CC&n,E=1,*STQ   d|Store accumultor|d=Q         | ",
            "|DAA    |Dec Adjust Acc. |A=BCD format|STS   d|Store Stack ptr |S=a         | ",
            "|DEC   d|Decrement       |d=d-1       |STU   d|Store User  ptr |U=a         | ",
            "|DECa   |Decrement acc.  |a=a-1       |STi   d|Store index reg |i=a (Y ~s=7)| ",
            "*DECD   |Decrement acc.  |D=D-1       |SUBa  s|Subtract        |a=a-s       | ",
            "*DECe   |Decrement acc.  |e=e-1       |SUBD  s|Subtract D acc. |D=D-s       | ",
            "*DIVD  s|Divide          |D=D/s       *SUBe  s|Subtract D acc. |e=e-s       | ",
            "*DIVQ  s|Divide          |Q=Q/s       *SUBR rr|Subtract regs   |r1=r1-r2    | ",
            "|EORa  s|Logical Excl OR |a=axs       |SWI    |Software Int 1  |-[S]={regs} | ",
            "*EORD  s|Logical Excl OR |D=Dxs       |SWI2   |Software Int 2  |SWI         | ",
            "*EORR rr|Logical Excl OR |r1= r1xr2   |SWI3   |Software Int 3  |SWI         | ",
            "|EXG  rr|Exchg(same size)|r1<->r2     |SYNC   |Sync. to int    |  (min ~s=2)| ",
            "|INC   d|Increment       |d=d+1       *TFM  tf|Block transfer  | - special- | ",
            "|INCa   |Increment acc.  |a=a+1       |TFR r,r|Transfer r1->r2 |r2=r1       | ",
            "*INCD   |Increment acc.  |D=D+1       |TST   s|Test            |s           | ",
            "*INCe   |Increment acc.  |e=e+1       |TSTa   |Test accumulator|a           | ",
            "|JMP   s|Jump            |PC=EAs      |TSTD   |Test accumulator|D           | ",
            "|JSR   s|Jump to Sub     |-[S]=PC,JMP |TSTe   |Test accumulator|e           | ",
            "----------------------------------------------------------------------------  ",
            "                                                                              ",
            " ___________________________________________________________________________  ",
            "| a        |Acc A or B                |***** Legend - todo - do more *******| ",
            "| e        |Acc E, F, or W (6309)     |* prefix  |6309 only instruction     | ",
            "| d  s  EA |Dest/Src/effective addr.  | m        |Rel addr (-128 to +127)   | ",
            "| i  p  r  |X orY/X,Y,S,U/any reg     | n  nn    |8/16-bit (0 to 255/65535) | ",
            "| rr       |two registers r1,r2       | tf       |transfer registers and +- | ",
            " ---------------------------------------------------------------------------  ",
            "                                                                              ",
            "Interrupt Vectors                                                             ",
            "             ________________________________________________________         ",
            "            | FFF0 to FFF1 |Note 1       | FFF8 to FFF9 |IRQ   vector|        ",
            " Reserved   | FFF2 to FFF3 |SWI3  vector | FFFA to FFFB |SWI   vector|        ",
            " Addresses  | FFF4 to FFF5 |SWI2  vector | FFFC to FFFD |NMI   vector|        ",
            "            | FFF6 to FFF7 |FIRQ  vector | FFFE to FFFF |Reset vector|        ",
            "             --------------------------------------------------------         ",
            "Note 1: Reserved in 6809. For 6309 mode, holds vector for divide by 0 error   ",
            "or illegal instruction error. Error can be read in 6309 register MD.          ",
            "                                                                              ",
            "                                                                              ",
            "The Hitachi HD63B09EP (6309) microprocessor is a clone of the Motorola        ",
            "MC68B09E (6809) chip, with additional registers and instructions. Bit 0 of    ",
            "the 6309 only register MD determines which mode is on: 6809 emulation or      ",
            "6309 native. 6309 often has faster instruction timings. When cycle counts     ",
            "are given for 6809/6309 for a 6309 only instruction, these are for            ",
            "emulation/native timings.                                                     ",
            "                                                                              ",
            "The Motorola 6809 was released circa 1979, and came in many flavors: 68A09,   ",
            "68A09E,68B09,68B09E. The 68A09(E) ran at 1 MHz and 1.5 MHz, the 68B09(E) at   ",
            "2 MHz. The 6809 had an internal clock generator needing only an external      ",
            "crystal, and the 6809E needed an external clock generator.                    ",
            "                                                                              ",
            "The 6309 has a B (2 MHz) and a C version rated at either 3.0 or 3.5 MHz. Some ",
            "hackers have pushed the 63C09 variant can to 5 MHZ. The 6309 comes in internal",
            "and external clock versions (HD63B/C09 and HD63B/C09E respectively).          ",
            "                                                                              ",
            "Some useful code ideas, based on [1]: (see [1] for more info)                 ",
            "1. Check if code on a 6309 or 6809:                                           ",
            "      LDB   #255  , CLRD    ; executes as a $10 (ignored) $4F (CLRA) on a 6809",
            "      TSTB        , BEQ   Is6309                                              ",
            "                                                                              ",
            "2. Check if 6309 system is in native mode or to check 6309 FIRQ mode, use RTI ",
            "   with appropriate items on stack.                                           ",
            "                                                                              ",
            "Document History                                                              ",
            " - May   2007 - Version 1.2 - minor corrections.                              ",
            " - April 2007 - Version 1.1 - extensive additions, minor corrections.         ",
            " - July  2006 - Version 1.0 - initial release.                                ",
            "Sources                                                                       ",
            "[1] HD63B09EP Technical Reference Guide,[5] Notes by Paul D. Burgin           ",
            "    Chet Simpson, Alan DeKok            [6] Notes by Sockmaster(John Kowalski)",
            "[2] Programming the 6809, Rodney Zaks,  [7] The MC6809 Cookbook, Carl Warren, ",
            "    William Labiak,1982 Sybex               1981.                             ",
            "[3] Notes by Jonathan Bowen.            [8] en.wikipedia.org/wiki/6809        ",
            "[4] Notes by Neil Franklin, 2004.11.01  [9] www.howell1964.freeserve.co.uk/   ",
            "                                                                              ",
            "END OF FILE                                                                   ",
        };

        #endregion

        static string[] requireRegisters =
        {
            "TFR","EXG","PULU","PULS","PSHU","PSHS",
            // 6309
            "ADCR","ADDR","ANDR","CMPR","EORR","ORR","SBCR","SUBR","TFM"
        };

        // branch mnemonics (not incl the 'L' forms)
        static string[] branches =
        {
            "BCC","BCS","BEQ","BGE","BGT","BHI","BHS","BLE","BLO","BLS","BLT","BMI","BNE","BPL","BRA","BRN","BSR","BVC","BVS"
            
        };

        public static bool RequiresRegisterList(string mnemonic)
        {
            return requireRegisters.Contains(mnemonic.ToUpper());
        }

        public static bool IsBranch(string mnemonic)
        {
            var m = mnemonic.ToUpper();
            if (m.StartsWith("L"))
                m = m.Substring(1);
            return branches.Contains(m);
        }
    }
}
