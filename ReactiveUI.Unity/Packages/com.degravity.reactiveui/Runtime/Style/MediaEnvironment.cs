using System;
using UnityEngine;

namespace ReactiveUI
{
	/// <summary>
	/// The space a UI is laid out in.
	/// </summary>
	public readonly struct Viewport : IEquatable<Viewport>
	{
		public readonly float Width;
		public readonly float Height;

		public Viewport(float width, float height)
		{
			Width = width;
			Height = height;
		}

		public Viewport(Vector2 size) : this(size.x, size.y)
		{
		}

		public bool IsPortrait => Height >= Width;

		public bool IsLandscape => Width > Height;

		/// <summary>Width over height, or zero when the rect has no area to divide by.</summary>
		public float AspectRatio => Height > 0f ? Width / Height : 0f;

		public bool Equals(Viewport other)
		{
			return Width.Equals(other.Width) && Height.Equals(other.Height);
		}

		public override bool Equals(object? obj)
		{
			return obj is Viewport other && Equals(other);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(Width, Height);
		}

		public override string ToString()
		{
			return $"{Width}x{Height}";
		}
	}

	/// <summary>
	/// Everything a media query is asked about.
	/// </summary>
	/// <remarks>
	/// <c>width</c> and <c>height</c> measure the container rect rather than the screen, because that is
	/// the space every other length in the sheet already resolves in — a UI rendered into a half-screen
	/// panel should break at the panel's width, not the monitor's. The rem size rides along for the same
	/// reason a compiled breakpoint keeps its unit: a <c>rem</c> breakpoint has to move when the UI is
	/// rescaled.
	/// </remarks>
	public readonly struct MediaEnvironment : IEquatable<MediaEnvironment>
	{
		public readonly Viewport Viewport;
		public readonly float RemSize;

		/// <summary>The device the player last used, for <c>(input-device: …)</c>.</summary>
		public readonly InputDeviceKind Device;

		/// <summary>The last gamepad's button family, for <c>(gamepad-layout: …)</c>.</summary>
		public readonly GamepadLayout Layout;

		public MediaEnvironment(
			Viewport viewport,
			float remSize,
			InputDeviceKind device = InputDeviceKind.Keyboard,
			GamepadLayout layout = GamepadLayout.Generic)
		{
			Viewport = viewport;
			RemSize = remSize;
			Device = device;
			Layout = layout;
		}

		public bool Equals(MediaEnvironment other)
		{
			return Viewport.Equals(other.Viewport)
				&& RemSize.Equals(other.RemSize)
				&& Device == other.Device
				&& Layout == other.Layout;
		}

		public override bool Equals(object? obj)
		{
			return obj is MediaEnvironment other && Equals(other);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(Viewport, RemSize, Device, Layout);
		}
	}
}
