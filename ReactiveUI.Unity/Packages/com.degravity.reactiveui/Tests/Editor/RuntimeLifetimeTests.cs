using NUnit.Framework;
using UnityEngine;

namespace ReactiveUI.Tests
{
	public sealed class RuntimeLifetimeTests
	{
		private GameObject _container;

		[TearDown]
		public void TearDown()
		{
			if (_container != null)
				Object.DestroyImmediate(_container);
		}

		private UiRuntime Render()
		{
			_container = new GameObject("Container", typeof(RectTransform));

			var rect = (RectTransform)_container.transform;
			rect.sizeDelta = new Vector2(400f, 300f);

			var runtime = new UiRuntime(rect, 32f, null, new StyleSheetLibrary());

			runtime.SetRoot(() => new View(ClassName.Intern("lt-row"))
			{
				new Text(ClassName.Intern("lt-label"), new TextProps("hello")),
			});
			runtime.Update();

			return runtime;
		}

		[Test]
		public void Pause_KeepsTheTree()
		{
			var runtime = Render();
			var transform = _container.transform;
			var count = transform.childCount;
			var first = transform.GetChild(0).gameObject;

			runtime.Pause();
			runtime.Update();

			Assert.AreEqual(count, transform.childCount);
			Assert.AreSame(first, transform.GetChild(0).gameObject);

			runtime.Dispose();
		}

		[Test]
		public void Dispose_AfterTheHierarchyIsDestroyed()
		{
			var runtime = Render();

			Object.DestroyImmediate(_container);

			Assert.DoesNotThrow(runtime.Dispose);
		}
	}
}
