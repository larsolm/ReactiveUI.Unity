using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace ReactiveUI
{
	/// <summary>
	/// Writes a built <see cref="StyleSheet"/> to bytes and reads it back, which is what lets the CSS
	/// library stay in the editor: sheets are compiled when they are imported, and everything that
	/// renders — the editor included — loads the result.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Almost everything is written as it is held. The exception is anything that is an id in
	/// <see cref="ClassTable"/> — class and type names, custom property names, keyframe names and
	/// the <c>var()</c> references inside values — because those ids are handed out per process in
	/// whatever order names are first seen. They are written as indices into a string table and
	/// interned again on load.
	/// </para>
	/// <para>
	/// Pseudo-class bits are the same kind of thing: a project registers its own states through
	/// <see cref="UiStates.Register"/>, and the order that happens in is not something a sheet compiled in
	/// the editor can count on. The names are written alongside, and the masks remapped on load.
	/// </para>
	/// <para>
	/// Enum values and keyword ids are written raw. They are only stable for one build of this
	/// assembly, and that is enough: the importer depends on the assembly's identity, so a sheet is
	/// recompiled whenever any of them could have moved.
	/// </para>
	/// </remarks>
	internal static class StyleSheetSerializer
	{
		/// <summary>Bumped whenever the layout below changes.</summary>
		internal const int FormatVersion = 4;

		private const uint Magic = 0x53535552; // "RUSS"

		private enum ReferenceKind : byte
		{
			None,
			String,
			Calc,
			ShadowList,
			Checker,
			Gradient,
			VarColor,
			PendingTransition,
			PendingTransform,
		}

		#region Writing

		internal static byte[] Write(StyleSheet sheet)
		{
			var names = new NameTable();
			var body = new MemoryStream();

			using (var writer = new BinaryWriter(body, Encoding.UTF8, leaveOpen: true))
				WriteBody(writer, sheet, names);

			var output = new MemoryStream((int)body.Length + 256);

			using (var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true))
			{
				writer.Write(Magic);
				writer.Write(FormatVersion);
				writer.Write(names.Strings.Count);

				foreach (var name in names.Strings)
					writer.Write(name);

				writer.Write(UiStates.Names.Count);

				foreach (var state in UiStates.Names)
					writer.Write(state);

				writer.Write(body.GetBuffer(), 0, (int)body.Length);
			}

			return output.ToArray();
		}

		private static void WriteBody(BinaryWriter writer, StyleSheet sheet, NameTable names)
		{
			writer.Write(sheet.Simples.Length);

			foreach (var simple in sheet.Simples)
			{
				writer.Write((byte)simple.Kind);
				writer.Write(simple.Kind is SelectorKind.Class or SelectorKind.Type ? names.Index(simple.Value) : simple.Value);
			}

			writer.Write(sheet.Compounds.Length);

			foreach (var compound in sheet.Compounds)
			{
				writer.Write(compound.Start);
				writer.Write(compound.Count);
				writer.Write((byte)compound.ToNext);
				writer.Write(compound.StateMask);
			}

			writer.Write(sheet.Selectors.Length);

			foreach (var selector in sheet.Selectors)
			{
				writer.Write(selector.CompoundStart);
				writer.Write(selector.CompoundCount);
				writer.Write(selector.Specificity);
				writer.Write(selector.SelfStateMask);
				writer.Write(selector.AncestorStateMask);
			}

			writer.Write(sheet.Rules.Length);

			foreach (var rule in sheet.Rules)
			{
				writer.Write(rule.SelectorIndex);
				writer.Write(rule.DeclarationStart);
				writer.Write(rule.DeclarationCount);
				writer.Write(rule.MediaQueryIndex);
				writer.Write(rule.ScopeIndex);
				writer.Write(rule.LayerIndex);
			}

			writer.Write(sheet.Declarations.Length);

			foreach (var declaration in sheet.Declarations)
			{
				writer.Write((int)declaration.Id);
				writer.Write(declaration.IsCustom ? names.Index(declaration.CustomNameId) : -1);
				WriteValue(writer, declaration.Value, declaration.Id, names);
			}

			writer.Write(sheet.Keyframes.Length);

			foreach (var clip in sheet.Keyframes)
			{
				writer.Write(names.Index(clip.NameId));
				writer.Write(clip.Tracks.Length);

				foreach (var track in clip.Tracks)
				{
					writer.Write((byte)track.Channel);
					writer.Write(track.Stops.Length);

					foreach (var stop in track.Stops)
					{
						writer.Write(stop.Offset);
						WriteValue(writer, stop.Value, PropId.None, names);
						writer.Write((int)stop.Easing);
						writer.Write(stop.HasEasing);
					}
				}
			}

			writer.Write(sheet.MediaQueries.Length);

			foreach (var query in sheet.MediaQueries)
			{
				writer.Write(query.ClauseStart);
				writer.Write(query.ClauseCount);
				writer.Write(query.ParentIndex);
				writer.Write(query.NeverMatches);
			}

			writer.Write(sheet.MediaClauses.Length);

			foreach (var clause in sheet.MediaClauses)
			{
				writer.Write(clause.FeatureStart);
				writer.Write(clause.FeatureCount);
				writer.Write(clause.IsInverse);
				writer.Write(clause.TypeMatches);
			}

			writer.Write(sheet.MediaFeatures.Length);

			foreach (var feature in sheet.MediaFeatures)
			{
				writer.Write((byte)feature.Kind);
				writer.Write((byte)feature.Comparison);
				WriteLength(writer, feature.Length);
				writer.Write(feature.Number);
				writer.Write(feature.Keyword);
			}

			writer.Write(sheet.Scopes.Length);

			foreach (var scope in sheet.Scopes)
			{
				writer.Write(scope.RootStart);
				writer.Write(scope.RootCount);
				writer.Write(scope.LimitStart);
				writer.Write(scope.LimitCount);
				writer.Write(scope.ParentIndex);
			}

			writer.Write(sheet.LayerNames.Length);

			foreach (var layer in sheet.LayerNames)
				writer.Write(layer);

			writer.Write(sheet.FontFaces.Length);

			foreach (var face in sheet.FontFaces)
			{
				writer.Write(face.Family);
				writer.Write(face.ResourcePath);
				writer.Write(face.Weight);
			}
		}

		/// <param name="property">
		/// The property the value belongs to. <c>animation-name</c> is the one keyword that carries a
		/// name rather than an enum member, and only the property says so.
		/// </param>
		private static void WriteValue(BinaryWriter writer, in StyleValue value, PropId property, NameTable names)
		{
			var tag = value.Tag;
			var kind = value.Kind;

			if (kind == StyleValueKind.VarReference
				|| (kind == StyleValueKind.Keyword && property == PropId.AnimationName && value.AsKeyword() != 0))
			{
				// The payload above the kind byte is a name id; swap it for a table index.
				tag = (int)kind | (names.Index(tag >> 8) << 8);
			}

			writer.Write(value.A);
			writer.Write(value.B);
			writer.Write(value.C);
			writer.Write(value.D);
			writer.Write(tag);

			WriteReference(writer, value.Reference, names);
		}

		private static void WriteReference(BinaryWriter writer, object? reference, NameTable names)
		{
			switch (reference)
			{
				case null:
					writer.Write((byte)ReferenceKind.None);

					break;

				case string text:
					writer.Write((byte)ReferenceKind.String);
					writer.Write(text);

					break;

				case CalcExpr calc:
					writer.Write((byte)ReferenceKind.Calc);
					writer.Write((byte)calc.Output);
					writer.Write(calc.Ops.Length);

					foreach (var op in calc.Ops)
					{
						writer.Write((byte)op.Kind);
						writer.Write((byte)op.Unit);
						writer.Write(op.Value);
						writer.Write(op.Kind == CalcOpKind.Var ? names.Index(op.VarId) : 0);
					}

					break;

				case ShadowList shadows:
					writer.Write((byte)ReferenceKind.ShadowList);
					writer.Write(shadows.Count);

					for (var i = 0; i < shadows.Count; i++)
					{
						var shadow = shadows[i];

						WriteLength(writer, shadow.OffsetX);
						WriteLength(writer, shadow.OffsetY);
						WriteLength(writer, shadow.Blur);
						WriteLength(writer, shadow.Spread);
						WriteInk(writer, shadow._ink, names);
					}

					break;

				case Checker checker:
					writer.Write((byte)ReferenceKind.Checker);
					WriteLength(writer, checker.CellSize);
					WriteLength(writer, checker.LineWidth);
					WriteInk(writer, checker.Ink, names);
					writer.Write(checker.RowsOnly);

					break;

				case Gradient gradient:
					writer.Write((byte)ReferenceKind.Gradient);
					writer.Write((byte)gradient.Kind);
					writer.Write(gradient.Angle);
					writer.Write(gradient.Stops.Count);

					foreach (var stop in gradient.Stops)
					{
						writer.Write(stop.Position);
						WriteInk(writer, stop._ink, names);
					}

					break;

				case VarColorValue color:
					writer.Write((byte)ReferenceKind.VarColor);
					WriteInk(writer, color._ink, names);

					break;

				case PendingTransition transition:
					writer.Write((byte)ReferenceKind.PendingTransition);
					writer.Write(transition.Text);

					break;

				case PendingTransform transform:
					writer.Write((byte)ReferenceKind.PendingTransform);
					writer.Write(transform.Text);

					break;

				default:
					// A value kind the format does not know would load as nothing at all — a declaration
					// that compiled cleanly and then silently never applied. Refusing the sheet names it.
					throw new NotSupportedException(
						$"A stylesheet value of type {reference.GetType().Name} cannot be compiled; "
						+ $"{nameof(StyleSheetSerializer)} needs to learn it.");
			}
		}

		private static void WriteInk(BinaryWriter writer, in VarColor ink, NameTable names)
		{
			writer.Write(ink.IsVar);

			if (ink.IsVar)
			{
				writer.Write(names.Index(ink._varId));
				writer.Write(ink._alpha);
			}
			else
			{
				WriteColor(writer, ink._value);
			}
		}

		private static void WriteColor(BinaryWriter writer, Color color)
		{
			writer.Write(color.r);
			writer.Write(color.g);
			writer.Write(color.b);
			writer.Write(color.a);
		}

		private static void WriteLength(BinaryWriter writer, StyleLength length)
		{
			writer.Write(length.Value);
			writer.Write((byte)length.Unit);
		}

		/// <summary>Assigns each distinct name a table index, in first-use order.</summary>
		private sealed class NameTable
		{
			internal readonly List<string> Strings = new();
			private readonly Dictionary<int, int> _indices = new();

			internal int Index(int nameId)
			{
				if (_indices.TryGetValue(nameId, out var index))
					return index;

				index = Strings.Count;
				Strings.Add(ClassTable.NameOf(nameId));
				_indices[nameId] = index;

				return index;
			}
		}

		#endregion

		#region Reading

		/// <exception cref="InvalidDataException">The bytes are not a compiled sheet of this format.</exception>
		internal static StyleSheet Read(byte[] data, string name)
		{
			using var reader = new BinaryReader(new MemoryStream(data, writable: false), Encoding.UTF8);

			if (data.Length < 8 || reader.ReadUInt32() != Magic)
				throw new InvalidDataException($"{name} is not a compiled stylesheet.");

			var version = reader.ReadInt32();

			if (version != FormatVersion)
			{
				throw new InvalidDataException(
					$"{name} was compiled by a different version of ReactiveUI (format {version}, expected "
					+ $"{FormatVersion}). Reimport it.");
			}

			var strings = new string[reader.ReadInt32()];

			for (var i = 0; i < strings.Length; i++)
				strings[i] = reader.ReadString();

			var ids = new int[strings.Length];

			for (var i = 0; i < strings.Length; i++)
				ids[i] = ClassTable.Intern(strings[i]);

			var states = new StateMap(reader);

			var simples = new SimpleSelector[reader.ReadInt32()];

			for (var i = 0; i < simples.Length; i++)
			{
				var kind = (SelectorKind)reader.ReadByte();
				var value = reader.ReadInt32();

				simples[i] = new SimpleSelector(kind, kind switch
				{
					SelectorKind.Class or SelectorKind.Type => ids[value],
					SelectorKind.PseudoClass => states.Bit(value),
					_ => value,
				});
			}

			var compounds = new CompoundSelector[reader.ReadInt32()];

			for (var i = 0; i < compounds.Length; i++)
			{
				compounds[i] = new CompoundSelector(
					reader.ReadInt32(), reader.ReadInt32(), (Combinator)reader.ReadByte(), states.Mask(reader.ReadUInt64()));
			}

			var selectors = new SelectorRecord[reader.ReadInt32()];

			for (var i = 0; i < selectors.Length; i++)
			{
				selectors[i] = new SelectorRecord(
					reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(),
					states.Mask(reader.ReadUInt64()), states.Mask(reader.ReadUInt64()));
			}

			var rules = new RuleRecord[reader.ReadInt32()];

			for (var i = 0; i < rules.Length; i++)
			{
				rules[i] = new RuleRecord(
					reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(),
					reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
			}

			var declarations = new Declaration[reader.ReadInt32()];

			for (var i = 0; i < declarations.Length; i++)
			{
				var property = (PropId)reader.ReadInt32();
				var custom = reader.ReadInt32();
				var value = ReadValue(reader, property, ids);

				declarations[i] = custom >= 0 ? new Declaration(ids[custom], value) : new Declaration(property, value);
			}

			var keyframes = new KeyframesClip[reader.ReadInt32()];

			for (var i = 0; i < keyframes.Length; i++)
			{
				var nameId = ids[reader.ReadInt32()];
				var tracks = new KeyframeTrack[reader.ReadInt32()];

				for (var t = 0; t < tracks.Length; t++)
				{
					var channel = (MotionChannelId)reader.ReadByte();
					var stops = new KeyframeStop[reader.ReadInt32()];

					for (var s = 0; s < stops.Length; s++)
					{
						var offset = reader.ReadSingle();
						var value = ReadValue(reader, PropId.None, ids);
						var easing = (Easing)reader.ReadInt32();

						stops[s] = new KeyframeStop(offset, value, easing, reader.ReadBoolean());
					}

					tracks[t] = new KeyframeTrack(channel, stops);
				}

				keyframes[i] = new KeyframesClip(nameId, tracks);
			}

			var queries = new MediaQuery[reader.ReadInt32()];

			for (var i = 0; i < queries.Length; i++)
			{
				queries[i] = new MediaQuery(
					reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadBoolean());
			}

			var clauses = new MediaClause[reader.ReadInt32()];

			for (var i = 0; i < clauses.Length; i++)
			{
				clauses[i] = new MediaClause(
					reader.ReadInt32(), reader.ReadInt32(), reader.ReadBoolean(), reader.ReadBoolean());
			}

			var features = new MediaFeatureTest[reader.ReadInt32()];

			for (var i = 0; i < features.Length; i++)
			{
				var kind = (MediaFeatureKind)reader.ReadByte();
				var comparison = (MediaComparison)reader.ReadByte();
				var length = ReadLength(reader);
				var number = reader.ReadSingle();
				var keyword = reader.ReadInt32();

				features[i] = kind switch
				{
					MediaFeatureKind.Width or MediaFeatureKind.Height => MediaFeatureTest.OfLength(kind, comparison, length),
					MediaFeatureKind.AspectRatio => MediaFeatureTest.OfNumber(kind, comparison, number),
					_ => MediaFeatureTest.OfKeyword(kind, keyword),
				};
			}

			var scopes = new ScopeRecord[reader.ReadInt32()];

			for (var i = 0; i < scopes.Length; i++)
			{
				scopes[i] = new ScopeRecord(
					reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
			}

			var layers = new string[reader.ReadInt32()];

			for (var i = 0; i < layers.Length; i++)
				layers[i] = reader.ReadString();

			var faces = new FontFace[reader.ReadInt32()];

			for (var i = 0; i < faces.Length; i++)
				faces[i] = new FontFace(reader.ReadString(), reader.ReadString(), reader.ReadInt32());

			return new StyleSheet(
				name, simples, compounds, selectors, rules, declarations, keyframes,
				queries, clauses, features, scopes, layers, faces);
		}

		private static StyleValue ReadValue(BinaryReader reader, PropId property, int[] ids)
		{
			var a = reader.ReadSingle();
			var b = reader.ReadSingle();
			var c = reader.ReadSingle();
			var d = reader.ReadSingle();
			var tag = reader.ReadInt32();
			var kind = (StyleValueKind)(tag & 0xFF);
			var reference = ReadReference(reader, ids);

			switch (kind)
			{
				case StyleValueKind.None:
					return default;

				case StyleValueKind.Length:
					return StyleValue.OfLength(new StyleLength(a, (LengthUnit)((tag >> 8) & 0xFF)));

				case StyleValueKind.Color:
					return StyleValue.OfColor(new Color(a, b, c, d));

				case StyleValueKind.Number:
					return StyleValue.OfNumber(a);

				case StyleValueKind.Keyword:
				{
					var keyword = tag >> 8;

					return StyleValue.OfKeyword(property == PropId.AnimationName && keyword != 0 ? ids[keyword] : keyword);
				}

				case StyleValueKind.Reference:
					return StyleValue.OfReference(reference);

				case StyleValueKind.VarReference:
					return StyleValue.OfVar(ids[tag >> 8]);

				default:
					throw new InvalidDataException($"Unknown style value kind {kind}.");
			}
		}

		private static object? ReadReference(BinaryReader reader, int[] ids)
		{
			switch ((ReferenceKind)reader.ReadByte())
			{
				case ReferenceKind.None:
					return null;

				case ReferenceKind.String:
					return reader.ReadString();

				case ReferenceKind.Calc:
				{
					var output = (CalcOutput)reader.ReadByte();
					var ops = new CalcOp[reader.ReadInt32()];

					for (var i = 0; i < ops.Length; i++)
					{
						var kind = (CalcOpKind)reader.ReadByte();
						var unit = (CalcUnit)reader.ReadByte();
						var value = reader.ReadSingle();
						var varIndex = reader.ReadInt32();

						ops[i] = kind switch
						{
							CalcOpKind.Literal => CalcOp.Literal(value, unit),
							CalcOpKind.Var => CalcOp.Var(ids[varIndex]),
							_ => CalcOp.Operator(kind),
						};
					}

					return new CalcExpr(ops, output);
				}

				case ReferenceKind.ShadowList:
				{
					var shadows = new Shadow[reader.ReadInt32()];

					for (var i = 0; i < shadows.Length; i++)
					{
						shadows[i] = new Shadow(
							ReadLength(reader), ReadLength(reader), ReadLength(reader), ReadLength(reader), ReadInk(reader, ids));
					}

					return new ShadowList(shadows);
				}

				case ReferenceKind.Checker:
					return new Checker(ReadLength(reader), ReadLength(reader), ReadInk(reader, ids), reader.ReadBoolean());

				case ReferenceKind.Gradient:
				{
					var kind = (GradientKind)reader.ReadByte();
					var angle = reader.ReadSingle();
					var stops = new GradientStop[reader.ReadInt32()];

					for (var i = 0; i < stops.Length; i++)
					{
						var position = reader.ReadSingle();

						stops[i] = new GradientStop(ReadInk(reader, ids), position);
					}

					return new Gradient(kind, angle, stops);
				}

				case ReferenceKind.VarColor:
					return new VarColorValue(ReadInk(reader, ids));

				case ReferenceKind.PendingTransition:
					return new PendingTransition(reader.ReadString());

				case ReferenceKind.PendingTransform:
					return new PendingTransform(reader.ReadString());

				default:
					throw new InvalidDataException("Unknown style value reference.");
			}
		}

		/// <summary>
		/// Maps the state bits a sheet was compiled against onto the ones this process handed out.
		/// </summary>
		private readonly struct StateMap
		{
			private readonly int[]? _bits;

			internal StateMap(BinaryReader reader)
			{
				var count = reader.ReadInt32();
				var bits = new int[count];
				var identity = true;

				for (var i = 0; i < count; i++)
				{
					bits[i] = UiStates.Register(reader.ReadString())._index;
					identity &= bits[i] == i;
				}

				// The usual case — the built-ins, registered in the same order everywhere.
				_bits = identity ? null : bits;
			}

			internal int Bit(int compiled) => _bits is null ? compiled : _bits[compiled];

			internal ulong Mask(ulong compiled)
			{
				if (_bits is null || compiled == 0UL)
					return compiled;

				var mask = 0UL;

				for (var i = 0; i < _bits.Length; i++)
				{
					if ((compiled & (1UL << i)) != 0UL)
						mask |= 1UL << _bits[i];
				}

				return mask;
			}
		}

		private static VarColor ReadInk(BinaryReader reader, int[] ids)
		{
			if (!reader.ReadBoolean())
				return new VarColor(ReadColor(reader));

			var id = ids[reader.ReadInt32()];

			return new VarColor(id, reader.ReadSingle());
		}

		private static Color ReadColor(BinaryReader reader)
		{
			return new Color(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
		}

		private static StyleLength ReadLength(BinaryReader reader)
		{
			return new StyleLength(reader.ReadSingle(), (LengthUnit)reader.ReadByte());
		}

		#endregion
	}
}
