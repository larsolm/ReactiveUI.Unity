using ReactiveUI;
using static ReactiveUI.Ui;

namespace Sandbox
{
	public readonly record struct CounterProps(int Start = 0);

	/// <summary>
	/// Exercises both generators: <c>ComponentGenerator</c> supplies the constructors and handle, and
	/// <c>StyleClassGenerator</c> supplies <see cref="Styles"/> from <c>Counter.css</c> beside it.
	/// </summary>
	public readonly partial struct Counter : IComponent<CounterProps>
	{
		public Element Render(in CounterProps props)
		{
			var count = UseState(props.Start);
			var increment = UseCallback(count, static count => count.Set(count.Value + 1));

			return new View(Styles.Counter)
			{
				new Text(Styles.Label, new TextProps($"Clicked {count.Value} times")),
				new View(Ui.Row)
				{
					new Pressable(Styles.Button, new PressableProps(OnClick: increment))
					{
						new Text(Styles.Label, new TextProps("Click me")),
					},
				},
			};
		}
	}
}
