using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ReactiveUI.Editor;
using UnityEngine;

namespace ReactiveUI.Tests
{
	/// <summary>Structural pseudo-classes as the reconciler maintains them across renders.</summary>
	public sealed class StructuralPseudoClassTests
	{
		private readonly List<Object> _created = new();

		private GameObject _container;
		private UiRuntime _runtime;
		private int _rows;
		private string _label = "";

		[TearDown]
		public void TearDown()
		{
			_runtime?.Dispose();
			_runtime = null;

			if (_container != null)
				Object.DestroyImmediate(_container);

			foreach (var created in _created)
				Object.DestroyImmediate(created);

			_created.Clear();
		}

		private void Render(string css, int rows)
		{
			var result = CssCompiler.Compile(css, "Assets/Structural.css");
			Assert.IsNotNull(result.Sheet, string.Join("\n", result.Diagnostics));

			var asset = ScriptableObject.CreateInstance<CompiledStyleSheet>();
			result.WriteTo(asset, "Assets/Structural.css");
			_created.Add(asset);

			var library = new StyleSheetLibrary();
			library.Load(CssAssets.Order(new List<CompiledStyleSheet> { asset }, report: false));

			_container = new GameObject("Container", typeof(RectTransform));
			((RectTransform)_container.transform).sizeDelta = new Vector2(400f, 300f);

			_runtime = new UiRuntime((RectTransform)_container.transform, 32f, null, library);
			_rows = rows;
			_runtime.SetRoot(Build);
			_runtime.Update();
		}

		private void Rerender(int rows, string label = "")
		{
			_rows = rows;
			_label = label;
			_runtime.SetRoot(Build);
			_runtime.Update();
		}

		private Element Build()
		{
			var list = new View(ClassName.Intern("st-list"));

			for (var i = 0; i < _rows; i++)
			{
				list.Add(new View(ClassName.Intern("st-row"))
				{
					new Text(ClassName.Intern("st-label"), new TextProps(i == 0 ? _label : "x")),
				});
			}

			return list;
		}

		private GameObject List() =>
			_container.GetComponentsInChildren<RectTransform>(true).First(t => t.name == "st-list").gameObject;

		private GameObject[] Rows()
		{
			var list = List().transform;

			return Enumerable.Range(0, list.childCount)
				.Select(list.GetChild)
				.Where(t => t.gameObject.activeSelf && t.name == "st-row")
				.Select(t => t.gameObject)
				.ToArray();
		}

		private static GameObject Label(GameObject row) =>
			row.GetComponentsInChildren<RectTransform>(true).First(t => t.name == "st-label").gameObject;

		private static float Opacity(GameObject node) =>
			node.TryGetComponent<CanvasGroup>(out var group) ? group.alpha : 1f;

		[Test]
		public void LastChild_MovesWhenTheListGrows()
		{
			Render(".st-row:last-child { opacity: 0.5; }", rows: 2);

			var rows = Rows();
			Assert.AreEqual(1f, Opacity(rows[0]));
			Assert.AreEqual(0.5f, Opacity(rows[1]));

			Rerender(3);

			rows = Rows();
			Assert.AreEqual(3, rows.Length);
			Assert.AreEqual(1f, Opacity(rows[1]), "the old last row lost :last-child");
			Assert.AreEqual(0.5f, Opacity(rows[2]));
		}

		[Test]
		public void NthChild_StripesAndRestripes()
		{
			Render(".st-row:nth-child(odd) { opacity: 0.5; }", rows: 4);

			CollectionAssert.AreEqual(new[] { 0.5f, 1f, 0.5f, 1f }, Rows().Select(Opacity).ToArray());

			Rerender(5);

			CollectionAssert.AreEqual(new[] { 0.5f, 1f, 0.5f, 1f, 0.5f }, Rows().Select(Opacity).ToArray());
		}

		[Test]
		public void OnlyChild_FollowsTheCount()
		{
			Render(".st-row:only-child { opacity: 0.5; }", rows: 1);

			Assert.AreEqual(0.5f, Opacity(Rows()[0]));

			Rerender(2);

			Assert.AreEqual(1f, Opacity(Rows()[0]));
		}

		[Test]
		public void Empty_ReadsHostChildren()
		{
			Render(".st-list:empty { opacity: 0.4; }", rows: 0);

			Assert.AreEqual(0.4f, Opacity(List()));

			Rerender(2);

			Assert.AreEqual(1f, Opacity(List()));

			Rerender(0);

			Assert.AreEqual(0.4f, Opacity(List()));
		}

		[Test]
		public void Empty_ReadsTextContent()
		{
			Render(".st-label:empty { opacity: 0.25; }", rows: 2);

			var rows = Rows();
			Assert.AreEqual(0.25f, Opacity(Label(rows[0])), "\"\" is :empty");
			Assert.AreEqual(1f, Opacity(Label(rows[1])));

			Rerender(2, label: "hello");

			Assert.AreEqual(1f, Opacity(Label(Rows()[0])));
		}

		[Test]
		public void FirstChild_OnAnAncestor()
		{
			Render(".st-row:first-child .st-label { opacity: 0.3; }", rows: 3);

			var rows = Rows();
			Assert.AreEqual(0.3f, Opacity(Label(rows[0])));
			Assert.AreEqual(1f, Opacity(Label(rows[1])));
		}

		[Test]
		public void NewlyMountedRow_SnapsToItsRealPosition()
		{
			// Every row is matched before its parent places it, as if it were the only child. A
			// transition from that guess would start the later rows at 0.2.
			Render(".st-row:first-child { opacity: 0.2; transition: opacity 1s; }", rows: 3);

			var rows = Rows();
			Assert.AreEqual(0.2f, Opacity(rows[0]));
			Assert.AreEqual(1f, Opacity(rows[1]));
			Assert.AreEqual(1f, Opacity(rows[2]));
		}
	}
}
