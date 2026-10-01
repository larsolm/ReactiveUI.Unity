using Microsoft.CodeAnalysis;

namespace ReactiveUI.Generators
{
	internal static class Diagnostics
	{
		private const string Category = "ReactiveUI";

		/// <summary>
		/// An error rather than a warning on purpose: without its generated constructor, <c>new Foo()</c>
		/// binds to the implicit one and yields a handle to no element, so the component and everything
		/// under it vanish at runtime with nothing logged.
		/// </summary>
		public static readonly DiagnosticDescriptor ComponentNotPartial = new(
			id: "RUI0001",
			title: "Component must be partial",
			messageFormat: "'{0}' implements IComponent, so ReactiveUI generates its constructors and handle; declare {1} partial",
			category: Category,
			defaultSeverity: DiagnosticSeverity.Error,
			isEnabledByDefault: true);

		public static readonly DiagnosticDescriptor StylesNotPartial = new(
			id: "RUI0002",
			title: "Stylesheet companion must be partial",
			messageFormat: "'{0}' has a colocated stylesheet ({1}), so ReactiveUI generates its Styles table; declare {2} partial",
			category: Category,
			defaultSeverity: DiagnosticSeverity.Warning,
			isEnabledByDefault: true);

		public static readonly DiagnosticDescriptor ClassNameCollision = new(
			id: "RUI0003",
			title: "Two CSS classes map to the same member",
			messageFormat: "`.{0}` and `.{1}` both map to `{2}` ({3}); keeping `.{1}`. Rename one of them to reach the other by symbol.",
			category: Category,
			defaultSeverity: DiagnosticSeverity.Warning,
			isEnabledByDefault: true);

		public static readonly DiagnosticDescriptor GenericComponent = new(
			id: "RUI0004",
			title: "Generic components are not supported",
			messageFormat: "'{0}' is generic; ReactiveUI cannot generate a component's constructors for an open generic type",
			category: Category,
			defaultSeverity: DiagnosticSeverity.Error,
			isEnabledByDefault: true);

		public static readonly DiagnosticDescriptor CompanionTypeMissing = new(
			id: "RUI0005",
			title: "Stylesheet companion declares no matching type",
			messageFormat: "{0} sits beside {1}, which declares no type named '{2}'; its classes get no table. Rename the type or the sheet to match.",
			category: Category,
			defaultSeverity: DiagnosticSeverity.Warning,
			isEnabledByDefault: true);
	}
}
