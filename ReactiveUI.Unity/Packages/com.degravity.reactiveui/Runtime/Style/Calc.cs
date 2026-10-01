using System;
using System.Collections.Generic;
using System.Globalization;

namespace ReactiveUI
{
	/// <summary>
	/// The unit one term inside a <c>calc()</c> is measured in.
	/// </summary>
	/// <remarks>
	/// Deliberately not <see cref="LengthUnit"/>. An expression also has to carry the unitless
	/// numbers it scales by, and the times and ems that reach it from the properties whose syntax is
	/// neither a length nor a plain number.
	/// </remarks>
	internal enum CalcUnit : byte
	{
		Scalar,
		Points,
		Rem,
		Percent,
		Seconds,
		Em,
	}

	/// <summary>
	/// What the finished expression has to come out as — the property's syntax, not anything the
	/// expression itself says.
	/// </summary>
	internal enum CalcOutput : byte
	{
		/// <summary>A custom property: whatever the arithmetic works out to, since nothing has read it yet.</summary>
		Untyped,
		Length,
		Number,
		Time,
		LetterSpacing,
		Count,
	}

	/// <summary>One operand mid-evaluation: a number and the unit it is counted in.</summary>
	internal readonly struct CalcTerm
	{
		internal readonly float Value;
		internal readonly CalcUnit Unit;

		internal CalcTerm(float value, CalcUnit unit)
		{
			Value = value;
			Unit = unit;
		}
	}

	internal enum CalcOpKind : byte
	{
		Literal,
		Var,
		Add,
		Subtract,
		Multiply,
		Divide,
	}

	internal readonly struct CalcOp : IEquatable<CalcOp>
	{
		internal readonly CalcOpKind Kind;
		internal readonly CalcUnit Unit;
		internal readonly float Value;
		internal readonly int VarId;

		private CalcOp(CalcOpKind kind, CalcUnit unit, float value, int varId)
		{
			Kind = kind;
			Unit = unit;
			Value = value;
			VarId = varId;
		}

		internal static CalcOp Literal(float value, CalcUnit unit) => new(CalcOpKind.Literal, unit, value, 0);

		internal static CalcOp Var(int varId) => new(CalcOpKind.Var, CalcUnit.Scalar, 0f, varId);

		internal static CalcOp Operator(CalcOpKind kind) => new(kind, CalcUnit.Scalar, 0f, 0);

		public bool Equals(CalcOp other)
		{
			return Kind == other.Kind && Unit == other.Unit && Value.Equals(other.Value) && VarId == other.VarId;
		}

		public override bool Equals(object? obj) => obj is CalcOp other && Equals(other);

		public override int GetHashCode() => HashCode.Combine((int)Kind, (int)Unit, Value, VarId);
	}

	/// <summary>
	/// A <c>calc()</c> that could not be folded when the sheet was built, because it reads a custom
	/// property.
	/// </summary>
	/// <remarks>
	/// Held as postfix ops rather than as a tree, so evaluating one allocates nothing but the value
	/// it produces. It resolves exactly where a bare <c>var()</c> resolves — once per (rule set,
	/// scope) pair, cached with the computed style — and an unresolvable name drops the declaration,
	/// as CSS does.
	/// </remarks>
	internal sealed class CalcExpr : IEquatable<CalcExpr>
	{
		/// <summary>
		/// The most ops one expression may hold. Evaluation stacks its operands, and this bound is
		/// what lets that stack be a <c>stackalloc</c> rather than a list.
		/// </summary>
		internal const int MaxOps = 64;

		private readonly CalcOp[] _ops;
		private readonly CalcOutput _output;

		internal CalcExpr(CalcOp[] ops, CalcOutput output)
		{
			_ops = ops;
			_output = output;
		}

		internal CalcOp[] Ops => _ops;

		internal CalcOutput Output => _output;

