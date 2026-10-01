using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Compilation;

namespace ReactiveUI.Editor
{
	/// <summary>
	/// Defines <c>REACTIVEUI_SOAP</c> while the project contains Soap, which is what lets the
	/// <c>ReactiveUI.Soap</c> integration compile.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Soap ships through the Asset Store into <c>Assets/</c> rather than as a package, so an asmdef
	/// <c>versionDefines</c> entry has nothing to key on, and Soap declares no define of its own.
	/// </para>
	/// <para>
	/// Checked again at the start of every compilation as well as after a reload. Deleting Soap leaves
	/// the define behind, the integration then fails to compile, and a failed compile skips the domain
	/// reload — so a check that only ran on load would never get the chance to take the define away.
	/// The handler registered by the previous domain is still live at that point and does.
	/// </para>
	/// </remarks>
	[InitializeOnLoad]
	internal static class SoapDefine
	{
		private const string Symbol = "REACTIVEUI_SOAP";
		private const string SoapAssembly = "Obvious.Soap";

		static SoapDefine()
		{
			CompilationPipeline.compilationStarted += _ => Sync();
			Sync();
		}

		private static void Sync()
		{
			var target = NamedBuildTarget.FromBuildTargetGroup(
				BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget));

			PlayerSettings.GetScriptingDefineSymbols(target, out var current);

			var symbols = new List<string>(current);
			var present = symbols.Contains(Symbol);
			var wanted = SoapInstalled();

			if (present == wanted)
				return;

			if (wanted)
				symbols.Add(Symbol);
			else
				symbols.RemoveAll(symbol => string.Equals(symbol, Symbol, StringComparison.Ordinal));

			PlayerSettings.SetScriptingDefineSymbols(target, symbols.ToArray());
		}

		private static bool SoapInstalled()
		{
			foreach (var assembly in CompilationPipeline.GetAssemblies())
			{
				if (string.Equals(assembly.name, SoapAssembly, StringComparison.Ordinal))
					return true;
			}

			return false;
		}
	}
}
