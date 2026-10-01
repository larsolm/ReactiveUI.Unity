using System;
using UnityEngine;

namespace ReactiveUI
{
	internal struct InlineEntry
	{
		public PropId Id;
		public StyleValue Value;
	}

	internal struct InlineVar
	{
		public int Id;
		public StyleValue Value;
	}

	/// <summary>
	/// Per-instance style set from C#, for values a stylesheet cannot know: a tile's board position,
	/// a bar's measured width, a rarity colour handed down from game data. Applied on top of the
	/// cascaded result at the highest precedence, and deliberately kept out of the computed-style
	/// cache key so a per-instance value can never mint a shared rule.
	/// </summary>
	/// <remarks>
	/// Four properties inline and a spill beyond that, so every site in the game sets its values
	/// without allocating. Reached through a <c>ref</c>-returning <c>Style</c> property on the
	/// element — see <see cref="InlineArena"/> for why that has to be a ref.
	/// </remarks>
	public struct InlineStyle
	{
		private const int InlineCapacity = 4;

		private InlineEntry _a;
		private InlineEntry _b;
		private InlineEntry _c;
		private InlineEntry _d;
		private InlineEntry[]? _spill;

		private InlineVar[]? _vars;

		public int Count { get; private set; }

		public int VarCount { get; private set; }

		public StyleLength this[LengthProp prop]
		{
			set => Set(prop._id, StyleValue.OfLength(value));
		}

		public Color this[ColorProp prop]
		{
			set => Set(prop._id, StyleValue.OfColor(value));
		}

		public float this[FloatProp prop]
		{
			set => Set(prop._id, StyleValue.OfNumber(value));
		}

		/// <summary>
		/// Sets a custom property, e.g. <c>Style["--accent"] = rarity.Color</c>.
		/// </summary>
		public StyleValue this[string customProperty]
		{
			set
			{
				var id = ClassTable.Intern(customProperty);

				for (var i = 0; i < VarCount; i++)
				{
					if (_vars![i].Id != id)
						continue;

					_vars[i].Value = value;

					return;
				}

				if (_vars is null)
					_vars = new InlineVar[2];
				else if (VarCount == _vars.Length)
					Array.Resize(ref _vars, VarCount * 2);

				_vars[VarCount] = new InlineVar { Id = id, Value = value };
				VarCount++;
			}
		}

		/// <summary>
		/// The escape hatch for properties with no typed handle.
		/// </summary>
		public StyleValue this[PropId prop]
		{
			set => Set(prop, value);
		}

		/// <summary>
		/// Sets all four corner radii, since a uniform radius is one value in CSS but four properties
		/// in the model.
		/// </summary>
		public void SetRadius(StyleLength radius)
		{
			var value = StyleValue.OfLength(radius);

			Set(PropId.BorderTopLeftRadius, value);
			Set(PropId.BorderTopRightRadius, value);
			Set(PropId.BorderBottomRightRadius, value);
			Set(PropId.BorderBottomLeftRadius, value);
		}

		/// <summary>
		/// Sets a uniform border, the twelve properties CSS writes as one.
		/// </summary>
		public void SetBorder(float width, Color color, BorderStyle style = BorderStyle.Solid)
		{
			var w = StyleValue.OfNumber(width);
			var c = StyleValue.OfColor(color);
			var s = StyleValue.OfKeyword((int)style);

			Set(PropId.BorderTopWidth, w);
			Set(PropId.BorderRightWidth, w);
			Set(PropId.BorderBottomWidth, w);
			Set(PropId.BorderLeftWidth, w);
			Set(PropId.BorderTopColor, c);
			Set(PropId.BorderRightColor, c);
			Set(PropId.BorderBottomColor, c);
			Set(PropId.BorderLeftColor, c);
			Set(PropId.BorderTopStyle, s);
			Set(PropId.BorderRightStyle, s);
			Set(PropId.BorderBottomStyle, s);
			Set(PropId.BorderLeftStyle, s);
		}

		/// <summary>
		/// Sets a single centred glow, the shape most per-instance shadows take.
		/// </summary>
		public void SetGlow(Color color, float blur)
		{
			Set(PropId.BoxShadow, StyleValue.OfReference(new ShadowList(new Shadow(0f, 0f, blur, 0f, color))));
		}

		internal readonly InlineEntry EntryAt(int index) => index switch
		{
			0 => _a,
			1 => _b,
			2 => _c,
			3 => _d,
			_ => _spill![index - InlineCapacity],
		};

		internal readonly InlineVar VarAt(int index) => _vars![index];

		private void Set(PropId id, StyleValue value)
		{
			for (var i = 0; i < Count; i++)
			{
				if (EntryAt(i).Id != id)
					continue;

				Write(i, id, value);

				return;
			}

			Write(Count, id, value);
			Count++;
		}

		private void Write(int index, PropId id, StyleValue value)
		{
			var entry = new InlineEntry { Id = id, Value = value };

			switch (index)
			{
				case 0:
					_a = entry;
					break;
				case 1:
					_b = entry;
					break;
				case 2:
					_c = entry;
					break;
				case 3:
					_d = entry;
					break;
				default:
					var spilled = index - InlineCapacity;

					if (_spill is null)
						_spill = new InlineEntry[4];
					else if (spilled == _spill.Length)
						Array.Resize(ref _spill, spilled * 2);

					_spill[spilled] = entry;

					break;
			}
		}
	}
}
