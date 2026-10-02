using UnityEngine;

namespace ReactiveUI
{
	/// <summary>
	/// A handle to a mounted element for reading its size and applying transforms imperatively.
	/// </summary>
	public sealed class ElementRef
	{
		internal HostInstance? _host;

		/// <summary>
		/// Whether the referenced node is currently mounted.
		/// </summary>
		public bool IsMounted => _host is { _unmounted: false };

		/// <summary>
		/// The node's laid-out size, or zero before layout.
		/// </summary>
		public Vector2 Size => _host is null ? Vector2.zero : _host._rectTransform.sizeDelta;

		/// <summary>
		/// Offsets the node from its laid-out position.
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
		/// Scales the node, multiplied with its styled scale.
		/// </summary>
		public void SetScale(float scale)
		{
			if (_host is null)
				return;

			_host._motionScale = scale;
			_host.RefreshTransform();
		}

		/// <summary>
		/// Rotates the node, added to its styled rotation.
		/// </summary>
		public void SetRotation(float degrees)
		{
			if (_host is null)
				return;

			_host._motionRotation = degrees;
			_host.RefreshTransform();
		}

		/// <summary>
		/// Resets the translate, scale, and rotation set through this handle.
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
