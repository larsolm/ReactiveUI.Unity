using System;
using System.Collections.Generic;

namespace ReactiveUI.Editor
{
	/// <summary>
	/// Compiles one <c>.css</c> file into what a <see cref="CompiledStyleSheet"/> holds.
	/// </summary>
	internal static class CssCompiler
	{
		internal sealed class Result
		{
			/// <summary>The built sheet, or null if the file could not be parsed at all.</summary>
			public StyleSheet? Sheet;

			public byte[] Data = Array.Empty<byte>();
			public string[] Classes = Array.Empty<string>();
			public string[] UnscopedClasses = Array.Empty<string>();
			public CompiledStyleSheet.Import[] Imports = Array.Empty<CompiledStyleSheet.Import>();
			public readonly List<string> Diagnostics = new();
		}

		internal static Result Compile(string css, string path)
		{
			var result = new Result();
			var parsed = CssBuilder.Parse(css, path, result.Diagnostics);

			if (parsed is null)
				return result;

			var sheet = CssBuilder.Build(parsed, path, result.Diagnostics);

			result.Sheet = sheet;
			result.Imports = CssBuilder.ReadImports(parsed, path, result.Diagnostics);
			result.Classes = ClassNames(sheet);
			result.UnscopedClasses = UnscopedClasses(sheet);

			try
			{
				result.Data = StyleSheetSerializer.Write(sheet);
			}
			catch (NotSupportedException ex)
			{
				result.Diagnostics.Add($"{path}: {ex.Message}");
			}

			return result;
		}

		/// <summary>
		/// Every class a selector in the sheet names, sorted.
		/// </summary>
		/// <remarks>
		/// Read from the built sheet rather than rescanned from the text, so comments, strings and
		/// anything else the parser already resolved cannot be mistaken for a selector.
		/// </remarks>
		private static string[] ClassNames(StyleSheet sheet)
		{
			var names = new List<string>();
			var seen = new HashSet<string>(StringComparer.Ordinal);

			foreach (var simple in sheet.Simples)
			{
				if (simple.Kind != SelectorKind.Class)
					continue;

				var name = ClassTable.NameOf(simple.Value);

				if (name.Length > 0 && seen.Add(name))
					names.Add(name);
			}

			names.Sort(StringComparer.Ordinal);

			return names.ToArray();
		}

		/// <summary>
		/// Classes this sheet styles with nothing else narrowing them, first-seen order.
		/// </summary>
		/// <remarks>
		/// Only a rule that is a single compound naming a single class, outside any <c>@scope</c>, counts.
		/// That is the spelling with nothing to confine it: it styles the class wherever the class lands,
		/// including inside another component. More than one compound means an ancestor or sibling has to
		/// match too, and a scope bounds the rule to its own subtree.
		/// </remarks>
		private static string[] UnscopedClasses(StyleSheet sheet)
		{
			var names = new List<string>();
			var seen = new HashSet<string>(StringComparer.Ordinal);

			foreach (var rule in sheet.Rules)
			{
				if (rule.ScopeIndex != 0)
					continue;

				var selector = sheet.Selectors[rule.SelectorIndex];

				if (selector.CompoundCount != 1)
					continue;

				var compound = sheet.Compounds[selector.CompoundStart];
				var name = "";
				var classes = 0;

				for (var i = 0; i < compound.Count; i++)
				{
					var simple = sheet.Simples[compound.Start + i];

					// A pseudo-class narrows by state rather than by identity, so it still leaves the
					// rule free to land on any node carrying the class.
					if (simple.Kind == SelectorKind.PseudoClass)
						continue;

					if (simple.Kind != SelectorKind.Class)
					{
						classes = 0;

						break;
					}

					classes++;
					name = ClassTable.NameOf(simple.Value);
				}

				if (classes == 1 && name.Length > 0 && seen.Add(name))
					names.Add(name);
			}

			return names.ToArray();
		}
	}
}
