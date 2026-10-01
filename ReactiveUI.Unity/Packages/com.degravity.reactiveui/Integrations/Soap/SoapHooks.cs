using System;
using System.Collections.Generic;
using Obvious.Soap;

namespace ReactiveUI.Soap
{
	/// <summary>
	/// Hooks that read Soap variables, collections and events.
	/// </summary>
	/// <remarks>
	/// A class of its own rather than more members on <see cref="Ui"/>: Soap is an optional
	/// dependency, so these live in an assembly that only compiles when <c>REACTIVEUI_SOAP</c> is
	/// defined, and a partial class cannot span two assemblies. <c>using static
	/// ReactiveUI.Soap.SoapHooks;</c> keeps call sites reading the same as the built-in hooks.
	/// </remarks>
	public static class SoapHooks
	{
		/// <summary>
		/// Reads a Soap variable and re-renders when it changes.
		/// </summary>
		public static T UseScriptable<T>(ScriptableVariable<T> variable)
		{
			var hook = Ui.Current().GetOrCreate(0, static (store, _) => new ScriptableHook<T>(store));

			hook.Bind(variable);

			return variable.Value;
		}

		/// <summary>
		/// Reads a Soap list and re-renders when items are added, removed or cleared.
		/// </summary>
		public static IList<T> UseScriptable<T>(ScriptableList<T> list)
		{
			var hook = Ui.Current().GetOrCreate(0, static (store, _) => new ScriptableCollectionHook(store));

			hook.Bind(list);

			return list;
		}

		/// <summary>
		/// Reads a Soap dictionary and re-renders when entries are added, removed or cleared.
		/// </summary>
		public static IDictionary<TKey, TValue> UseScriptable<TKey, TValue>(
			ScriptableDictionary<TKey, TValue> dictionary)
		{
			var hook = Ui.Current().GetOrCreate(0, static (store, _) => new ScriptableCollectionHook(store));

			hook.Bind(dictionary);

			return dictionary;
		}

		/// <summary>
		/// Runs <paramref name="handler"/> whenever a Soap event is raised, for as long as this
		/// component is mounted.
		/// </summary>
		public static void UseScriptableEvent<TState>(ScriptableEventNoParam evt, TState state, Action<TState> handler)
		{
			var hook = Ui.Current().GetOrCreate(0, static (store, _) => new ScriptableEventHook<TState>(store));

			hook.Bind(evt, state, handler);
		}

		/// <summary>
		/// Runs <paramref name="handler"/> with the raised value whenever a Soap event fires.
		/// </summary>
		public static void UseScriptableEvent<TState, T>(
			ScriptableEvent<T> evt,
			TState state,
			Action<TState, T> handler)
		{
			var hook = Ui.Current().GetOrCreate(0, static (store, _) => new ScriptableEventHook<TState, T>(store));

			hook.Bind(evt, state, handler);
		}
	}
}
