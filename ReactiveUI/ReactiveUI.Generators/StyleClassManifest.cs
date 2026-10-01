using System;
using System.Collections.Generic;

namespace ReactiveUI.Generators
{
	/// <summary>
	/// One stylesheet's entry in the manifest the editor writes.
	/// </summary>
	/// <param name="Sheet">The stylesheet, relative to the Unity project.</param>
	/// <param name="Assembly">The assembly that owns the sheet's folder, whose compilation generates its table.</param>
	/// <param name="Namespace">That assembly's root namespace, where a shared sheet's <c>Ui</c> table goes.</param>
	/// <param name="Classes">Every class selector in the sheet, as written in CSS.</param>
	/// <remarks>
	/// Whether a sheet belongs to a component is deliberately not recorded here: the compilation knows
	/// whether a type named after the sheet is declared beside it, and deciding it there means adding or
	/// removing that <c>.cs</c> never needs the manifest rewritten.
	/// </remarks>
	internal sealed record SheetEntry(
		string Sheet,
		string Assembly,
		string Namespace,
		EquatableArray<string> Classes);

	/// <summary>
	/// Reads the manifest the ReactiveUI editor keeps of every stylesheet's class names.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This file is how stylesheets reach the compiler at all. Unity passes a generator only assets
	/// named <c>Name.[Generator].additionalfile</c>, so the <c>.css</c> files themselves are invisible
	/// here. The editor already parses every sheet for hot reload, so it writes down the one thing the
	/// compiler needs from them, and rewrites the file only when a class is added or renamed — a
	/// value-only edit still restyles a running game without a recompile.
	/// </para>
	/// <para>
	/// Line-based rather than JSON because a generator cannot take a dependency on a parser:
	/// </para>
	/// <code>
	/// sheet Assets/UI/Button.css
	/// assembly Game.UI
	/// namespace Game.UI
	/// class btn
	/// class btn__label
	/// </code>
	/// </remarks>
	internal static class StyleClassManifest
	{
		/// <summary>
		/// The manifest's file name up to the generator's name, which Unity requires to carry no dot.
		/// </summary>
		public const string FilePrefix = "StyleClasses.";
		public const string FileSuffix = ".additionalfile";

		public static bool IsManifest(string path)
		{
			var name = System.IO.Path.GetFileName(path);

			return name.StartsWith(FilePrefix, StringComparison.Ordinal)
				&& name.EndsWith(FileSuffix, StringComparison.Ordinal);
		}

		public static EquatableArray<SheetEntry> Parse(string text)
		{
			var entries = new List<SheetEntry>();

			string? sheet = null;
			string assembly = string.Empty;
			string ns = string.Empty;
			var classes = new List<string>();

			void Flush()
			{
				if (sheet is not null)
					entries.Add(new SheetEntry(sheet, assembly, ns, new EquatableArray<string>(classes.ToArray())));

				sheet = null;
				assembly = string.Empty;
				ns = string.Empty;
				classes.Clear();
			}

			foreach (var raw in text.Split('\n'))
			{
				var line = raw.Trim();

				if (line.Length == 0 || line[0] == '#')
					continue;

				var space = line.IndexOf(' ');
				var key = space < 0 ? line : line.Substring(0, space);
				var value = space < 0 ? string.Empty : line.Substring(space + 1).Trim();

				switch (key)
				{
					case "sheet":
						Flush();
						sheet = value;
						break;

					case "assembly":
						assembly = value;
						break;

					case "namespace":
						ns = value;
						break;

					case "class":
						if (value.Length > 0)
							classes.Add(value);
						break;
				}
			}

			Flush();

			return new EquatableArray<SheetEntry>(entries.ToArray());
		}
	}
}
