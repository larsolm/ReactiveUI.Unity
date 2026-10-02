using System;

namespace ReactiveUI
{
	/// <summary>
	/// Base class for state owned outside of components, read through <see cref="Ui.UseStore{T}"/>.
	/// </summary>
	public abstract class Store
	{
		internal event Action? Changed;

		/// <summary>
		/// Re-renders every component that reads this store.
		/// </summary>
		protected void NotifyChanged()
		{
			Changed?.Invoke();
		}
	}
}
