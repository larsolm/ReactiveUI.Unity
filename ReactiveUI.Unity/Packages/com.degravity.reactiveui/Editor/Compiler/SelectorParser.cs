using System;
using System.Collections.Generic;

namespace ReactiveUI
{
	/// <summary>
	/// What <c>:scope</c> and <c>&amp;</c> mean where a selector is being parsed.
	/// </summary>
	internal readonly struct ScopeContext
	{
		/// <summary>
		/// Whether there is a scoping root to stand for. Outside any <c>@scope</c> both mean the root of
		/// the tree, which is <c>:root</c>.
		/// </summary>
		public readonly bool InScope;

		/// <summary>What <c>&amp;</c> adds: the specificity of the scope's root selectors, as <c>:is()</c> of them would.</summary>
		public readonly int AmpersandSpecificity;

		public ScopeContext(bool inScope, int ampersandSpecificity)
		{
			InScope = inScope;
			AmpersandSpecificity = ampersandSpecificity;
		}
	}

	internal sealed class SelectorParser
	{
		private readonly List<SimpleSelector> _simples;
		private readonly List<CompoundSelector> _compounds;
		private readonly List<string> _diagnostics;

		private ScopeContext _scope;
		private int _extraSpecificity;

		public SelectorParser(List<SimpleSelector> simples, List<CompoundSelector> compounds, List<string> diagnostics)
		{
			_simples = simples;
			_compounds = compounds;
			_diagnostics = diagnostics;
		}

		/// <summary>
		/// Whether a selector names its scoping root — <c>:scope</c> or <c>&amp;</c> — and so is not made
		/// relative to it.
		/// </summary>
		internal static bool ReferencesScope(string selector)
		{
			for (var i = 0; i < selector.Length; i++)
			{
				if (selector[i] == '&')
					return true;

				if (selector[i] == ':'
					&& string.Compare(selector, i + 1, "scope", 0, 5, StringComparison.OrdinalIgnoreCase) == 0
					&& (i + 6 >= selector.Length || !IsIdentifierPart(selector[i + 6])))
				{
					return true;
				}
			}

			return false;
		}

		public bool TryParse(string selector, out SelectorRecord record, in ScopeContext scope = default)
		{
			record = default;

			_scope = scope;
			_extraSpecificity = 0;

			var compoundStart = _compounds.Count;
			var classes = 0;
			var types = 0;
			var index = 0;
			var pendingCombinator = Combinator.None;
			var compoundCount = 0;

			SkipWhitespace(selector, ref index);

			while (index < selector.Length)
			{
				var simpleStart = _simples.Count;
				var stateMask = 0UL;

				if (!TryParseCompound(selector, ref index, ref classes, ref types, ref stateMask))
				{
					Rewind(compoundStart, compoundCount);

					return false;
				}

				if (_simples.Count == simpleStart)
				{
					Report($"'{selector}': expected a selector but found '{Remainder(selector, index)}'.");
					Rewind(compoundStart, compoundCount);

					return false;
				}

				// The combinator is recorded on the compound to its *left*, so a right-to-left walk
				// reads "how do I get from here to the node I just matched".
				if (compoundCount > 0)
				{
					var previous = _compounds[_compounds.Count - 1];
					_compounds[_compounds.Count - 1] = new CompoundSelector(previous.Start, previous.Count, pendingCombinator, previous.StateMask);
				}

				_compounds.Add(new CompoundSelector(simpleStart, _simples.Count - simpleStart, Combinator.None, stateMask));
				compoundCount++;

				if (!TryReadCombinator(selector, ref index, out pendingCombinator))
					break;
			}

			if (compoundCount == 0)
			{
				Report($"'{selector}': empty selector.");

				return false;
			}

			var selfMask = _compounds[_compounds.Count - 1].StateMask;
			var ancestorMask = 0UL;

			for (var i = compoundStart; i < _compounds.Count - 1; i++)
				ancestorMask |= _compounds[i].StateMask;

			record = new SelectorRecord(
				compoundStart,
				compoundCount,
				SelectorRecord.PackSpecificity(classes, types) + _extraSpecificity,
				selfMask,
				ancestorMask);

			return true;
		}

