using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace ReactiveUI.Editor
{
	/// <summary>
	/// Writes down every stylesheet's class names for <c>ReactiveUI.Generators</c>, which turns them into
	/// <c>ClassName</c> constants at compile time.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The compiler cannot read the stylesheets itself: Unity hands a generator only assets named
	/// <c>Name.[Generator].additionalfile</c> under <c>Assets/</c>. Each compiled sheet already records
	/// the classes it names, so this writes down the one thing compilation needs from them — which
	/// classes each sheet declares, and which assembly and type they belong to.
	/// </para>
	/// <para>
	/// The file is rewritten only when its text actually changes, which is what preserves the point of
	/// keeping styles in data: retuning a value in a <c>.css</c> still restyles a running game with no
	/// domain reload. Only adding, removing or renaming a class costs a recompile.
	/// </para>
	/// </remarks>
	internal static class StyleClassManifestWriter
	{
		/// <summary>
		/// Unity parses the generator's name out of the file name, so the part before it must carry no dot.
		/// </summary>
		internal const string ManifestPath = "Assets/Plugins/ReactiveUI/StyleClasses.ReactiveUI.Generators.additionalfile";

		public static void Rebuild(IReadOnlyList<CompiledStyleSheet> sheets)
		{
			var text = new StringBuilder(4096);
			var entries = 0;

			// A class is global even though the tables that name it are per component, so two sheets
			// reaching for the same name have to be reconciled across the whole run.
			var declaredIn = new Dictionary<string, string>(StringComparer.Ordinal);

			Line(text, "# Written by the ReactiveUI editor from the project's stylesheets and read by ReactiveUI.Generators.");
			Line(text, "# Rewritten only when a class selector is added, removed or renamed. Do not edit.");

			foreach (var sheet in sheets)
			{
				var path = CssAssets.SourcePath(sheet);
				var classes = sheet.Classes;

				if (classes.Count == 0)
					continue;

				WarnOnSharedClasses(sheet, path, declaredIn);

				// Unity resolves a script path to its assembly by folder, whether or not the file exists, so
				// a sibling .cs path answers which assembly owns the sheet. Whether that .cs exists — and
				// so whether the sheet is a component's or shared — is left for the generator to see in
				// the compilation; see StyleClassGenerator.
				var probe = Path.ChangeExtension(path, ".cs");

				Line(text);
				Line(text, "sheet " + path);
				Line(text, "assembly " + AssemblyOf(probe));
				Line(text, "namespace " + (CompilationPipeline.GetAssemblyRootNamespaceFromScriptPath(probe) ?? string.Empty));

				foreach (var css in classes)
					Line(text, "class " + css);

				entries++;
			}

			Write(text.ToString(), entries);
		}

		/// <summary>
		/// Newlines are written explicitly so the file is identical on every platform, which is what the
		/// unchanged-text check depends on.
		/// </summary>
		private static void Line(StringBuilder text, string line = "") => text.Append(line).Append('\n');

		private static string AssemblyOf(string scriptPath)
		{
			var name = CompilationPipeline.GetAssemblyNameFromScriptPath(scriptPath) ?? string.Empty;

			return name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? name.Substring(0, name.Length - 4) : name;
		}

		/// <summary>
		/// Writes the manifest when its content changed, and only then.
		/// </summary>
		/// <remarks>
		/// Compared against the exact text on disk rather than a hash — there is no bookkeeping to get
		/// wrong, and an equal file is genuinely equal. A project with no class selectors never gets the
		/// file at all.
		/// </remarks>
		private static void Write(string text, int entries)
		{
			string? current = null;

			try
			{
				if (File.Exists(ManifestPath))
					current = File.ReadAllText(ManifestPath);
			}
			catch (Exception ex)
			{
				Debug.LogError($"[ReactiveUI] Could not read {ManifestPath}: {ex.Message}");
			}

			if (string.Equals(current, text, StringComparison.Ordinal) || (current is null && entries == 0))
				return;

			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(ManifestPath)!);
				File.WriteAllText(ManifestPath, text);
			}
			catch (Exception ex)
			{
				Debug.LogError($"[ReactiveUI] Could not write {ManifestPath}: {ex.Message}");

				return;
			}

			AssetDatabase.ImportAsset(ManifestPath);
			Debug.Log($"[ReactiveUI] Style classes changed in {entries} stylesheet(s); recompiling their tables.");
		}

		/// <summary>
		/// Warns when two sheets both style a class on its own, recording the first to claim each.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The generator reconciles C# member names <em>within</em> one table and never sees this:
		/// <see cref="ClassName.Intern"/> is process-wide, so a name two components both reach for is one
		/// class, and whichever rule is more specific wins on a node carrying it in either.
		/// </para>
		/// <para>
		/// Only a rule that is a single compound naming a single class, outside any <c>@scope</c>, counts
		/// as a claim. That is the spelling with nothing to confine it — it styles the class wherever the
		/// class lands, including inside another component — so two of them are a genuine collision. A
		/// class reached through an ancestor (<c>.identity &gt; .icon</c>), qualified by another class
		/// (<c>.map-node.map-node-type--start</c>) or written inside a scope cannot escape its own subtree
		/// and is left alone, which is what keeps short names from filling the console.
		/// </para>
		/// </remarks>
		private static void WarnOnSharedClasses(CompiledStyleSheet sheet, string path, Dictionary<string, string> declaredIn)
		{
			foreach (var css in sheet.UnscopedClasses)
			{
				if (declaredIn.TryGetValue(css, out var first))
				{
					Debug.LogWarning(
						$"[ReactiveUI] `.{css}` is styled on its own by both {first} and {path}. A class is "
						+ "global, so each sheet's rule applies to the other's nodes wherever the name lands. "
						+ "Rename one, or confine it with '@scope (.component-root) { … }'.");

					continue;
				}

				declaredIn[css] = path;
			}
		}
	}
}
