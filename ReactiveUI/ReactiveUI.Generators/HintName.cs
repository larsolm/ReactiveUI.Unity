using System.Text;

namespace ReactiveUI.Generators
{
	internal static class HintName
	{
		/// <summary>
		/// <c>Game.UI.Button</c> and <c>Styles</c> become <c>Game.UI.Button.Styles.g.cs</c>, with anything
		/// the compiler refuses in a hint name — a generic's angle brackets, a path's slashes — flattened
		/// to underscores.
		/// </summary>
		public static string For(string name, string kind)
		{
			var builder = new StringBuilder(name.Length + kind.Length + 6);

			foreach (var c in name)
				builder.Append(char.IsLetterOrDigit(c) || c == '.' || c == '_' ? c : '_');

			return builder.Append('.').Append(kind).Append(".g.cs").ToString();
		}
	}
}