		private bool TryParseCompound(string selector, ref int index, ref int classes, ref int types, ref ulong stateMask)
		{
			while (index < selector.Length)
			{
				var c = selector[index];

				switch (c)
				{
					case '*':
						index++;
						_simples.Add(new SimpleSelector(SelectorKind.Universal, 0));

						continue;

					case '.':
					{
						index++;
						var name = ReadIdentifier(selector, ref index);
						if (name is null)
						{
							Report($"'{selector}': expected a class name after '.'.");

							return false;
						}

						classes++;
						_simples.Add(new SimpleSelector(SelectorKind.Class, ClassTable.Intern(name)));

						continue;
					}

					case '&':
					{
						// Inside a scope, `&` is the scoping root with the specificity of its selector; with
						// no scope it is the root of the tree, as `:scope` is.
						index++;

						if (_scope.InScope)
						{
							_simples.Add(new SimpleSelector(SelectorKind.ScopeRoot, 0));
							_extraSpecificity += _scope.AmpersandSpecificity;
						}
						else
						{
							AddRoot(ref classes, ref stateMask);
						}

						continue;
					}

					case '#':
					{
						// An id names the GameObject and takes no part in the cascade, so there is
						// nothing for `#hud` to match. Saying so beats a rule that silently never
						// applies; a rule meant for exactly one node wants a class of its own.
						index++;
						var name = ReadIdentifier(selector, ref index) ?? "";

						Report(
							$"'{selector}': id selectors are not supported — an id only names the "
							+ $"GameObject. Use a class such as '.{name}' instead.");

						return false;
					}

					case ':':
					{
						index++;

						// A pseudo-element is not something this framework has a concept of yet;
						// skipping the second colon keeps `::before` a clean diagnostic rather than
						// a mysterious parse failure.
						if (index < selector.Length && selector[index] == ':')
						{
							Report($"'{selector}': pseudo-elements are not supported.");

							return false;
						}

						var name = ReadIdentifier(selector, ref index);
						if (name is null)
						{
							Report($"'{selector}': expected a pseudo-class name after ':'.");

							return false;
						}

						if (name.Equals("scope", StringComparison.OrdinalIgnoreCase)
							&& (index >= selector.Length || selector[index] != '('))
						{
							// A pseudo-class, and counted as one.
							if (_scope.InScope)
							{
								classes++;
								_simples.Add(new SimpleSelector(SelectorKind.ScopeRoot, 0));
							}
							else
							{
								AddRoot(ref classes, ref stateMask);
							}

							continue;
						}

						// `:where(:scope)`: the scoping root, adding nothing to specificity.
						if (name.Equals("where", StringComparison.OrdinalIgnoreCase) && TryReadWhereScope(selector, ref index))
						{
							if (_scope.InScope)
							{
								_simples.Add(new SimpleSelector(SelectorKind.ScopeRoot, 0));
							}
							else
							{
								stateMask |= UiStates.s_root.Mask;
								_simples.Add(new SimpleSelector(SelectorKind.PseudoClass, UiStates.s_root._index));
							}

							continue;
						}

						// Functional pseudo-classes are parsed far enough to reject them clearly.
						if (index < selector.Length && selector[index] == '(')
						{
							var depth = 0;
							while (index < selector.Length)
							{
								if (selector[index] == '(')
								{
									depth++;
								}
								else if (selector[index] == ')' && --depth == 0)
								{
									index++;
									break;
								}

								index++;
							}

							Report($"'{selector}': ':{name}(...)' is not supported.");
							return false;
						}

						// Rejected rather than registered on sight. Every bit that means anything is set
						// by the framework, so minting one for an unrecognised name only ever produced a
						// rule that parsed cleanly and then matched nothing — a typo you find by
						// wondering why your style never applies.
						if (!UiStates.TryGet(name, out var bit))
						{
							Report($"'{selector}': ':{name}' is not a pseudo-class. Use a class for state a component decides itself.");
							return false;
						}

						classes++;
						stateMask |= bit.Mask;
						_simples.Add(new SimpleSelector(SelectorKind.PseudoClass, bit._index));

						continue;
					}

					default:
					{
						if (!IsIdentifierStart(c))
							return true;

						var name = ReadIdentifier(selector, ref index);
						if (name is null)
							return true;

						// Only the host primitives answer to a type name. A component's does not reach
						// the tree — nothing is forwarded down from one any more — so `Card { }` would
						// parse and match nothing at all.
						if (!IsHostTypeName(name))
						{
							Report($"'{selector}': '{name}' is not a host element. Only {HostTypeNames} match by type; give the component a class prop instead.");
							return false;
						}

						types++;
						_simples.Add(new SimpleSelector(SelectorKind.Type, ClassTable.Intern(name)));

						continue;
					}
				}
			}

			return true;
		}

