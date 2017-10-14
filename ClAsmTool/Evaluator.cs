using System;
using System.Linq;

namespace Lomont.ClAsmTool
{
    // expression evaluator
    static class Evaluator
    {


        // evaluate expression 
        // return true if evaluated to a value, else false
        // if false, marks line as needing a fixup
        public static bool Evaluate(Asm6809.AsmState state, Line line, string expression, out int value)
        {
            var retval = true; // assume works
            value = 0;
            try
            {
                var state2 = new State(expression);
                var t = ParseToTree(state2);
                retval = EvaluateTree(state, t, out value);
            }
            catch (Exception)
            {
                // todo - errors handled elsewhere
                retval = false;
            }

            if (!retval)
                line.NeedsFixup = true;
            return retval;
        }

        static bool EvaluateTree(Asm6809.AsmState state, Tree tree, out int value)
        {
            value = 0;
            int left, right;
            if (tree.op != null)
            {
                switch (tree.op)
                {
                    case "+":
                        if (!EvaluateTree(state, tree.left, out left))
                            return false;
                        if (tree.right == null)
                        {
                            value = left;
                            return true;
                        }
                        if (!EvaluateTree(state, tree.right, out right))
                            return false;
                        value = left + right;
                        return true;
                    case "-":
                        if (!EvaluateTree(state, tree.left, out left))
                            return false;
                        if (tree.right == null)
                        {
                            value = -left;
                            return true;
                        }
                        if (!EvaluateTree(state, tree.right, out right))
                            return false;
                        value = left - right;
                        return true;
                    case "*":
                        if (!EvaluateTree(state, tree.left, out left) ||
                            !EvaluateTree(state, tree.right, out right))
                            return false;
                        value = left * right;
                        return true;
                    case "/":
                        if (!EvaluateTree(state, tree.left, out left) ||
                            !EvaluateTree(state, tree.right, out right))
                            return false;
                        if (right == 0)
                            throw new Exception("Division by zero");
                        value = left / right;
                        return true;
                    case "%":
                        if (!EvaluateTree(state, tree.left, out left) ||
                            !EvaluateTree(state, tree.right, out right))
                            return false;
                        if (right == 0)
                            throw new Exception("Mod division by zero");
                        value = left % right;
                        return true;
                    default:
                        throw new Exception($"unknown op {tree.op}");
                }
            }
            else
            {
                var text = tree.value;
                if (text.StartsWith("$"))
                {
                    value = Convert.ToInt32(text.Substring(1), 16);
                    return true;
                }
                if (text.All(t => Char.IsDigit(t)))
                {
                    value = Int32.Parse(text);
                    return true;
                }
                // must be symbol - look up value or throw
                if (state.Symbols.GetValue(text, out value, state.Address))
                    return true;
                return false; // Symbol {text} not yet evaluated
            }
        }

        class Tree
        {
            public Tree left = null, right = null;
            public string op = null;
            public string value = null;

            public Tree(string op, Tree left, Tree right, string value = null)
            {
                this.left = left;
                this.right = right;
                this.op = op;
                this.value = value;
            }
            public Tree(string op, Tree tree) : this(op,tree,null,null)
            {
            }
            public Tree(string value) : this(null,null,null,value)
            {
            }
        }

        enum Assoc
        {
            Right,
            Left
        }

        enum Arity
        {
            Binary,
            Unary
        } 


        class State
        {

            class Op
            {
                public string op;
                public int prec;
                public Assoc assoc;
                public Arity arity;
                public Op(string op, int precedence, Assoc assoc, Arity arity)
                {
                    this.op = op;
                    this.prec = precedence;
                    this.assoc = assoc;
                    this.arity = arity;
                }

            }


            Op[] ops = new[]
            {
                new Op("+", 8, Assoc.Right, Arity.Unary),
                new Op("-", 8, Assoc.Right, Arity.Unary),

                new Op("*", 7, Assoc.Left, Arity.Binary),
                new Op("/", 7, Assoc.Left, Arity.Binary),
                new Op("%", 7, Assoc.Left, Arity.Binary),

                new Op("+", 6, Assoc.Left, Arity.Binary),
                new Op("-", 6, Assoc.Left, Arity.Binary),

                new Op("<<", 5, Assoc.Left, Arity.Binary),
                new Op(">>", 5, Assoc.Left, Arity.Binary),

                new Op("&", 4, Assoc.Left, Arity.Binary),
                new Op("^", 3, Assoc.Left, Arity.Binary),
                new Op("|", 2, Assoc.Left, Arity.Binary),
                new Op("&&", 1, Assoc.Left, Arity.Binary),
                new Op("||", 0, Assoc.Left, Arity.Binary)
            };

