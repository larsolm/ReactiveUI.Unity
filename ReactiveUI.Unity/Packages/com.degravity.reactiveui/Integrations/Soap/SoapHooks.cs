using System;
using System.Collections.Generic;
using Obvious.Soap;

namespace ReactiveUI.Soap
{
	/// <summary>
	/// Hooks that read Soap variables, collections, and events.
	/// </summary>
	/// <remarks>
	/// Available when the Soap package is installed. Intended for use with <c>using static ReactiveUI.Soap.SoapHooks;</c>.
	/// </remarks>
	public static class SoapHooks
	{
		/// <summary>
		/// Returns the value of a Soap variable and re-renders the component when it changes.
		/// </summary>
		public static T UseScriptable<T>(ScriptableVariable<T> variable)
		{
			var hook = Ui.Current().GetOrCreate(0, static (store, _) => new ScriptableHook<T>(store));

			hook.Bind(variable);

			return variable.Value;
		}

		/// <summary>
		/// Returns a Soap list and re-renders the component when items are added, removed, or cleared.
		/// </summary>
		public static IList<T> UseScriptable<T>(ScriptableList<T> list)
		{
			var hook = Ui.Current().GetOrCreate(0, static (store, _) => new ScriptableCollectionHook(store));

			hook.Bind(list);

			return list;
		}

		/// <summary>
		/// Returns a Soap dictionary and re-renders the component when entries are added, removed, or cleared.
		/// </summary>
		public static IDictionary<TKey, TValue> UseScriptable<TKey, TValue>(
			ScriptableDictionary<TKey, TValue> dictionary)
		{
			var hook = Ui.Current().GetOrCreate(0, static (store, _) => new ScriptableCollectionHook(store));

			hook.Bind(dictionary);

			return dictionary;
		}

		/// <summary>
		/// Invokes <paramref name="handler"/> whenever <paramref name="evt"/> is raised while this component is mounted.
		/// </summary>
		public static void UseScriptableEvent<TState>(ScriptableEventNoParam evt, TState state, Action<TState> handler)
		{
			var hook = Ui.Current().GetOrCreate(0, static (store, _) => new ScriptableEventHook<TState>(store));

			hook.Bind(evt, state, handler);
		}

		/// <summary>
		/// Invokes <paramref name="handler"/> with the raised value whenever <paramref name="evt"/> is raised while this component is mounted.
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
