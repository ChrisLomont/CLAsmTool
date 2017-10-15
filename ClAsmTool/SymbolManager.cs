using System;
using System.Collections.Generic;
using System.Diagnostics.SymbolStore;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Lomont.ClAsmTool
{
    public class SymbolManager : List<Symbol>
    {

        public void AddSymbol(Symbol symbol, Output output)
        {
            // todo - avoid some duplicates, but allow local labels?
            Add(symbol);
        }


        public List<Label> GetLabels()
        {
            return this.OfType<Label>().ToList();
        }

        public List<Struct> GetStructs()
        {
            return this.OfType<Struct>().ToList();
        }


        public void Dump(string label, Output output)
        {
            // todo - dump item
            var lbls = GetLabels().Where(c => c.Text == label);
            if (!lbls.Any())
                output.Info($"Cannot find label {label}");
            foreach (var lbl in lbls)
                output.Info($"Label: {lbl}");
        }

        /// <summary>
        /// Set label address
        /// </summary>
        /// <param name="line"></param>
        /// <param name="address"></param>
        /// <returns></returns>
        public bool SetAddress(Line line, int address)
        {
            // 1. if a label with this name has this address, do nothing
            // 2. else find unset label, and add it
            // 3. return true if set, else false

            var lineLabel = line?.Label.Text ?? "";
            foreach (var label in GetLabels())
            {
                if (label.Text != lineLabel)
                    continue;
                if (label.Address == address)
                    return true; // exists
                if (label.Address == Label.UnknownAddress)
                {
                    label.Address = address;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Try to lookup symbol value
        /// Symbol is label, or struct, or struct field, or even label.struct subfields
        /// Return true on success, else false
        /// </summary>
        /// <param name="text"></param>
        /// <param name="value"></param>
        /// <param name="address"></param>
        /// <returns></returns>
        public bool GetValue(string text, out int value, int address)
        {
            value = 0;
            var words = text.Split(new[] {'.'}, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
            if (words.Count < 1 || String.IsNullOrEmpty(words[0]))
                return false;

            // check labels - must all be present
            var labels = this
                .OfType<Label>()
                .Where(s => s.Text == words[0]).ToList();
            var someMissing = labels.Any(s => s.Address == Label.UnknownAddress);
            Label bestLabel = null;
            if (!someMissing)
            {
                // can look at labels
                bestLabel = labels.OrderBy(m => Math.Abs(m.Address - address)).FirstOrDefault();
                if (bestLabel != null && words.Count == 1)
                {
                    value = bestLabel.Address;
                    return true;
                }
            }

            var offset = 0;

            if (bestLabel != null)
                offset = bestLabel.Address;

            // find base struct, either from current label, or from structs
            var strToFind = bestLabel?.Line.Opcode?.Text ?? words[0];
            var curPtr = this.OfType<Struct>().FirstOrDefault(s => s.Text == strToFind);

            if (curPtr == null)
                return false;

            // walk words, which should be struct offsets
            for (var i = 1; i < words.Count; ++i)
            {
                var field = curPtr?.Fields.Find(f => f.Text == words[i] && f.Offset != -1);
                if (field == null) return false;
                offset += field.Offset;
                curPtr = field.Next;
            }
            value = offset;
            return true;

        }

        public bool MakeLengths(Output output)
        {
            var structs = GetStructs();
            foreach (var s in structs)
            {
                if (!MakeLength1(structs, s, output))
                {
                    output.Error(s.Line, s.Line.Label, "Cannot determine struct length");
                    return false;
                }
                //output.WriteLine($"{s.Text} => {s.ByteLength}");
            }
            return true;
        }



        // try to recursively fill in fields using the list of known structs
        static bool MakeLength1(List<Struct> structs, Struct s1 , Output output)
        {
            // CoinSound:  Sound1 <$FF, <1, $20, $3E>, 0>

            if (s1.ByteLength > 0)
                return true; // already done
            var offset = 0;
            foreach (var field in s1.Fields)
            {
                var text = field.Line.Opcode.Text;
                var dupCount = CountDups(s1, field.Line.Operand, output);
                field.Offset = offset;
                if (text == "fcb")
                {
                    // todo - dups?
                    for (var i = 0; i < dupCount; ++i)
                        s1.ByteLengths.Add(1);
                    offset += dupCount;
                }
                else if (text == "fdb")
                {
                    for (var i = 0; i < dupCount; ++i)
                        s1.ByteLengths.Add(2);
                    offset += dupCount * 2;
                }
                else if (structs.Any(s => s.Text == text))
                {
                    var str = structs.First(s => s.Text == text);
                    field.Next = str;
                    if (str.ByteLength < 0)
                    {
                        if (!MakeLength1(structs, str, output))
                        {
                            output.Error($"Cannot find struct value {str}");
                            return false; // cannot continue
                        }
                    }
                    for (var i = 0; i < dupCount; ++i)
                    {
                        foreach (var v in str.ByteLengths)
                            s1.ByteLengths.Add(v);
                    }
                    offset += dupCount * str.ByteLength;
                }
                else
                {
                    output.Error($"Cannot understand struct size {s1}");
                    return false;
                }
            }

            s1.ByteLength = s1.ByteLengths.Sum();

            return true;
        }

        static int CountDups(Struct s, Token operand, Output output)
        {
            /* ? = 1
             * n dup(?) = n
             */
            if (operand == null)
            {
                output.Error($"Missing operand for struct {s}");
                return 0;
            }
            if (operand.Text == "?")
                return 1;
            var w = operand.Text.Split(new[] { ' ', '\t' });
            if (w.Length > 0 && Int32.TryParse(w[0], out var value1))
                return value1;
            output.Error($"Missing operand for struct {s}");
            return 0;
        }


    }

    public class Symbol
    {
        public Line Line { get; protected set; }

        public string Text { get; protected set; }

        public Symbol(Line line)
        {
            Line = line;
            Text = Line.Label.Text;
        }

    }

    public class Label : Symbol
    {
        /// <summary>
        /// Address, or UnknownAddress if not filled out
        /// </summary>
        public int Address { get; set; } = UnknownAddress;

        public static int UnknownAddress { get; } = -1;

        public Label(Line line) : base(line)
        {
        }


        public override string ToString()
        {
            return $"{Text} = {Address:X4}";
        }
    }


    public class Struct : Symbol
    {

        /// <summary>
        /// Number of bytes struct takes up, or -1 if unknown
        /// </summary>
        public int ByteLength = -1;

        public int FieldCount => Fields.Count;

        public List<Field> Fields { get; } = new List<Field>();

        public List<int> ByteLengths { get; } = new List<int>();


        public class Field
        {
            public string Text;
            public Line Line;
            public int Offset = -1;
            public Struct Next = null;
        }

        public Struct(Line line) : base(line)
        {
        }


        public void AddField(Line line)
        {
            Fields.Add(new Field { Line = line, Text = line.Label?.Text });
        }


        public override string ToString()
        {
            return $"Struct {Text}";
        }


    }

}