            // find op, or null if none
            Op GetOp(string op, Arity arity)
            {
                return ops.FirstOrDefault(o => o.op == op && o.arity == arity);
            }

            public bool IsUnary(string op)
            {
                return GetOp(op, Arity.Unary) != null;
            }

            public bool IsBinary(string op)
            {
                return GetOp(op, Arity.Binary) != null;
            }


            // 0 for left assoc, else 1 for right
            public Assoc Associativity(string op, Arity arity)
            {
                return GetOp(op, arity).assoc;
            }

            public int Precedence(string op, Arity arity)
            {
                return GetOp(op, arity).prec;
            }

            // recognize values (here are symbols or digits or 0Xhex or $hex)
            public bool IsValue(string token)
            {
                if (Matcher.MatchSymbol(token, 0, out _) || Matcher.MatchStructField(token, 0, out _))
                    return true;
                if (token.All(t => Char.IsDigit(t)))
                    return true;
                if (token.StartsWith("$") && token.Skip(1).All(t => Char.IsDigit(t) || "abcdef".Contains(Char.ToLower(t))))
                    return true;
                return false;
            }


            #region tokenizer

            int chpos = 0;
            string expr;
            public State(string expression)
            {
                expr = expression;
                Consume();
            }

            // must match, else throw
            public void Expect(string expectedToken)
            {
                if (expectedToken == null)
                {
                    if (Next() != null)
                        throw new Exception("Expected end of expression");
                    return;
                }
                var next1 = Next();
                if (next1 != expectedToken)
                    throw new Exception($"Expected token {expectedToken}, obtained {next1}");
                Consume();
            }

            // advance to next token
            public void Consume()
            {
                if (chpos >= expr.Length)
                {
                    token = null;
                    return;
                }
                // tokens - are longest op, or are parens, or are string of letters, or are numbers, or error

                int length;
                if (Matcher.MatchNumber(expr, chpos, out length))
                {
                    token = expr.Substring(chpos, length);
                    chpos += length;
                    return;
                }

                if (Matcher.MatchStructField(expr, chpos, out length))
                {
                    token = expr.Substring(chpos, length);
                    chpos += length;
                    return;
                }

                var next = expr[chpos];
                if (next == '(' || next == ')')
                {
                    token = expr[chpos].ToString();
                    chpos++;
                    return;
                }

                // match longest operator
                var bestLen = -1;
                Op bestOp = null;
                var prefix = expr.Substring(chpos);
                foreach (var op in ops)
                {
                    if (prefix.StartsWith(op.op) && op.op.Length > bestLen)
                    {
                        bestOp = op;
                        bestLen = op.op.Length;
                    }
                }
                if (bestLen > 0)
                {
                    chpos += bestOp.op.Length;
                    token = bestOp.op;
                    return;
                }

                throw new Exception($"Unknown token start {expr.Substring(chpos)}");
            }


            string token = null;
            public string Next()
            {
                return token;
            }

            #endregion

        }

        // see http://www.engr.mun.ca/~theo/Misc/exp_parsing.htm
        static Tree ParseToTree(State state)
        {
            var t = Exp(state, 0);
            state.Expect(null); // expect no more tokens
            return t;

        }



        static Tree Exp(State state, int p)
        {
            var t = P(state);
            while (state.IsBinary(state.Next()) && state.Precedence(state.Next(), Arity.Binary) >= p)
            {
                var op = state.Next();
                state.Consume();
                var q = state.Associativity(op, Arity.Binary) == Assoc.Right ? p : 1 + p;
                var t1 = Exp(state,q);
                t = new Tree(op,t,t1);
            }
            return t;

        }

        static Tree P(State state)
        {
            var next = state.Next();

            if (state.IsUnary(next))
            {
                var op = next;
                state.Consume();
                var q = state.Precedence(op, Arity.Unary);
                var t = Exp(state,q);
                return new Tree(op, t);
            }
            else if (next == "(")
            {
                state.Consume();
                var t = Exp(state, 0);
                state.Expect(")");
                return t;
            }
            else if (state.IsValue(next))
            {
                var t = new Tree(next);
                state.Consume();
                return t;
            }
            else
                throw new Exception("Parse error");
        }


    }
}