		/// <summary>
		/// <c>:scope</c> with no scope: the root of the tree, which here is <c>:root</c>.
		/// </summary>
		private void AddRoot(ref int classes, ref ulong stateMask)
		{
			classes++;
			stateMask |= UiStates.s_root.Mask;
			_simples.Add(new SimpleSelector(SelectorKind.PseudoClass, UiStates.s_root._index));
		}

		/// <summary>
		/// Reads <c>(:scope)</c> at <paramref name="index"/>, advancing past it only when that is exactly
		/// what is there.
		/// </summary>
		private static bool TryReadWhereScope(string selector, ref int index)
		{
			if (index >= selector.Length || selector[index] != '(')
				return false;

			var close = selector.IndexOf(')', index);

			if (close < 0 || !selector.Substring(index + 1, close - index - 1).Trim().Equals(":scope", StringComparison.OrdinalIgnoreCase))
				return false;

			index = close + 1;

			return true;
		}

		/// <summary>The host primitives, which are the only types a selector can name.</summary>
		private static readonly string[] s_hostTypeNames = Enum.GetNames(typeof(HostKind));

		private static string HostTypeNames => string.Join(", ", s_hostTypeNames);

		private static bool IsHostTypeName(string name)
		{
			for (var i = 0; i < s_hostTypeNames.Length; i++)
			{
				if (string.Equals(s_hostTypeNames[i], name, StringComparison.Ordinal))
					return true;
			}

			return false;
		}

		private static bool TryReadCombinator(string selector, ref int index, out Combinator combinator)
		{
			var sawWhitespace = false;

			while (index < selector.Length && char.IsWhiteSpace(selector[index]))
			{
				sawWhitespace = true;
				index++;
			}

			if (index >= selector.Length)
			{
				combinator = Combinator.None;

				return false;
			}

			switch (selector[index])
			{
				case '>':
					index++;
					combinator = Combinator.Child;

					break;

				case '+':
					index++;
					combinator = Combinator.NextSibling;

					break;

				case '~':
					index++;
					combinator = Combinator.SubsequentSibling;

					break;

				default:
					combinator = Combinator.Descendant;

					// Without whitespace and without an explicit combinator there is nothing
					// separating two compounds, so the compound simply continues.
					return sawWhitespace;
			}

			SkipWhitespace(selector, ref index);

			return true;
		}

		private static void SkipWhitespace(string text, ref int index)
		{
			while (index < text.Length && char.IsWhiteSpace(text[index]))
				index++;
		}

		private static string? ReadIdentifier(string text, ref int index)
		{
			var start = index;

			while (index < text.Length && IsIdentifierPart(text[index]))
				index++;

			return index == start ? null : text.Substring(start, index - start);
		}

		private static bool IsIdentifierStart(char c)
		{
			return char.IsLetter(c) || c == '_' || c == '-';
		}

		private static bool IsIdentifierPart(char c)
		{
			return char.IsLetterOrDigit(c) || c == '_' || c == '-';
		}

		private static string Remainder(string text, int index)
		{
			return index < text.Length ? text.Substring(index) : "<end>";
		}

		private void Rewind(int compoundStart, int compoundCount)
		{
			if (compoundCount > 0)
				_simples.RemoveRange(_compounds[compoundStart].Start, CountSimples(compoundStart));

			_compounds.RemoveRange(compoundStart, _compounds.Count - compoundStart);
		}

		private int CountSimples(int compoundStart)
		{
			var total = 0;

			for (var i = compoundStart; i < _compounds.Count; i++)
				total += _compounds[i].Count;

			return total;
		}

		private void Report(string message)
		{
			_diagnostics.Add(message);
		}
	}
}
