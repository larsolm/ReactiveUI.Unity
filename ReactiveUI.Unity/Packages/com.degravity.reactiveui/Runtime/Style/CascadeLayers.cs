using System;
using System.Collections.Generic;

namespace ReactiveUI
{
	/// <summary>
	/// Puts every <c>@layer</c> the loaded sheets name into one order, as CSS Cascade 5 defines it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A layer's position is decided by where it is first named across the whole set of sheets, taken
	/// in cascade order, so no single sheet can know its own ranks. Each compiled sheet records its layer
	/// names, and this ranks them once the set is known.
	/// </para>
	/// <para>
	/// Layers form a tree — <c>framework.utilities</c> sits inside <c>framework</c> — and ranks come
	/// from walking it children-first: an earlier layer loses to a later one, a sublayer loses to the
	/// rules written directly in its parent, and rules outside every layer beat all of them.
	/// </para>
	/// <para>
	/// A sheet brought in with <c>@import "x.css" layer(name)</c> sits wholly inside that layer: its
	/// unlayered rules take the layer's rank and its own layers nest beneath it. The first importer, in
	/// cascade order, decides; a sheet is still loaded only once, so two importers asking for different
	/// layers cannot both be honoured, and the second is named.
	/// </para>
	/// </remarks>
	internal static class CascadeLayers
	{
		/// <summary>
		/// Ranks every source's layers from its metadata, without loading any sheet.
		/// </summary>
		/// <returns>
		/// Per source, the value for <see cref="StyleSheet.LayerRanks"/>: index 0 for its unlayered rules,
		/// then one per entry of <see cref="CompiledStyleSheet.LayerNames"/>.
		/// </returns>
		internal static int[][] Rank(IReadOnlyList<CompiledStyleSheet> sources, List<string> diagnostics)
		{
			var importLayers = ImportLayers(sources, diagnostics, out var importers);
			var prefixes = new string[sources.Count];
			var root = new Node(string.Empty);

			for (var i = 0; i < sources.Count; i++)
			{
				var prefix = Prefix(sources[i].SourcePath, importLayers, importers, 0);
				prefixes[i] = prefix;

				if (prefix.Length > 0)
					root.Find(prefix);

				foreach (var local in sources[i].LayerNames)
					root.Find(Join(prefix, local));
			}

			var next = 0;
			root.AssignRanks(ref next);

			var ranks = new int[sources.Count][];

			for (var i = 0; i < sources.Count; i++)
			{
				var names = sources[i].LayerNames;
				var prefix = prefixes[i];
				var sheetRanks = new int[names.Count + 1];

				sheetRanks[0] = prefix.Length == 0 ? int.MaxValue : root.Find(prefix).Rank;

				for (var l = 0; l < names.Count; l++)
					sheetRanks[l + 1] = root.Find(Join(prefix, names[l])).Rank;

				ranks[i] = sheetRanks;
			}

			return ranks;
		}

		/// <summary>Which layer each imported sheet was asked to sit in, and by whom.</summary>
		private static Dictionary<string, string> ImportLayers(
			IReadOnlyList<CompiledStyleSheet> sources,
			List<string> diagnostics,
			out Dictionary<string, string> importers)
		{
			var layers = new Dictionary<string, string>(StringComparer.Ordinal);
			importers = new Dictionary<string, string>(StringComparer.Ordinal);

			foreach (var source in sources)
			{
				foreach (var import in source.Imports)
				{
					if (string.IsNullOrEmpty(import.Layer))
						continue;

					if (layers.TryGetValue(import.Path, out var existing))
					{
						if (!string.Equals(existing, import.Layer, StringComparison.Ordinal))
						{
							diagnostics.Add(
								$"{source.SourcePath}: imports {import.Path} into layer '{Describe(import.Layer)}', but "
								+ $"{importers[import.Path]} already imports it into '{Describe(existing)}'. A sheet is "
								+ "loaded once, so the first import's layer is kept.");
						}

						continue;
					}

					layers[import.Path] = import.Layer;
					importers[import.Path] = source.SourcePath;
				}
			}

			return layers;
		}

		/// <summary>The full layer a whole sheet sits in, through however many layered imports.</summary>
		private static string Prefix(
			string path,
			Dictionary<string, string> layers,
			Dictionary<string, string> importers,
			int depth)
		{
			// Import cycles are reported where the order is worked out; here they only need to end.
			if (depth > 32 || !layers.TryGetValue(path, out var layer))
				return string.Empty;

			return Join(Prefix(importers[path], layers, importers, depth + 1), layer);
		}

		private static string Join(string prefix, string name) => prefix.Length == 0 ? name : prefix + "." + name;

		/// <summary>An anonymous layer's generated name, made readable.</summary>
		private static string Describe(string layer) => layer.Length > 0 && layer[0] == AnonymousMarker ? "(anonymous)" : layer;

		/// <summary>
		/// Starts the name the compiler gives an anonymous layer. No CSS identifier can contain it, so
		/// a generated name can never be named again — which is the point of an anonymous layer.
		/// </summary>
		internal const char AnonymousMarker = '\u0001';

		private sealed class Node
		{
			private readonly string _name;
			private readonly List<Node> _children = new();

			internal int Rank;

			internal Node(string name)
			{
				_name = name;
			}

			/// <summary>Finds a dotted path below this node, adding whatever is missing in first-seen order.</summary>
			internal Node Find(string path)
			{
				var node = this;
				var start = 0;

				while (start <= path.Length)
				{
					var dot = path.IndexOf('.', start);
					var end = dot < 0 ? path.Length : dot;
					var segment = path.Substring(start, end - start);

					node = node.Child(segment);
					start = end + 1;

					if (dot < 0)
						break;
				}

				return node;
			}

			private Node Child(string name)
			{
				foreach (var child in _children)
				{
					if (string.Equals(child._name, name, StringComparison.Ordinal))
						return child;
				}

				var created = new Node(name);
				_children.Add(created);

				return created;
			}

			/// <summary>Ranks children before their parent, so a parent's own rules beat its sublayers.</summary>
			internal void AssignRanks(ref int next)
			{
				foreach (var child in _children)
					child.AssignRanks(ref next);

				Rank = next++;
			}
		}
	}
}
