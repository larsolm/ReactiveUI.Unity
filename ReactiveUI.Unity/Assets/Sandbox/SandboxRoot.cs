using ReactiveUI;

namespace Sandbox
{
	public sealed class SandboxRoot : UiRoot
	{
		protected override Element CreateRoot() => new Counter(new CounterProps(Start: 0));
	}
}