		internal bool TryResolve(IReadOnlyDictionary<int, StyleValue> scope, out StyleValue value)
		{
			return Calc.TryEvaluate(_ops, scope, _output, out value);
		}

		public bool Equals(CalcExpr? other)
		{
			if (other is null || other._output != _output || other._ops.Length != _ops.Length)
				return false;

			for (var i = 0; i < _ops.Length; i++)
			{
				if (!_ops[i].Equals(other._ops[i])) return false;
			}

			return true;
		}

		public override bool Equals(object? obj) => Equals(obj as CalcExpr);

		public override int GetHashCode()
		{
			var hash = HashCode.Combine((int)_output, _ops.Length);

			for (var i = 0; i < _ops.Length; i++) hash = HashCode.Combine(hash, _ops[i]);

			return hash;
		}
	}

	/// <summary>
	/// Reads <c>calc()</c>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// An expression whose every term is literal is folded here, and the rest of the framework never
	/// learns it was written as arithmetic. One that reads a custom property becomes a
	/// <see cref="CalcExpr"/> and folds during the cascade instead, where the scope is known.
	/// </para>
	/// <para>
	/// Every term must share one unit. What an expression produces is a single
	/// <see cref="StyleLength"/> — one number and one unit — and Yoga takes a point or a percent,
	/// never a sum of the two, so a mixed expression is rejected with a diagnostic rather than
	/// approximated into something that lays out wrong. Points and rems cannot mix either: the rem
	/// size is a runtime knob, so there is no pixel to fold against until a node is being styled.
	/// Multiplying or dividing by a unitless number is how a unit is meant to be scaled.
	/// </para>
	/// </remarks>
	internal static class Calc
	{
		internal static bool IsCalc(string text)
		{
			return text.StartsWith("calc(", StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// The output a property's syntax asks for, or null where arithmetic is meaningless — a
		/// colour, a keyword, a font. Those reject a <c>calc()</c> the way they reject any other
		/// value they cannot read.
		/// </summary>
		internal static CalcOutput? OutputFor(ValueSyntax syntax) => syntax switch
		{
			ValueSyntax.Length => CalcOutput.Length,
			ValueSyntax.Number => CalcOutput.Number,
			ValueSyntax.Time => CalcOutput.Time,
			ValueSyntax.LetterSpacing => CalcOutput.LetterSpacing,
			ValueSyntax.Count => CalcOutput.Count,
			_ => null,
		};

		/// <summary>
		/// Reads a whole <c>calc(...)</c> value.
		/// </summary>
		/// <param name="allowVars">
		/// False where the caller has nowhere to keep a deferred expression — inside a shadow, a
		/// checker or a gradient stop, whose lengths are baked in when the sheet is built. Such a
		/// call still folds a literal expression, and rejects one that reads a custom property so
		/// the declaration is dropped and named rather than silently misdrawn.
		/// </param>
		internal static bool TryParse(string text, CalcOutput output, bool allowVars, out StyleValue value)
		{
			value = default;

			var reader = new Reader(text);

			// The whole value is one expression, since a calc is not a term CSS lets you add to.
			if (!Factor(ref reader))
				return false;

			reader.SkipWhitespace();

			if (!reader.AtEnd || reader.Ops.Count == 0 || reader.Ops.Count > CalcExpr.MaxOps)
				return false;

			var ops = reader.Ops.ToArray();

			if (!reader.HasVars)
				return TryEvaluate(ops, null, output, out value);

			if (!allowVars)
				return false;

			value = StyleValue.OfReference(new CalcExpr(ops, output));

			return true;
		}

		internal static bool TryEvaluate(
			CalcOp[] ops, IReadOnlyDictionary<int, StyleValue>? scope, CalcOutput output, out StyleValue value)
		{
			value = default;

			if (ops.Length == 0 || ops.Length > CalcExpr.MaxOps)
				return false;

			Span<CalcTerm> stack = stackalloc CalcTerm[ops.Length];
			var count = 0;

			for (var i = 0; i < ops.Length; i++)
			{
				var op = ops[i];

				switch (op.Kind)
				{
					case CalcOpKind.Literal:
						stack[count++] = new CalcTerm(op.Value, op.Unit);

						continue;

					case CalcOpKind.Var:
						// A name nobody set drops the declaration, which is what an unresolvable
						// `var()` does on its own.
						if (scope is null
							|| !scope.TryGetValue(op.VarId, out var resolved)
							|| !TryTerm(resolved, out var term))
						{
							return false;
						}

						stack[count++] = term;

						continue;
				}

				if (count < 2 || !TryCombine(stack[count - 2], stack[count - 1], op.Kind, out var combined))
					return false;

				count--;
				stack[count - 1] = combined;
			}

			return count == 1 && TryConvert(stack[0], output, out value);
		}

		#region Grammar

		/// <summary>
		/// <c>product (('+' | '-') product)*</c>.
		/// </summary>
		/// <remarks>
		/// CSS requires whitespace on both sides of a <c>+</c> or a <c>-</c>, because without it the
		/// sign belongs to the number that follows — <c>calc(4px -2px)</c> is a syntax error rather
		/// than a subtraction. The rule is enforced instead of guessed at, so two spellings a reader
		/// cannot tell apart can never quietly mean different things.
		/// </remarks>
		private static bool Expression(ref Reader reader)
		{
			if (!Product(ref reader))
				return false;

			while (true)
			{
				var mark = reader.Index;
				var spaced = false;

				while (!reader.AtEnd && char.IsWhiteSpace(reader.Current))
				{
					reader.Index++;
					spaced = true;
				}

				if (reader.AtEnd || (reader.Current != '+' && reader.Current != '-'))
				{
					reader.Index = mark;

					return true;
				}

				var kind = reader.Current == '+' ? CalcOpKind.Add : CalcOpKind.Subtract;
				reader.Index++;

				if (!spaced || reader.AtEnd || !char.IsWhiteSpace(reader.Current))
					return false;

				if (!Product(ref reader))
					return false;

				reader.Ops.Add(CalcOp.Operator(kind));
			}
		}

		/// <summary>
		/// <c>factor (('*' | '/') factor)*</c>. Whitespace is optional around these two, as in CSS.
		/// </summary>
		private static bool Product(ref Reader reader)
		{
			if (!Factor(ref reader))
				return false;

			while (true)
			{
				var mark = reader.Index;
				reader.SkipWhitespace();

				if (reader.AtEnd || (reader.Current != '*' && reader.Current != '/'))
				{
					reader.Index = mark;

					return true;
				}

				var kind = reader.Current == '*' ? CalcOpKind.Multiply : CalcOpKind.Divide;
				reader.Index++;

				if (!Factor(ref reader))
					return false;

				reader.Ops.Add(CalcOp.Operator(kind));
			}
		}

		private static bool Factor(ref Reader reader)
		{
			reader.SkipWhitespace();

			if (reader.AtEnd)
				return false;

			// A nested calc is a parenthesised group, which is all CSS says it is.
			if (reader.StartsWith("calc("))
			{
				reader.Index += 5;

				return Group(ref reader);
			}

			if (reader.Current == '(')
			{
				reader.Index++;

				return Group(ref reader);
			}

			return reader.StartsWith("var(") ? Variable(ref reader) : Literal(ref reader);
		}

		private static bool Group(ref Reader reader)
		{
			if (!Expression(ref reader))
				return false;

			reader.SkipWhitespace();

			return reader.Take(')');
		}

		/// <summary>
		/// Reads <c>var(--name)</c>, dropping any fallback the way every other var site does.
		/// </summary>
		private static bool Variable(ref Reader reader)
		{
			var open = reader.Index + 3;
			var depth = 0;
			var close = -1;

			for (var i = open; i < reader.Text.Length; i++)
			{
				var c = reader.Text[i];

				if (c == '(')
				{
					depth++;
				}
				else if (c == ')' && --depth == 0)
				{
					close = i;

					break;
				}
			}

			if (close < 0)
				return false;

			var inner = reader.Text.Substring(open + 1, close - open - 1);
			var comma = inner.IndexOf(',');

			if (comma >= 0)
				inner = inner.Substring(0, comma);

			var name = inner.Trim();

			if (name.Length == 0)
				return false;

			reader.Ops.Add(CalcOp.Var(ClassTable.Intern(name)));
			reader.HasVars = true;
			reader.Index = close + 1;

			return true;
		}

		private static bool Literal(ref Reader reader)
		{
			var start = reader.Index;

			if (reader.Current == '+' || reader.Current == '-')
				reader.Index++;

			while (!reader.AtEnd
				&& (char.IsLetterOrDigit(reader.Current) || reader.Current == '.' || reader.Current == '%'))
			{
				reader.Index++;
			}

			if (reader.Index == start)
				return false;

			if (!TryTerm(reader.Text.Substring(start, reader.Index - start), out var term))
				return false;

			reader.Ops.Add(CalcOp.Literal(term.Value, term.Unit));

			return true;
		}

		#endregion

		#region Terms

		private static bool TryTerm(string token, out CalcTerm term)
		{
			// Longest suffix first, so `rem` is not read as an `em` and `ms` is not read as an `s`.
			if (TrySuffix(token, "rem", out var value))
				term = new CalcTerm(value, CalcUnit.Rem);
			else if (TrySuffix(token, "em", out value))
				term = new CalcTerm(value, CalcUnit.Em);
			else if (TrySuffix(token, "px", out value))
				term = new CalcTerm(value, CalcUnit.Points);
			else if (TrySuffix(token, "ms", out value))
				term = new CalcTerm(value / 1000f, CalcUnit.Seconds);
			else if (TrySuffix(token, "s", out value))
				term = new CalcTerm(value, CalcUnit.Seconds);
			else if (TrySuffix(token, "%", out value))
				term = new CalcTerm(value, CalcUnit.Percent);
			else if (float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
				term = new CalcTerm(value, CalcUnit.Scalar);
			else
			{
				term = default;

				return false;
			}

			return true;
		}

		/// <summary>
		/// Reads the value a custom property held as a term. A colour or a resource has no place in
		/// arithmetic, and <c>auto</c> is not a number, so either drops the declaration.
		/// </summary>
		private static bool TryTerm(StyleValue value, out CalcTerm term)
		{
			term = default;

			switch (value.Kind)
			{
				case StyleValueKind.Length:
					var length = value.AsLength();

					switch (length.Unit)
					{
						case LengthUnit.Points:
							term = new CalcTerm(length.Value, CalcUnit.Points);

							return true;

						case LengthUnit.Rem:
							term = new CalcTerm(length.Value, CalcUnit.Rem);

							return true;

						case LengthUnit.Percent:
							term = new CalcTerm(length.Value, CalcUnit.Percent);

							return true;
					}

					return false;

				case StyleValueKind.Number:
					term = new CalcTerm(value.AsNumber(), CalcUnit.Scalar);

					return true;
			}

			return false;
		}

		private static bool TrySuffix(string token, string suffix, out float value)
		{
			value = 0f;

			return token.Length > suffix.Length
				&& token.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
				&& float.TryParse(
					token.Substring(0, token.Length - suffix.Length),
					NumberStyles.Float,
					CultureInfo.InvariantCulture,
					out value);
		}

		/// <summary>
		/// CSS's unit algebra: a sum needs both sides in one unit, a product needs one side
		/// unitless, and a quotient needs a unitless divisor.
		/// </summary>
		private static bool TryCombine(in CalcTerm left, in CalcTerm right, CalcOpKind kind, out CalcTerm result)
		{
			result = default;

			switch (kind)
			{
				case CalcOpKind.Add:
				case CalcOpKind.Subtract:
					if (left.Unit != right.Unit)
						return false;

					result = new CalcTerm(
						kind == CalcOpKind.Add ? left.Value + right.Value : left.Value - right.Value,
						left.Unit);

					return true;

				case CalcOpKind.Multiply:
					if (left.Unit != CalcUnit.Scalar && right.Unit != CalcUnit.Scalar)
						return false;

					result = new CalcTerm(
						left.Value * right.Value,
						left.Unit == CalcUnit.Scalar ? right.Unit : left.Unit);

					return true;

				case CalcOpKind.Divide:
					if (right.Unit != CalcUnit.Scalar || right.Value == 0f)
						return false;

					result = new CalcTerm(left.Value / right.Value, left.Unit);

					return true;
			}

			return false;
		}

		private static bool TryConvert(in CalcTerm term, CalcOutput output, out StyleValue value)
		{
			value = default;

			switch (output)
			{
				case CalcOutput.Length:
					return TryLength(term, out value);

				case CalcOutput.Number:
					// The plain parser strips a stray `px` or `em` off a number property rather than
					// rejecting it; the two agree here so calc is not the stricter spelling.
					if (term.Unit is not (CalcUnit.Scalar or CalcUnit.Points or CalcUnit.Em))
						return false;

					value = StyleValue.OfNumber(term.Value);

					return true;

				case CalcOutput.Time:
					if (term.Unit != CalcUnit.Seconds)
						return false;

					value = StyleValue.OfNumber(term.Value);

					return true;

				case CalcOutput.LetterSpacing:
					// Ems are stored as hundredths, as the plain parser stores them.
					if (term.Unit == CalcUnit.Em)
						value = StyleValue.OfNumber(term.Value * 100f);
					else if (term.Unit == CalcUnit.Scalar)
						value = StyleValue.OfNumber(term.Value);
					else
						return false;

					return true;

				case CalcOutput.Count:
					if (term.Unit != CalcUnit.Scalar || term.Value < 0f)
						return false;

					value = StyleValue.OfNumber(term.Value);

					return true;

				case CalcOutput.Untyped:
					if (term.Unit is CalcUnit.Scalar or CalcUnit.Seconds or CalcUnit.Em)
					{
						value = StyleValue.OfNumber(term.Value);

						return true;
					}

					return TryLength(term, out value);
			}

			return false;
		}

		private static bool TryLength(in CalcTerm term, out StyleValue value)
		{
			switch (term.Unit)
			{
				case CalcUnit.Points:
					value = StyleValue.OfLength(new StyleLength(term.Value));

					return true;

				case CalcUnit.Rem:
					value = StyleValue.OfLength(new StyleLength(term.Value, LengthUnit.Rem));

					return true;

				case CalcUnit.Percent:
					value = StyleValue.OfLength(new StyleLength(term.Value, LengthUnit.Percent));

					return true;

				// A bare number is a length only when it is zero, as it is everywhere else in CSS.
				case CalcUnit.Scalar when term.Value == 0f:
					value = StyleValue.OfLength(new StyleLength(0f));

					return true;
			}

			value = default;

			return false;
		}

		#endregion

		private struct Reader
		{
			internal readonly string Text;
			internal readonly List<CalcOp> Ops;

			internal int Index;
			internal bool HasVars;

			internal Reader(string text)
			{
				Text = text;
				Ops = new List<CalcOp>(8);
				Index = 0;
				HasVars = false;
			}

			internal bool AtEnd => Index >= Text.Length;

			internal char Current => Text[Index];

			internal void SkipWhitespace()
			{
				while (Index < Text.Length && char.IsWhiteSpace(Text[Index])) Index++;
			}

			internal bool Take(char c)
			{
				if (AtEnd || Text[Index] != c) return false;

				Index++;

				return true;
			}

			internal bool StartsWith(string token)
			{
				return string.Compare(Text, Index, token, 0, token.Length, StringComparison.OrdinalIgnoreCase) == 0;
			}
		}
	}
}
