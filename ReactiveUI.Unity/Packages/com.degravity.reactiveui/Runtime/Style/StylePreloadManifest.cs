using System;
using System.Collections.Generic;
using UnityEngine;

namespace ReactiveUI
{
	/// <summary>
	/// The sheets, fonts and textures each <see cref="UiRoot"/> loads when it starts, recorded in Play mode
	/// from what its tree used.
	/// </summary>
	public sealed class StylePreloadManifest : ScriptableObject
	{
		/// <summary>
		/// Where <see cref="StylePreloads"/> looks for the manifest, relative to a <c>Resources</c> folder.
		/// </summary>
		internal const string ResourcePath = "ReactiveUI/StylePreloads";

		/// <summary>What one root type loads at start.</summary>
		[Serializable]
		internal sealed class Entry
		{
			/// <summary>The root's full type name.</summary>
			public string Root = string.Empty;

			/// <summary>Source paths of the sheets to activate.</summary>
			public List<string> Sheets = new();

			/// <summary><c>Resources</c> paths of the font assets to load.</summary>
			public List<string> Fonts = new();

			/// <summary><c>Resources</c> paths of the textures to load.</summary>
			public List<string> Textures = new();

			/// <summary>The characters each font rendered, added to its atlas at start.</summary>
			public List<FontCharacters> Characters = new();

			internal List<string> Of(PreloadKind kind) => kind switch
			{
				PreloadKind.Sheet => Sheets,
				PreloadKind.Font => Fonts,
				_ => Textures,
			};
		}

		/// <summary>The characters one font rendered under a root.</summary>
		[Serializable]
		internal sealed class FontCharacters
		{
			/// <summary><c>Resources</c> path of the font asset.</summary>
			public string Font = string.Empty;

			/// <summary>Every code point rendered, once each, in ascending order.</summary>
			public string Characters = string.Empty;
		}

		[Tooltip("Recorded in Play mode from what each root's tree loads. Remove an entry, or a path from "
			+ "one, to have it recorded again.")]
		[SerializeField]
		private List<Entry> _entries = new();

		/// <summary>The entry for <paramref name="root"/>, or null if nothing is recorded for it.</summary>
		internal Entry? Find(string root)
		{
			foreach (var entry in _entries)
			{
				if (string.Equals(entry.Root, root, StringComparison.Ordinal))
					return entry;
			}

			return null;
		}

		private Entry FindOrAdd(string root, out bool added)
		{
			var entry = Find(root);

			added = entry is null;

			if (entry is null)
			{
				entry = new Entry { Root = root };
				_entries.Add(entry);
				_entries.Sort((a, b) => string.CompareOrdinal(a.Root, b.Root));
			}

			return entry;
		}

		/// <summary>
		/// Records <paramref name="path"/> under <paramref name="root"/>, keeping every list sorted.
		/// </summary>
		/// <returns>Whether it was not recorded already.</returns>
		internal bool Add(string root, PreloadKind kind, string path, out bool newRoot)
		{
			var entry = FindOrAdd(root, out newRoot);

			var list = entry.Of(kind);
			var index = list.BinarySearch(path, StringComparer.Ordinal);

			if (index >= 0)
				return false;

			list.Insert(~index, path);

			return true;
		}

		/// <summary>
		/// Adds <paramref name="codePoints"/> to what <paramref name="font"/> rendered under
		/// <paramref name="root"/>.
		/// </summary>
		/// <returns>Whether any of them was not recorded already.</returns>
		internal bool AddCharacters(string root, string font, IEnumerable<int> codePoints, out bool newRoot)
		{
			var entry = FindOrAdd(root, out newRoot);

			FontCharacters? record = null;

			foreach (var candidate in entry.Characters)
			{
				if (string.Equals(candidate.Font, font, StringComparison.Ordinal))
					record = candidate;
			}

			if (record is null)
			{
				record = new FontCharacters { Font = font };
				entry.Characters.Add(record);
				entry.Characters.Sort((a, b) => string.CompareOrdinal(a.Font, b.Font));
			}

			var set = new SortedSet<int>(CodePoints(record.Characters));
			var added = false;

			foreach (var codePoint in codePoints)
				added |= set.Add(codePoint);

			if (!added)
				return false;

			var text = new System.Text.StringBuilder(set.Count);

			foreach (var codePoint in set)
				text.Append(char.ConvertFromUtf32(codePoint));

			record.Characters = text.ToString();

			return true;
		}

		/// <summary>The code points of <paramref name="text"/>, reading surrogate pairs as one.</summary>
		internal static IEnumerable<int> CodePoints(string text)
		{
			for (var i = 0; i < text.Length; i++)
			{
				if (char.IsSurrogatePair(text, i))
				{
					yield return char.ConvertToUtf32(text, i);
					i++;
				}
				else if (!char.IsSurrogate(text[i]))
				{
					yield return text[i];
				}
			}
		}
	}
}
