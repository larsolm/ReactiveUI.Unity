using UnityEngine;

namespace ReactiveUI
{
	/// <summary>
	/// A handle to a mounted node, for driving it imperatively.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is the escape hatch from the declarative path, and it exists for one reason: values
	/// that change every frame. A falling tile, a dragged card, a counter ticking up — pushing
	/// those through render and the cascade would rebuild elements and re-resolve styles sixty
	/// times a second to move something a few pixels.
	/// </para>
	/// <para>
	/// Motion written here is an offset on top of whatever the stylesheet resolved, not a
	/// replacement for it, so a restyle cannot clobber a drag in progress and a drag cannot
	/// permanently displace a styled position.
	/// </para>
	/// </remarks>
	public sealed class ElementRef
	{
		internal HostInstance? _host;

		/// <summary>
		/// Whether the referenced node is currently mounted.
		/// </summary>
		public bool IsMounted => _host is { _unmounted: false };

		/// <summary>
		/// The node's laid-out size, once layout has run.
		/// </summary>
		public Vector2 Size => _host is null ? Vector2.zero : _host._rectTransform.sizeDelta;

		/// <summary>
		/// Offsets the node from where layout put it.
		/// </summary>
		public void SetTranslate(float x, float y)
		{
			if (_host is null)
				return;

			_host._motionX = x;
			_host._motionY = y;
			_host.RefreshTransform();
		}

		/// <summary>
		/// Scales the node, multiplying whatever the style already applied.
		/// </summary>
		public void SetScale(float scale)
		{
			if (_host is null)
				return;

			_host._motionScale = scale;
			_host.RefreshTransform();
		}

		/// <summary>
		/// Rotates the node, adding to whatever the style already applied.
		/// </summary>
		public void SetRotation(float degrees)
		{
			if (_host is null)
				return;

			_host._motionRotation = degrees;
			_host.RefreshTransform();
		}

		/// <summary>
		/// Returns the node to exactly where its style puts it.
		/// </summary>
		public void ClearMotion()
		{
			if (_host is null)
				return;

			_host._motionX = 0f;
			_host._motionY = 0f;
			_host._motionScale = 1f;
			_host._motionRotation = 0f;
			_host.RefreshTransform();
		}

		internal void Bind(HostInstance host)
		{
			_host = host;
		}

		internal void Unbind(HostInstance host)
		{
			if (ReferenceEquals(_host, host))
				_host = null;
		}
	}
}
