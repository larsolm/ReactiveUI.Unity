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
	/// Style properties set from code on a single element.
	/// </summary>
	/// <remarks>
	/// Inline values take precedence over stylesheet rules.
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

		internal int Count { get; private set; }

		internal int VarCount { get; private set; }

		/// <summary>
		/// Sets a length property.
		/// </summary>
		public StyleLength this[LengthProp prop]
		{
			set => Set(prop._id, StyleValue.OfLength(value));
		}

		/// <summary>
		/// Sets a color property.
		/// </summary>
		public Color this[ColorProp prop]
		{
			set
			{
				var color = StyleValue.OfColor(value);

				if (prop._allSides)
				{
					Set(PropId.BorderTopColor, color);
					Set(PropId.BorderRightColor, color);
					Set(PropId.BorderBottomColor, color);
					Set(PropId.BorderLeftColor, color);

					return;
				}

				Set(prop._id, color);
			}
		}

		/// <summary>
		/// Sets a numeric property.
		/// </summary>
		public float this[FloatProp prop]
		{
			set
			{
				var number = StyleValue.OfNumber(value);

				if (prop._bothAxes)
				{
					Set(PropId.ScaleX, number);
					Set(PropId.ScaleY, number);

					return;
				}

				Set(prop._id, number);
			}
		}

		/// <summary>
		/// Sets a CSS custom property, such as <c>Style["--accent"] = color</c>.
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

		internal StyleValue this[PropId prop]
		{
			set => Set(prop, value);
		}

		/// <summary>
		/// Sets all four corner radii.
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
		/// Sets the width, color, and style of all four borders.
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
		/// Sets a single centered box shadow with the given color and blur radius.
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
