using System;

namespace ReactiveUI
{
	/// <summary>
	/// State that belongs to a plain object rather than to a component — a router, a settings model,
	/// anything the tree reads that outlives whichever component happens to render it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The alternative to a hook. Hook state lives on a mounted instance, so an object that wants to
	/// own state otherwise has to borrow a setter from a component and take it in its constructor —
	/// and every component that must see the change has to be reached through that component's
	/// render. A store owns its state outright and says when it changed; the components that read it
	/// subscribe through <c>UseStore</c> and are marked dirty directly.
	/// </para>
	/// <para>
	/// Because of that a store keeps <em>one identity for its whole life</em>, which is what lets it
	/// be provided once and held by anything — including other long-lived objects. Contrast a value
	/// rebuilt to signal a change, which every holder then has to re-acquire.
	/// </para>
	/// <para>
	/// <see cref="Changed"/> is internal on purpose: subscribing by hand would leak, because nothing
	/// but a hook knows when the component goes away.
	/// </para>
	/// </remarks>
	public abstract class Store
	{
		internal event Action? Changed;

		/// <summary>
		/// Announces that something a reader can see has changed, re-rendering every component that
		/// read this store. Call it after the change, and only when there was one.
		/// </summary>
		protected void NotifyChanged()
		{
			Changed?.Invoke();
		}
	}
}
