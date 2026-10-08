using UnityEngine;
using UIImage = UnityEngine.UI.Image;

namespace ReactiveUI
{
	/// <summary>
	/// A sprite, painted like any other box with the sprite drawn on top.
	/// </summary>
	/// <remarks>
	/// Derives from <see cref="VisualHost"/> for the same reason <see cref="TextHost"/> does — an
	/// image is a box too, and answers to <c>background-color</c>, <c>border</c> and the rest — and
	/// carries its <c>Image</c> on the child <see cref="VisualHost.BuildGraphicChild"/> makes, so
	/// the paint has a GameObject of its own to sit behind the sprite on. `padding` then insets the
	/// sprite inside the box rather than only growing it, which is what makes a framed icon one
	/// node instead of two.
	/// </remarks>
	internal sealed class ImageHost : VisualHost
	{
		internal override HostKind Kind => HostKind.Image;

		private UIImage _image = null!;
		private Sprite? _sprite;
		private ImageMode _mode;

		internal void Build()
		{
			_image = BuildGraphicChild("Sprite").gameObject.AddComponent<UIImage>();
			_image.raycastTarget = false;
			_image.gameObject.AddComponent<CanvasMaterialModifier>();
		}

		internal override void ApplyProps(int node)
		{
			var props = ElementPool.Props<ImageProps>(node);

			if (!ReferenceEquals(_sprite, props.Sprite))
			{
				_sprite = props.Sprite;
				_image.sprite = _sprite;
			}

			if (_mode == props.Mode)
				return;

			_mode = props.Mode;
			_image.type = _mode == ImageMode.Sliced ? UIImage.Type.Sliced : UIImage.Type.Simple;

			// Vector art keeps the mesh baked in at import instead of a stretched quad, which is
			// what lets a single SVG sprite stay crisp at any size.
			_image.useSpriteMesh = _mode == ImageMode.Svg;
		}

		protected override bool PaintsContent => true;

		protected override void PaintContentColor(Color color)
		{
			if (_image != null)
				_image.color = color;
		}

		internal override void ResetForPool()
		{
			base.ResetForPool();

			_sprite = null;
			_mode = ImageMode.Simple;

			if (_image == null)
				return;

			_image.sprite = null;
			_image.color = Color.white;
			_image.type = UIImage.Type.Simple;
			_image.useSpriteMesh = false;
		}
	}
}
