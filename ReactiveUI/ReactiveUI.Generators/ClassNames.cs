using System;
using System.Collections.Generic;
using System.Text;

namespace ReactiveUI.Generators
{
	/// <summary>
	/// Turns CSS class names into the C# members that stand for them.
	/// </summary>
	internal static class ClassNames
	{
		/// <summary>
		/// The nested table in a component, and the shared one in each assembly's root namespace.
		/// </summary>
		/// <remarks>
		/// The shared table is named <c>Ui</c> rather than <c>Styles</c> on purpose: a namespace-level
		/// <c>Styles</c> would be shadowed by each component's own nested <c>Styles</c> and fail to
		/// resolve in precisely the files that use it most.
		/// </remarks>
		public const string StylesClass = "Styles";
		public const string GlobalClass = "Ui";

		/// <summary>
		/// <c>btn__label</c> becomes <c>BtnLabel</c>: BEM's separators are all word boundaries.
		/// </summary>
		/// <remarks>
		/// No keyword escaping: <see cref="Pascal"/> always uppercases the first letter and every C#
		/// keyword is lowercase, so the result cannot be one. What the result <em>can</em> collide with
		/// is a member every type inherits — a class named <c>equals</c> would hide
		/// <c>object.Equals</c>, which <c>-warnaserror</c> turns into a build failure.
		/// </remarks>
		public static string Identifier(string css)
		{
			var name = Pascal(css);

			if (name.Length == 0)
				return "_";

			if (char.IsDigit(name[0]))
				name = "_" + name;

			return s_reserved.Contains(name) ? name + "_" : name;
		}

		public static string Pascal(string value)
		{
			var builder = new StringBuilder(value.Length);
			var upper = true;

			foreach (var c in value)
			{
				if (c == '-' || c == '_' || c == ' ')
				{
					upper = true;

					continue;
				}

				if (!char.IsLetterOrDigit(c))
					continue;

				builder.Append(upper ? char.ToUpperInvariant(c) : c);
				upper = false;
			}

			return builder.ToString();
		}

		private static readonly HashSet<string> s_reserved = new(StringComparer.Ordinal)
		{
			"Equals",
			"GetHashCode",
			"GetType",
			"ToString",
			"ReferenceEquals",
			"MemberwiseClone",
			"Finalize",
			StylesClass,
			GlobalClass,
		};
	}
}
