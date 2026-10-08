# ReactiveUI

A reactive UI framework for Unity uGUI. A component is one struct with one `Render` method.
Styling is authored in CSS, and edits apply to a running editor session without a script recompile.
Layout is Yoga flexbox.

```csharp
public readonly partial struct Counter : IComponent
{
    public Element Render()
    {
        var count = UseState(0);
        var bump = UseCallback(count, static c => c.Update(1, static (n, by) => n + by));

        return new Pressable(Styles.Counter, new() { OnClick = bump })
        {
            new Text(Styles.Label, new($"Clicked {count.Value} times")),
        };
    }
}
```

```css
.counter { padding: 12px 24px; border-radius: 16px; background-color: #3A2847; }
.counter:hover { background-color: #463156; }
.counter__label { font-family: "Body"; font-size: 24px; color: #FBF0D9; }
```

---

## Contents

1. [Installing](#installing)
2. [Setting up a scene](#setting-up-a-scene)
3. [Components](#components) — and [why a struct](#why-a-struct-and-what-is-generated-for-you)
4. [Children and call-site syntax](#children-and-call-site-syntax)
5. [Host elements](#host-elements)
6. [Hooks](#hooks)
7. [Classes and names](#classes-and-names)
8. [Stylesheets](#stylesheets)
9. [States and pseudo-classes](#states-and-pseudo-classes)
10. [Transitions, animations, enter and exit](#transitions-animations-enter-and-exit)
11. [Inline styles and custom properties](#inline-styles-and-custom-properties)
12. [Motion: values that change every frame](#motion-values-that-change-every-frame)
13. [Focus, navigation and hotkeys](#focus-navigation-and-hotkeys)
14. [Scroll and portals](#scroll-and-portals)
15. [Hot reload](#hot-reload)
16. [Builds](#builds)
17. [How it works](#how-it-works)
18. [Limitations](#limitations)
19. [Diagnostics](#diagnostics)

---

## Installing

In **Window ▸ Package Manager ▸ + ▸ Install package from git URL**:

```txt
https://github.com/larsolm/ReactiveUI.Unity.git?path=/ReactiveUI.Unity/Packages/com.degravity.reactiveui
```

Two dependencies come from outside Unity's registry, so add their scoped registries to
`Packages/manifest.json` first:

```json
"scopedRegistries": [
  { "name": "npm", "url": "https://registry.npmjs.org", "scopes": [ "com.kyrylokuzyk" ] },
  { "name": "OpenUPM", "url": "https://package.openupm.com", "scopes": [ "games.corundum.isexternalinit" ] }
]
```

Everything else — the Yoga layout engine and its native libraries, the CSS parser, and the code
generators — ships inside the package.

**Your UI code needs an assembly definition that references `ReactiveUI`.** The package is not
auto-referenced, and its code generators only run for assemblies that reference it. Put a `csc.rsp`
beside that `.asmdef` raising the language version, since components and their props rely on C# 10
(parameterless struct constructors and `record struct`):

```txt
-langversion:11
-nullable:enable
```

**Commit `Assets/Plugins/ReactiveUI/`.** The editor keeps two files there: the stylesheet manifest a
build loads, and the list of class names the compiler turns into constants (see
[Classes and names](#classes-and-names)). Both are regenerated automatically, but a fresh clone
compiles before any editor code has run, so they have to arrive with it.

**Soap.** The `UseScriptable` hooks live in an optional `ReactiveUI.Soap` assembly that compiles only
while the project contains Soap. The editor defines `REACTIVEUI_SOAP` for you when it finds Soap, and
removes it when Soap goes. Add `ReactiveUI.Soap` to your assembly definition's references and
`using static ReactiveUI.Soap.SoapHooks;` to the files that use them.

---

## Setting up a scene

Subclass `UiRoot`, return your root element, and put it on a `RectTransform` under a Canvas.

```csharp
public sealed class AppUi : UiRoot
{
    protected override Element CreateRoot() => new StartScreen();
}
```

In the inspector:

| Field | Meaning |
| ----- | ------- |
| **Container** | The `RectTransform` the tree renders into. Defaults to the object's own. |
| **Rem Size** | Pixels per `rem`, default 32. Change this to scale the whole UI. |

There is nothing to assign for styling: every `.css` in the project is picked up automatically, in the
editor and in a build alike. See [Builds](#builds).

`CreateRoot` is a factory called inside the render pass.

`UiRoot.Runtime` is the live `UiRuntime` for that tree, and the way to reach `Focus` and `Hotkeys`
from outside a component. It exists between `OnEnable` and `OnDisable` and is null otherwise.

### Gamma blending in a linear project

A linear project blends translucent UI in linear light, so a CSS value lifted from a web design comes
out brighter than the browser draws it — markedly so for low alphas over dark colours. Add
`GammaCanvas` to the root Canvas to blend that canvas in gamma space instead, as a browser does,
while the scene stays linear:

1. The canvas moves to Screen Space - Camera on `Camera.main` (followed across scene loads) and onto
   its **Layer**, `UI` by default. Leave `vertexColorAlwaysGammaSpace` to it; it turns it on.
2. On URP, add **ReactiveUI Gamma** (`ReactiveUIGammaFeature`, from the `ReactiveUI.Universal`
   integration) to the camera's renderer, and remove the same layer from the renderer's own layer mask
   so the canvas is not also drawn linearly.

The feature draws the canvas into an 8-bit target with materials that output sRGB, after
post-processing, then composites it over the frame in sRGB. Boxes, images and text are covered. A
material whose shader has no copy under `Hidden/ReactiveUI/Gamma/` (today `UI/Default` and TMP's
`Distance Field` and `Mobile/Distance Field`) keeps its linear output and so draws too dark on a gamma
canvas, as does a TMP fallback font, which renders on a sub-mesh of its own.

---

## Components

A component is a `readonly partial struct` implementing `IComponent<TProps>`, where `TProps` is a
`readonly record struct` holding its props.

```csharp
public readonly record struct CardProps
{
    public string Title { get; init; } = "";
    public bool Selected { get; init; }
    public Action? OnPick { get; init; }
    public ClassSet CardClass { get; init; }

    public CardProps() { }
}

public readonly partial struct Card : IComponent<CardProps>
{
    public Element Render(in CardProps props)
    {
        return new Pressable(
            props.CardClass | Styles.Card | (Styles.Selected & props.Selected),
            new() { OnClick = props.OnPick })
        {
            new Text(Styles.Title, new(props.Title)),
            Children,              // whatever the caller passed in
        };
    }
}
```

```csharp
new Card(props: new() { Title = "Spicy S Pack", Selected = true, OnPick = Buy })
```

A component with no props of its own implements `IComponent` instead, whose `Render` takes no
parameter. The file needs `using static ReactiveUI.Ui;`.

### Why a struct, and what is generated for you

**An element is not an object.** `Element` is a handle — an index into an arena that lives for one
render pass and is then dropped whole. Declaring an element therefore allocates *nothing*: no
element, no children list, no props box. That is the entire reason the authoring type is a struct
rather than a class.

It is also why a component cannot inherit any of its plumbing: a struct has no base class. Every
component needs the same five members, so **they are generated**. Declaring the type `partial` and
giving it `IComponent<T>` is the whole of the contract; a source generator in the package adds a
partial holding:

| Member | Why |
| ------ | --- |
| `Element Handle { get; }` | The handle itself. `IComponent<T>` requires it via `IElement`. |
| `Card()` | A struct's implicit parameterless constructor would otherwise win — see below. |
| `Card(CardProps? props = null)` | Writes the node and its props into the arena. |
| `implicit operator Element` | Lets the component stand anywhere an `Element` is wanted — a child, an `Element?` prop. |
| `IEnumerable.GetEnumerator` | C# requires `IEnumerable` of any collection-initializer target. Iterating throws. |

Generation happens inside the compiler, so there is nothing to run and no file on disk: the members
exist as soon as the declaration does, and your IDE shows the generated source under the assembly's
analyzers. A component that is not `partial` — or is nested in a type that is not — is compile error
**RUI0001**, pointing at the declaration.

**The parameterless constructor is generated rather than left to you because leaving it out is a
silent bug.** For a struct, `new Card()` binds to the *implicit* parameterless constructor, **not**
to the one whose arguments are all optional. Without an explicit one it zero-initialises into a
handle to no element at all, and the node plus every child added to it simply does not render, with
nothing logged.

**Props arrive as a parameter, not as a property.** `Render(in TProps props)` is handed the props the
instance committed, because the element that declared them is gone by the time a hook-driven
re-render runs — the arena has been reset. The committed copy lives on the instance, and dispatch is
a constrained generic call on a struct, so nothing is boxed on the way in.

**State lives in hooks.** Everything that must survive a render — hook state, the GameObject, the
resolved style, the committed props — belongs to the instance the reconciler keeps. Nothing survives
on an element.

### Styling is the host's, not the component's

Everything to do with styling belongs to [host elements](#host-elements), which are the nodes a
stylesheet can actually reach. **A component that wants to be styled from outside takes a `ClassSet`
as a prop, named for the node it lands on, and applies it itself** — `CardClass` above.

**Components are invisible to selectors.** A component owns no box, so it contributes nothing to the
cascade: no class, no type name. `.panel > .card` matches the `Pressable` that `Card` renders, and
keeps working however `Card` is built internally — but `Card { … }` as a selector does not exist, and
is rejected at parse time rather than matching nothing.

Every class a node carries is written on that node, by whoever built it. There is no merge happening
elsewhere and nothing to keep in sync.

### Memoisation

A component memoises on its props: the reconciler copies the declared props onto the instance, and
when they compare equal it keeps the previous render instead of recomputing the subtree. The compiler
writes that equality for the record struct.

**A component that was *given* children always re-renders when reached.** Whether those children
changed is a question about the *caller's* element rather than about this component's props, and
answering it would mean retaining the previous element tree — which is exactly what stops the arena
being reclaimable. Re-rendering costs no allocation now, the host props below are diffed against the
live nodes, and the recursion still stops at every childless component whose props match.

A propless component with no children memoises too — nothing about it can have changed. Such a
component can only vary with hooks and context, and both mark it dirty directly.

A component that reads mutable state directly in `Render` rather than through a hook will stop
tracking it. Read it through a hook — `UseState`, `UseContext`, `UseSubscription`, or with Soap,
`UseScriptable` — instead.

---

## Children and call-site syntax

One form: what the element *is* goes in the parentheses, what it *contains* goes in the braces.

```csharp
new Pressable(Styles.StartScreen, new(OnClick: Begin))
{
    new Text(Styles.Prompt, new("PRESS ANY KEY"))
}
```

`className` and `props` are the first two parameters, in the order they are nearly always written,
so those two read positionally and the one rarer parameter (`elementRef`) is named. A component takes
`props` alone. An element with no children is just the call:

```csharp
new View(Styles.Spacer)
new Scroll(props: new(ScrollAxis.Both))     // props without a class: name it
```

Useful details:

- `null` children are skipped, so `cond ? new Text(props: new("x")) : null` reads plainly.
- **`condition & element`** is the conditional child — it yields the element or nothing, in either
  operand order. It is declared on every element type and on `Element` itself, rather than once,
  because an operator is only looked up on its own operand types: one declared on `Element` alone
  could not be reached through a component's implicit conversion to it. The component generator
  emits the pair for each component it writes.
- It yields `Element?`, not an interface, so nothing boxes and the result drops straight into the
  `Add(Element?)` that already skips `null`. `Add` also takes an `Element?` *parent*, so a
  conditional element can still be filled in afterwards:

  ```csharp
  var overlay = busy & new View(Styles.Overlay);
  overlay.Add(new Spinner());          // nothing is added when busy is false
  ```

- **`element.When(condition)`** is the same thing as a trailing call, for a condition long enough
  that reading it before the element would bury the element.
- `&` binds *tighter* than `&&`, so `a && b & element` parses as `a && (b & element)` and fails to
  compile. Parenthesise the condition — `(a && b) & element` — or use `.When(a && b)`. Comparisons
  bind tighter still, so `count > 0 & element` needs nothing.
- **`Each(items, state, static (item, s) => …)`** projects a list into children without allocating.
  Write the selector `static` and reach everything through `state`; capturing a local puts back the
  closure it exists to avoid. A plain `foreach` adding to the parent is equally allocation-free.
- To add children after construction, call **`parent.Add(child)`** — not `parent.Children.Add(child)`.
  `Children` lives on `Element`, and C# has no extension properties.
- `Children` inside a `Render` is the children *this* component was given, and splices in rather
  than nesting.
- Elements are not enumerable; the `IEnumerable` implementation exists only so the collection
  initializer compiles, and iterating one throws.
- Inline styles are set after construction — `node.Style[Css.Left] = 12f` — see
  [Inline styles](#inline-styles-and-custom-properties).
- `props` is an `in` parameter, so a lambda cannot capture it (CS1628). Copy what you need to a local
  first, or reach it through hook state.

### Identity is position

A child is matched against whatever stood in its position last render, and its type has to agree —
that is the whole of it. There are no keys.

The consequence is the one rule to know before writing a list: **a list that can change length in
the middle must render fixed slots.** Skipping entries slides every later child onto its neighbour's
instance, which re-props it rather than unmounting the one that actually went away.

```csharp
// Wrong: the list shrinks in the middle, so every later cell shifts by one.
foreach (var cell in cells)
{
    if (cell.Occupied) continue;
    grid.Children.Add(new Plot(new(cell.Column, cell.Row)));
}

// Right: every position is always rendered, and decides for itself what it holds.
for (var row = 1; row <= Size; row++)
for (var column = 1; column <= Size; column++)
{
    grid.Children.Add(new Cell(new() { Column = column, Row = row })
    {
        Occupant(column, row) is { } tile ? new Tile(…) : new View(Styles.Plot),
    });
}
```

The second form is also the faster one. A cell that never moves never changes the inline style its
coordinates live in, so nothing below it restyles; only the slot that actually changed does any work,
and its child changes type, which remounts exactly that child.

Appending to or truncating the end of a list is fine — those positions are stable by definition — and
so is a stable-length list whose contents change. It is only mid-list insertion and removal that need
the fixed-slot treatment.

Note that `Children.Add(null)` is skipped rather than holding a slot, so `cond ? el : null` changes
its list's length. That is safe when the neighbours are a different type, since the type check
remounts rather than mis-matching, and it is worth a second thought when they are not.

---

## Host elements

The primitives that become GameObjects. Those with props take them the same way a component does —
`new Text(props: new("hi"))` and `new Scroll(Styles.Inventory, new(ScrollAxis.Both))`.

| Element | Props | Notes |
| ------- | ----- | ----- |
| `View` | — | A box. The default container. |
| `Text` | `Content` | Measured through TextMeshPro inside the layout pass. Paints like a `View` too — background, border, shadow — with the glyphs inside its padding. |
| `Image` | `Sprite`, `Mode` (`Simple`/`Sliced`/`Svg`) | Tinted by `color`. Paints like a `View` too, with the sprite inside its padding. Has no intrinsic size — give it `width`/`height` or an `aspect-ratio`. |
| `Pressable` | `OnClick`, `OnClickAt`, `OnPressDown`, `OnPressUp`, `OnHoverEnter`, `OnHoverExit`, `Disabled` | Focusable. |
| `Scroll` | `Axis` (`Vertical`/`Horizontal`/`Both`) | Clips and scrolls its children. |
| `Fragment` | — | Groups children without adding a level. |
| `Portal` | — | Renders children into the overlay above everything. |
| `ContextProvider<T>` | — | Makes one value visible to everything below it. See [Context](#context). |

`Fragment`, `Portal` and `ContextProvider<T>` own no box, so they take no class and no ref — there
would be no node to put one on. Group children with one and style the children themselves.

Each of these is a struct like any other element, and each declares an explicit parameterless
constructor for the reason given under [Components](#components): `new View()` without one is a
handle to nothing.

---

## Hooks

Statics on `Ui`, reached with `using static ReactiveUI.Ui;`, and callable only while a component is
rendering — calling one anywhere else throws. They are statics rather than inherited methods because
a component is a struct and so has no base class to inherit them from; the reconciler points them at
the component it is rendering, which also makes a hook valid inside a helper method that `Render`
calls. Call them unconditionally and in a stable order: a changed hook count or a
changed hook type at a slot throws `HookOrderException` rather than quietly reading the wrong state.
Two hooks of the *same* type swapping places is the one reordering nothing can catch, so it silently
swaps their values — the same caveat React has.

```csharp
State<int> count   = UseState(0);              // re-renders on change
Ref<float> elapsed = UseRef<float>();          // survives renders, never causes one
var services       = UseConstant(env, static e => new Services(e));  // built once, same one ever after
var layout         = UseMemo(env, static e => e.Build(), deps);      // recompute only when deps change
ElementRef handle  = UseElementRef();          // a handle to the mounted node

UseEffect(host, static h => { h.Attach(); return h.Detach; }, deps); // after layout, when deps change
UseEffectOnce(host, static h => { h.Attach(); return h.Detach; });   // once, on mount

Action onPick = UseCallback(id, static i => Buy(i));    // one identity for the component's life
Action onBuy  = UseCallback(props.OnBuy, props.Id);     // ... forwarding a payload to a parent

UseHotkey(Key.Escape, close, static c => c());  // bound while mounted, released on unmount
UseInputAction(ui.Cancel, close, static c => c()); // ... the same for an Input System action

UseFocusScope(dialog);                            // trap navigation inside a node while mounted
UseAutoFocus(firstButton);                        // focus a node once, on mount
FocusManager focus = UseFocus();                  // the runtime's focus manager, for handlers

var app        = UseContext<AppContext>();        // nearest provided value; throws if unprovided
var theme      = UseContextOrNull<Theme>();       // ... or null, when the context is optional
var router     = UseStore<Router>();              // ... and re-render whenever it changes
var modality   = UseInputModality();              // pointer or gamepad, re-renders when it changes

bool compact   = UseMedia("(max-width: 40rem)");  // re-renders when the answer flips, not on every pixel
Viewport view  = UseViewport();                   // the live size; re-renders on every change

// With Soap — ReactiveUI.Soap, and `using static ReactiveUI.Soap.SoapHooks;`
int seeds      = UseScriptable(run.Seeds);        // reads a Soap variable, re-renders on change
IList<Relic> r = UseScriptable(run.Relics);       // ... a Soap list
IDictionary<K,V> d = UseScriptable(run.Stock);    // ... and a Soap dictionary

UseScriptableEvent(app.LoadGameEvent, save,       // runs a handler when a Soap event is raised
    static s => s.Load());
UseScriptableEvent(map.SelectNodeEvent, map,      // ... including one that carries a value
    static (m, node) => m.Select(node));

UseSubscription(model,                            // re-render on any other source's event
    static (m, invalidate) => m.Changed += invalidate,
    static (m, invalidate) => m.Changed -= invalidate);
```

**Every hook that takes a callback takes the state it acts on beside it.** Write the callback
`static` and reach everything it needs through that state. This is the one rule the whole vocabulary
rests on: non-capturing is what gets the compiler to cache the delegate in `<>c`, and `static` is
only the guard that keeps it that way — capturing a local silently restores a per-render closure.
There are no deps on a callback: the state is refreshed every render, so nothing goes stale.

`UseRef` and `UseConstant` are the exception, because their factory runs on the first render only —
the state they are handed is that render's, so pass something that will still be true later. Where
they build from nothing, `UseRef<T>()` and `UseConstant<T>()` take no lambda at all and
default-construct a `T`.

`UseHotkey` takes a `UnityEngine.InputSystem.Key`; `UseInputAction` takes an `InputAction`, so a
binding follows the player's rebinds and gamepad buttons work too. Both are last-binding-wins, so a
modal binding Cancel shadows the screen beneath it and hands it back automatically when it unmounts.
An action is polled, not enabled: a disabled action — a map the game has switched off — fires
nothing.

That ordering is registration order, which is why the binding is made once and never renewed —
re-registering each render would move it back to the head of the queue, and a screen that re-rendered
would take `Escape` back from the modal above it. What gets registered is a stable listener, so the
state it dispatches to is still refreshed every render; a hotkey reading `Props` sees the current
ones, not the first render's.

**Deps are compared without boxing.** `UseMemo` and `UseEffect` constrain them to `IEquatable<TDeps>`,
which covers an `int`, an enum, a tuple of them (`(index, selected)`), a `string`, and any `record`.
A class that declares no equality of its own — a `ScriptableObject` asset, a definition — goes
through `Deps.Of`, a struct wrapper that compares it by identity and composes into a tuple like
anything else:

```csharp
UseEffect(relic, Watch, Deps.Of(relic));                 // one reference, compared by identity
UseMemo(relic, Build, (index, Deps.Of(relic)));          // mixed with value types
```

Because a reference can now be a dep, a **mutable** object used as one will compare equal to itself
after it mutates, and the memo will go stale. Prefer a record, a value, or `Deps.Of` on something
that is genuinely replaced rather than edited in place.

For an effect with no deps at all, use `UseEffectOnce`.

`State<T>` exposes `Value`, `Set` and `Update`, and converts implicitly to `T`, so a state can be
read straight into an expression. Prefer `Update` when the new value depends on the old, since
several updates can batch into one frame; it passes state the same way a hook does:

```csharp
count.Update(step, static (current, amount) => current + amount);
```

Setters are bound to the instance, not to the element that read them, so a handler captured in one
render keeps working after later renders have replaced that element.

### Context

A context is identified by its own type, so there is nothing to declare — no token, no static field.
Any class will do, reference types only. Wrap the subtree in a `ContextProvider<T>` and read it back
with `UseContext<T>()`:

```csharp
var app = UseConstant((Props.AppState, route.Set),
    static state => new AppContext(state.AppState, state.Set));

return new ContextProvider<AppContext>(app)
{
    new Screen(),
};
```

```csharp
var app = UseContext<AppContext>();     // non-nullable
app.Navigate(AppRoute.Settings);
```

The type is the key, which means one context per type. If you want two of something, give them two
types — it reads better than two tokens would have.

`UseContext<T>` throws `MissingContextException` when nothing above provides one, because that is a
wiring mistake rather than a state to branch on. Paying for it once, at the read, is what keeps the
return non-nullable and every call site free of `?.`. For a context that really is optional,
`UseContextOrNull<T>()` returns null instead.

**Give the value a stable identity.** `UseConstant` builds it on mount and hands back that same one
ever after; a value rebuilt each render would defeat the memoisation of the whole subtree and mark
every consumer dirty for nothing. Its factory runs on the first render only, so the state handed to
it is the first render's — pass something that does not go stale, which hook state and setters are
and props are not. When the provided value genuinely changes from render to render, pass it to
`new ContextProvider<T>(value)` directly.

A provider owns no render of its own: it is a group node, and its children are reconciled in place
like a fragment's. A changed value is noticed when the node is reconciled, and the consumers that a
memoised ancestor would have cut off are marked dirty directly — see [Memoisation](#memoisation).

`UseContext` takes a hook slot like every other hook, so it must be called unconditionally and in a
stable order too.

### Stores

A hook holds state for one component. A `Store` holds it for the app: a plain object that owns its
own state and says when it changed, so it never has to borrow a setter from whichever component
happened to mount it.

```csharp
public sealed class Router : Store
{
    public AppRoute Route { get; private set; }

    public Router(AppRoute start) => Route = start;

    public void Navigate(AppRoute route)
    {
        if (Route == route) return;     // no change, no notification

        Route = route;
        NotifyChanged();
    }
}
```

Build it once and provide it. Because it owns its state, it keeps **one identity for its whole
life** — so anything may hold on to one, including other long-lived objects:

```csharp
var router = UseConstant(Props.StartRoute, static route => new Router(route));
var app    = UseMemo((Props.AppState, router), static s => new AppContext(s.AppState, s.router), Props);
```

That is the difference from signalling a change by rebuilding the value: nothing has to re-acquire a
store, and the component that provides it needs no state of its own.

**Reading is two verbs, and the difference matters.** `UseStore<T>()` reads the nearest provided
store *and subscribes*, so the component re-renders whenever it changes. `UseContext<T>()` finds the
same object and does not listen — which is what a component that only *acts* on a store wants, since
a screen that navigates has no reason to re-render because someone else navigated. Use `UseStore`
when the render output depends on what the store currently says, `UseContext` when you only call
methods on it.

Since the context value never changes, none of a provider's own invalidation applies to a store:
every re-render a reader gets comes from its `UseStore` subscription, which reaches it directly
however many memoised components sit in between.

`NotifyChanged` is yours to call, and only when something actually changed — guard it the way
`State<T>.Set` guards itself. `Changed` is internal, so a store can only be observed through a hook;
subscribing by hand would leak, because nothing but a hook knows when the component goes away.

### Reading outside sources

Every reactive read is the same hook underneath — hold a source, re-attach when the reference
changes, detach at unmount — so the ones that ship are sugar over one mechanism, and anything else
plugs into that mechanism directly.

`UseScriptable` covers Soap's variables, lists and dictionaries; it and `UseScriptableEvent` live in
the optional `ReactiveUI.Soap` assembly (see [Installing](#installing)). One caveat comes from Soap rather
than from here: a list raises nothing for `list[i] = x`, so replacing an item **in place** will not
re-render. Adding, removing and clearing all do.

`UseScriptableEvent` is the counterpart for the things that are announced rather than stored. It is
the one binding hook that does not re-render on its own — an event carries no state for a render to
read — so it runs your handler and leaves what to do about it to you, usually setting some state:

```csharp
var toast = UseState<string?>(null);

UseScriptableEvent(run.RelicGained, toast, static (t, relic) => t.Set($"Gained {relic.Name}"));
```

For anything else — a plain C# event, an observable, someone else's model — `UseSubscription` takes
the attach and detach directly, so no new hook has to be written:

```csharp
UseSubscription(model,
    static (m, invalidate) => m.Changed += invalidate,
    static (m, invalidate) => m.Changed -= invalidate);
```

The action handed in is the listener; attach it, and detach *that same one*. Write both lambdas
`static`, as above — the compiler then caches them and the call allocates nothing per render. They
are captured on the first render and kept, so a non-static pair cannot desynchronise attach from
detach; it would only allocate. Passing a different source re-attaches, and passing null detaches.

Events that carry a value need their type arguments spelled out, since a bare lambda gives the
compiler nothing to infer them from:

```csharp
UseSubscription<Model, int>(model,
    static (m, invalidate) => m.ScoreChanged += invalidate,
    static (m, invalidate) => m.ScoreChanged -= invalidate);
```

---

## Classes and names

Classes are symbols, not strings. Every class selector in a stylesheet generates a `ClassName`
constant, so a name is spelled once — in the CSS — and referred to by symbol everywhere else. A typo
is a compile error rather than a rule that silently matches nothing.

A sheet colocated with a component generates into a partial of it, which is why `Button.css` gives
`Button.Styles.Btn` and a component reaches its own classes as just `Styles.Btn`. Everything else
generates into the shared `Ui` table:

```csharp
new Pressable(
    Styles.Btn |
    Ui.Row |
    (Styles.BtnSmall & (props.Size == ButtonSize.Small)) |
    (RailPanel.Styles.RailPanelTall & tall))   // another component's class
```

The component must be `partial` (warning **RUI0002** otherwise); generation is otherwise automatic and
needs no wiring. A sheet counts as a component's when a `.cs` of the same name beside it declares a
type of that name — the compiler decides this, so adding or removing that file reroutes the classes
on the next compile.

The constants are generated by the compiler, but the compiler cannot read `.css` — Unity only hands a
generator files named `*.additionalfile`. So the editor keeps
`Assets/Plugins/ReactiveUI/StyleClasses.ReactiveUI.Generators.additionalfile`, one entry per sheet
listing its classes, and rewrites it only when the set of class names changes. Retuning a value
therefore still hot-reloads without a recompile — see [Stylesheets](#stylesheets). Commit the file
along with the rest of that folder.

`ClassName` is one interned int; `ClassSet` holds eight inline before spilling to an array. Equality is
a few int compares and the common case allocates nothing.

**A class lives on a host element and nowhere else.** A class names a CSS rule, and a rule can only
ever apply to a node that reaches the layout — so a component has no `Class` of its own, and nothing
is forwarded down from one. A component that wants to be styled by its caller declares a `ClassSet`
among its props, named for the node it lands on (`CardClass`, `TextClass`, `PanelClass`), and merges
it where that node is built. That is one line in the component and it says exactly which host is
being styled, which the old automatic forward could only guess at — wrongly, whenever a component
rendered more than one styleable host.

### GameObjects are named after their class

There is no `id`. A host's GameObject is named after its first class, so a hierarchy reads as a screen
instead of forty rows called "View":

```csharp
new View(Styles.Hud)          // the GameObject is called "hud"
new Pressable(Styles.Start)   // "start"
```

A node with no class falls back to its kind (`View`, `Text`, `Pressable`, …).

An explicit id used to exist and was removed: every one in this project sat beside a class that
already said the same thing, and being able to set one on a component made it ambiguous — the
component's id and its root host's id both wanted the same GameObject name, so one had to be silently
thrown away.

**There is no `#id` selector either.** A rule that must hit exactly one node uses a class nobody else
wears, which the generated constants make unambiguous anyway; `#hud { … }` is rejected at parse time
with a diagnostic telling you to write `.hud`. Specificity therefore has two columns, not three:
classes and pseudo-classes, then type names.

---

## Stylesheets

Any `.css` file in the project is picked up automatically. Rules cascade by layer, then
specificity, then scope proximity, then document order, exactly as CSS does.

A sheet is **compiled when Unity imports it**: the `.css` asset is a `CompiledStyleSheet`, and its
diagnostics are reported against the file, once, at import. The editor and a player both load the
compiled form, so there is one path from CSS to the cascade, nothing parses CSS at runtime, and
saving one sheet recompiles only that sheet.

### Selectors

```css
View                      /* element or component type name — the C# name, capital and all */
.card                     /* class */
*                         /* everything */
.panel .title             /* descendant */
.panel > .title           /* child */
.row + .row               /* adjacent sibling */
.row ~ .row               /* subsequent sibling */
.card:hover .title        /* an ancestor's state styling a descendant */
```

A type selector is the C# type name and is **case-sensitive**: `View`, `Text`, `Card`. Written the
CSS way — `view` — it interns a different symbol and silently matches nothing.

### Nesting

Rules nest, and `&` stands for the enclosing selector — exactly as in CSS:

```css
.card {
    padding: 16px;

    &:hover { background-color: var(--hover); }   /* .card:hover           */
    &.is-open { height: auto; }                   /* .card.is-open         */
    & .title { color: gold; }                     /* .card .title          */
    .title { color: gold; }                       /* .card .title — same   */
    > .row { gap: 8px; }                          /* .card>.row            */
}
```

`&` **compounds**; it never joins onto the parent's text. The Sass spelling `&__title` /`&--wide`
is a diagnostic, not a rule — write `& .title` for an element inside the block and `&.wide` for a
class on the block itself. A block is scoped by the nesting rather than by retyping its name, so a
BEM sheet becomes:

```css
.card {
    padding: 16px;

    > .title { font-size: 24px; }                 /* .card>.title          */
    &.wide { width: 100%; }                       /* .card.wide            */
    &.wide > .title { font-size: 28px; }          /* .card.wide>.title     */
}
```

Prefer `>` over a descendant for an element the component renders itself: it costs no specificity
and cannot reach into a child component that happens to reuse the name.

Nesting is resolved when the sheet is built, into exactly the flat rules the previous section
describes. Nothing about matching, specificity or the cascade changes, and a nested rule sits in
document order where it was written — a block's own declarations first, then each nested rule in
turn. A block holding nothing but nested rules contributes no rule of its own.

A selector list nests too, and each of its selectors is relativised on its own, so
`.card { &:hover, .x { … } }` means `.card:hover` and `.card .x` — not a global `.x`. A list in the
*parent* expands: `.a, .b { & .c { … } }` becomes `.a .c` and `.b .c`, and both are scored with the
highest specificity in the group, which is what CSS means by taking `:is()` at its most specific
argument.

Because a class is global even when the rule that styles it is nested, two sheets that each style
the same bare class — `.panel` in two places — are reported when the sheets are built. A name
reached through an ancestor, qualified by another class or written inside an `@scope` is confined to
its own subtree and is left alone, which is what makes short names inside a block safe.

### Scope

A class is one global name, so `.label` in `Card.css` and `.label` in `Dialog.css` are the same
class — and nesting alone does not stop `.card .label` reaching the `.label` inside some *other*
component that happens to render under a card. `@scope` (CSS Cascade 6) is the standard way to fence
a component's rules in:

```css
@scope (.card) to (.slot) {
    :scope { padding: 1rem; }              /* the .card itself                         */
    .label { color: white; }               /* a .label inside .card, but not in .slot */
    & > .icon { width: 1rem; }             /* & is the root, with the root's specificity */

    .button {
        &:hover { opacity: 0.8; }          /* nesting works as anywhere else          */
    }
}
```

- **The root** (`(.card)`) is where the scope starts. A rule inside matches only nodes at or below a
  node matching it.
- **The limit** (`to (.slot)`) is where it stops. A node matching the limit, and everything below it,
  is out of scope — which is how a component keeps its rules out of the children it renders into a
  slot, or out of a nested component: `to (.child-root)`.
- **A rule that names neither `:scope` nor `&` is relative to the root**, as if written
  `:where(:scope) .label` — it must sit below the root, and the root adds nothing to its specificity.
  `:scope` counts as a pseudo-class; `&` counts as the root selector itself.
- **Proximity.** Of two equally specific rules, the one whose root is nearer the node wins, whatever
  the document order — so a `.label` inside a card inside a dialog takes the card's `.label`. An
  unscoped rule is as far away as a rule can be, so a scoped `.label` beats a global `.label` of equal
  specificity. A more specific rule still wins, as in CSS.

`@scope` nests inside `@media`, `@layer` and another `@scope`; outside any scope `:scope` means
`:root`. A scope with no root selector (`@scope { … }`) has nothing to be rooted at here and is a
diagnostic, as is an `@scope` written inside a style rule.

The generated class constants are untouched by any of this — `Card.Styles.Label` is still the one
global `.label`. A scope bounds *where a rule applies*, not what the class is called.

### Layers and imports

`@layer` (CSS Cascade 5) orders whole groups of rules, ahead of specificity:

```css
@layer reset, theme, components, utilities;

@layer theme      { .button { background-color: var(--accent); } }
@layer components { .button { background-color: #333; } }   /* wins: a later layer */
.button { opacity: 1; }                                       /* unlayered beats every layer */
```

A later layer beats an earlier one however specific the earlier one's selector is; rules outside any
layer beat all of them; the block form, the statement form, anonymous layers and dotted sublayers
(`framework.utilities`) all work. Layer order is decided across every sheet, by where each name is
first mentioned in cascade order — so the usual pattern is one small sheet naming the layers, imported
by the rest.

`@import` (standard syntax only — `@import "x.css";`, `@import url("x.css");`, optionally with
`layer` or `layer(name)`) does not copy rules: every sheet in the project is already loaded once,
globally. What it does is **order**. An imported sheet always cascades before the sheet importing it,
whatever their paths, and `layer(name)` puts the whole imported sheet in that layer:

```css
/* Theme/Index.css */
@import "Reset.css" layer(reset);
@import "Tokens.css" layer(theme);
```

Paths resolve relative to the importing sheet (`Assets/` and `Packages/` paths are taken as given).
Without imports the cascade order is ordinal asset path, as it always was. An import must come before
other rules; a media query or `supports()` on one is named and ignored, since a sheet that applies
everywhere cannot be imported conditionally; an import of a file that is not in the project, and an
import cycle, are each a warning.

### Mixins

`@mixin` and `@apply` (CSS Functions and Mixins) name a block of declarations and nested rules once
and reuse it in any style rule:

```css
@mixin --raised(--depth <length>: 2px) {
    box-shadow: 0 var(--depth) 4px rgba(0, 0, 0, 0.4);
    &:hover { opacity: 0.9; }
    @contents { border-radius: 4px; }      /* replaced by the block passed to @apply */
}

.card   { @apply --raised; }
.dialog { @apply --raised(8px) { border-radius: 12px; } }
```

- **Expanded when the sheet compiles.** `@apply` is replaced by the mixin's body, nested in the rule
  that applies it, so `&`, nested rules and `@media` mean what they would written there by hand.
- **Parameters** are read with `var(--name)` inside the body and replaced by the argument, the
  parameter's default, or the `var()` fallback, in that order. An argument containing commas is
  wrapped in braces: `@apply --shadows({0 1px 2px #000, 0 0 1px #fff})`. Types are not checked.
- **Visibility.** An `@apply` sees the mixins of its own sheet and of every sheet it imports, directly
  or through another import. Editing an imported mixin recompiles the sheets that apply it. Where a
  name is defined more than once, the last definition in cascade order wins.

An unknown mixin, too many arguments, a parameter left without a value, a mixin that applies itself,
an `@apply` outside a style rule and a `@mixin` that is not at the top level of a sheet are each
named and ignored.

### Media queries

`@media` works at the top level and, following CSS Nesting, inside a rule's own block:

```css
@media (max-width: 40rem) {
    .card { padding: 1rem; }
}

.card {
    padding: 2rem;

    @media (max-width: 40rem) {
        padding: 1rem;

        .title { font-size: 1rem; }
    }
}
```

The two are the same thing. A block written inside a rule desugars to an implicit `&` rule, so its
bare declarations apply to the parent at the parent's specificity, and anything nested further
resolves against the parent as usual.

Supported features are **`width`**, **`height`**, **`orientation`** and **`aspect-ratio`**, each in
the `min-`/`max-`/plain forms and in the range forms (`(width > 40rem)`, `(40rem <= width)`,
`(20rem < width <= 60rem)`), plus two of the framework's own:

- **`input-device: keyboard | gamepad`** — the device the player last touched. Keyboard and mouse
  count as one.
- **`gamepad-layout: xbox | playstation | switch | generic`** — the button family of the last pad
  used, read from its layout (so a DualSense is `playstation`). It keeps its value while the player
  is on the keyboard, so a prompt can prepare the right glyphs before they pick the pad back up.

```css
.hint-pad { display: none; }

@media (input-device: gamepad) {
    .hint-keys { display: none; }
    .hint-pad { display: flex; }
}
```

A pad counts as used once a button is pressed or a stick passes half its travel, so drift never flips
prompts while the player types. `InputDeviceTracker.Device` and `.Layout` answer the same question
from C#. A comma
separates alternatives, `and` combines them, `not` inverts one, and a media type (`screen`, `all`,
`print`) is honoured. Nesting one block inside another is a logical AND.

Lengths are **`px` or `rem`** — a percentage has nothing to be a percentage of here, and `calc()`
has no scope to resolve against. A `rem` breakpoint tracks the live rem size, so rescaling the UI
moves it.

**`width` and `height` measure the container rect, not the screen.** That is the same space `px` and
`rem` already resolve in everywhere else in the sheet, so `max-width: 1280px` means the same number
as `width: 1280px` on a rule. A UI rendered into a half-screen panel breaks at the panel's width
rather than the monitor's.

A media rule sits in the cascade exactly where it was written — document order is unchanged, and so
is specificity. Nothing about the cascade changes; a conditional rule is simply not a candidate
while its condition does not hold.

For the cases CSS cannot reach — rendering a *different* subtree rather than restyling the one you
have — read the same query from C# with `UseMedia`, or the raw size with `UseViewport`. See
[Hooks](#hooks).

### Properties

**Layout** — `display` · `position` · `left`/`right`/`top`/`bottom` · `inset` · `flex-direction` ·
`flex-wrap` · `justify-content` · `align-items` · `align-self` · `align-content` ·
`flex-grow`/`-shrink`/`-basis` · `flex` · `width`/`height` · `min-`/`max-` · `aspect-ratio` ·
`padding` · `margin` · `gap`/`row-gap`/`column-gap` · `overflow`

`flex` writes all three longhands, so `flex: 1` is `1 1 0` — items share the container rather than
their own content widths — and `flex: none`/`flex: auto` mean what CSS says. A fixed width under a
`flex: 1` shell still needs `flex-grow: 0; flex-basis: auto; width: …`, because the basis wins.

**Visual** — `background-color` · `background` (colour only) · `background-image` · `opacity` ·
`visibility` · `border` · `border-width`/`border-color`/`border-style` (+ per side) ·
`border-radius` (+ per corner) · `box-shadow` · `color` · `-rui-checker` · `-rui-grid`

**Text**, all inherited — `font-family` · `font-size` · `font-weight` · `text-align` ·
`vertical-align` · `white-space` · `line-height` · `letter-spacing` · `word-spacing` ·
`text-transform`

`Text` and `Image` are boxes like any other: everything under **Visual** applies to them as well, and
`padding`/`border-width` inset the glyphs or the sprite rather than only growing the box around them.
So a framed icon or a chip with a background is one node, not a `View` wrapping a `Text`. Both are
built as two GameObjects for this — the paint on the node, the content on a child — because uGUI
allows one `Graphic` per GameObject and a canvas draws a parent before its children, so a background
on the child would sit *over* the thing it is meant to sit behind.

`vertical-align` is not the CSS property of the same name. There are no inline boxes here — a text
run is a block that TextMeshPro aligns inside — so this is TMP's vertical alignment, and it only
shows when the box is taller than the text (an explicit `height`, a stretched row, extra
`line-height`). It defaults to `middle`, which is where text sat before the property existed.

`background-image` takes `resource("Textures/Foo")` — a Resources path to a texture or an unpacked
sprite — or a `linear-gradient()`/`radial-gradient()`. The texture is stretched across the box;
there is no `background-size`/`-position`/`-repeat` to say otherwise.

Each border edge keeps its own width, colour and style, and the corners mitre between two different
sides at 45° as CSS draws them. `border-radius` still has to be large enough to survive the border,
since the inner edge is the outer one pushed inward.

All ten `border-style` keywords are drawn, and every one of them follows `border-radius` around the
corners rather than falling back to a box:

```css
.ghost  { border: 2px dashed var(--line-strong); border-radius: 12px; }
.slot   { border: 3px dotted var(--border-subtle); border-radius: 999px; }
.plaque { border: 6px double var(--accent); }
.key    { border: 4px outset var(--raised); }   /* also inset, groove, ridge */
.open   { border-left-style: none; }            /* also hidden */
```

- **`solid` is the default, not `none`.** A border here has always been a width and a colour, so a
  rule that sets `border-width` without a style means the line it can already see. Defaulting to
  CSS would erase every one of them.
- **`none` and `hidden` give the space back**, as CSS does — the used width of such an edge is zero,
  so the content moves out rather than leaving a transparent gutter. They differ only in table
  border conflict resolution, which does not exist here, so the two are the same thing.
- **Dashes are fitted to the side they run along**, mitre to mitre, so every edge starts and ends on
  ink and the four corners stay symmetrical. Dash and dot size scale with the edge's own width, so
  a thicker border gets longer dashes rather than more of them.
- **`inset`, `outset`, `groove` and `ridge` shade from the authored colour**, halving its RGB on the
  edges facing the light and leaving alpha alone. A `groove` on a colour with no headroom to darken
  — black, or a near-transparent tint — reads as flat, exactly as it does in a browser.
- **`transition` does not animate a style change**; it flips on the frame the rule starts, the way
  border width and radii already do.

`visibility: hidden` keeps the layout box and stops the node painting or taking clicks. It is
inherited, and hiding is a zero alpha on a `CanvasGroup` that multiplies down the subtree — so
unlike CSS, a descendant cannot win itself back with `visibility: visible`.

**Transform** — `transform: translate() | translateX() | translateY() | scale() | scaleX() | scaleY() | rotate(Ndeg)`.
`scale(s)` scales both axes and `scale(x, y)` each separately. Rotation is clockwise, as CSS measures it, and a positive `translateY` moves down. A translation
keeps its unit: `rem` scales with the rem size and a percentage is of the node's own box, so
`translateX(-100%)` moves a node exactly its own width. A keyframe track takes the unit of its first
nonzero stop; mixing lengths and percentages within one track is not supported. All five channels are
written on every `transform` declaration, so a more specific rule replaces the whole list rather than
merging into it. A `transform` may read custom properties — `translateX(calc(var(--bar) * -1))` — and is
parsed per node once they are substituted; inside `@keyframes`, which has no scope, that is rejected.

**Transform origin** — `transform-origin`, one or two components from `left`/`center`/`right`,
`top`/`center`/`bottom`, a length, or a percentage, in either order when both are keywords. It
defaults to `50% 50%` as CSS does, so scaling and rotation act from the centre unless a rule says
otherwise. It is implemented by moving the rect's pivot, which is why the framework — not your
scene — owns the pivot of every node it lays out.

**Transitions** — `transition` and its longhands. `transition-property: transform` covers all five
transform channels, and `transition-property: border-color` all four edges.

**Animations** — `animation` and its longhands, against a `@keyframes` block. See
[Transitions, animations, enter and exit](#transitions-animations-enter-and-exit).

Keyword values are only the ones Yoga and TextMeshPro can express, and an unrecognised one is a
diagnostic rather than a silent default:

| Property | Accepts |
| -------- | ------- |
| `display` | `flex` · `none` — there is no `block`; everything is flex |
| `position` | `static` · `relative` · `absolute` |
| `overflow` | `visible` · `hidden` · `scroll` |
| `flex-direction` | `row` · `column` · `row-reverse` · `column-reverse` |
| `flex-wrap` | `nowrap` · `wrap` · `wrap-reverse` |
| `justify-content` | `flex-start`/`start` · `center` · `flex-end`/`end` · `space-between` · `space-around` · `space-evenly` |
| `align-items`/`-self`/`-content` | the above, plus `auto` · `stretch` · `baseline` |
| `text-align` | `left` · `center` · `right` · `justify` |
| `vertical-align` | `middle`/`center` · `top` · `bottom` · `baseline` · `--geometry` · `--capline` |
| `white-space` | `normal` · `nowrap` |
| `font-weight` | `100`–`900`, or `thin` · `extralight` · `light` · `normal` · `medium` · `semibold` · `bold` · `extrabold` · `black` |
| `text-transform` | `none` · `uppercase` · `lowercase` · `capitalize` |
| `visibility` | `visible` · `hidden` (`collapse` is treated as `hidden`) |
| `border-style` | `solid` · `none` · `hidden` · `dashed` · `dotted` · `double` · `groove` · `ridge` · `inset` · `outset` |

`background-image` takes either a gradient or a resource:

```css
background-image: linear-gradient(180deg, var(--raised), var(--raised-deep));
background-image: radial-gradient(#34243F 0%, #2A1C33 60%, #221629 100%);
```

`box-shadow` is a comma-separated list in CSS order — `x y blur spread colour`, first entry on top.
A blur of zero gives the chunky 3D edge under a button; a spread with no offset gives a ring:

```css
box-shadow: 0 7px var(--banana-600);                  /* chunky edge */
box-shadow: 0 0 45px var(--accent), 0 27px 62px rgba(0, 0, 0, 0.35);
box-shadow: 0 0 0 4px var(--tile-gold);               /* ring */
```

A `var()` inside a shadow or a gradient resolves per node against the custom properties in scope,
which is what lets one `.card` rule serve every rarity.

`-rui-checker: <cell> <colour>` paints a repeating two-tone grid over the fill, and
`-rui-grid: <cell> <line> <colour>` paints hairlines of width `<line>` along the top and left edge
of every cell instead. They are the vendor-prefixed properties, because the CSS spellings would be a
`repeating-conic-gradient` or a tiled `linear-gradient`, and the mesh painter draws these in a
single pass. Both set the same property, so the later declaration on a node wins.

Units: `px`, `rem`, `%` and `auto`. A bare number is a length only when it is zero, as in CSS.
`line-height` is a unitless multiplier and `letter-spacing` is in `em`. Times are `ms` or `s`.

`calc()` does arithmetic on any of them, including on a custom property:

```css
.node {
    width: calc(2rem * 2);              /* folded when the sheet is built */
    padding: calc(var(--gap) * 2);      /* folded per node, where --gap is in scope */
    height: calc(100% / 3);
}
```

**Every term in one expression must share a unit.** `calc(100% - 20px)` is rejected with a
diagnostic, and so is `calc(1rem + 8px)`. This is not a gap waiting to be filled: what a length
resolves to is one number and one unit, and Yoga takes a point or a percent, never a sum of the two
— a percentage is not resolved against anything until layout runs. Rems cannot mix with pixels for
the same reason, since the rem size is a runtime knob. Multiplying or dividing by a plain number is
how an expression is meant to change scale, and that has no such restriction.

A literal expression is folded once, when the sheet is built, so it costs nothing at runtime. One
that reads a custom property is folded during the cascade, alongside `var()` itself — once per
(rule set, scope) pair rather than per node — and drops the declaration if a name it reads is
missing, exactly as a bare `var()` does.
Colours: `#rgb`, `#rrggbb`, `#rrggbbaa`, `rgb()`, `rgba()` — channels as 0–255 or a percentage, alpha
0–1 — and the named colours Unity's `ColorUtility` knows, which is a much shorter list than CSS's.

Inheritance is the main ergonomic win over a style object: set the font once on a screen and every
text run beneath it inherits.

```css
.screen { font-family: "Body"; font-size: 24px; color: var(--text-primary); }
```

### Fonts

Fonts are named in CSS and resolved from `Resources`:

```css
@font-face { font-family: "Display"; src: resource("Fonts/TitanOne-Regular"); }
```

`resource()` rather than `url()`, because the path is a Resources path.
`UiFonts.Register(name, asset, weight)` is the escape hatch for fonts Resources cannot reach.

`font-weight` picks a face rather than synthesising one — a TMP asset is a baked atlas of one face,
so there is no synthetic bolding worth having. **Register a face per weight**, and the weight names
the asset outright:

```css
@font-face { font-family: "Body"; src: resource("Fonts/Baloo2-Regular"); }
@font-face { font-family: "Body"; font-weight: 700; src: resource("Fonts/Baloo2-Bold SDF"); }
@font-face { font-family: "Body"; font-weight: 800; src: resource("Fonts/Baloo2-ExtraBold SDF"); }
```

A face registered without a weight is filed under 400, and a family asked for a weight it has no
face for falls back to its nearest one — so a sheet that never mentions `font-weight` keeps working
against a single registered face. The pair is resolved when the style is applied rather than when
the sheet is parsed, so `@font-face` and the rules using it may live in any file in any order.

The other way a family can carry its weights is TMP's own **Font Weights** table on the asset
(assigned in the Font Asset inspector). That still works — the weight reaches `TMP_Text.fontWeight`
whenever the *resolved* asset has an entry there — but **prefer `@font-face`**: a weight served from
the table is what TMP calls an *alternative typeface*, and an alternative typeface renders through a
second material, which costs a `TMP SubMeshUI` child GameObject on every text node using it and
leaves the node's own mesh empty. A face registered here is the primary asset instead, and the run
stays one mesh.

The weight is only handed to `TMP_Text.fontWeight` when the resolved asset actually has that table
entry, which is what stops the two mechanisms fighting — and matters beyond the sub-mesh, because
TMP's lookup for a non-Regular weight returns nothing at all rather than falling through to the
asset's own glyphs. A family registered at one weight and asked for another would otherwise send
every character down the missing-character path, on every rebuild, forever: the retry caches its
answer under the Regular key, so the composite key the next lookup builds misses again.

---

## States and pseudo-classes

Built in: `:hover` · `:active` · `:focus` · `:focus-visible` · `:disabled` · `:enter` · `:exit` ·
`:root`. (`:first-child` and friends are recognised but inert — see [Limitations](#limitations).)

```css
.btn:hover  { background-color: #463156; }
.btn:active { transform: translateY(3px); }

/* :focus follows focus however it was acquired. :focus-visible is additionally gated on the
   player using a gamepad or keyboard — which is why clicking leaves no navigation ring. */
.btn:focus-visible { border-color: #FFC53D; }
```

**Interaction never re-renders.** A state bit flips, the affected nodes re-resolve against an
already-cached computed style, and the element tree is untouched. Hovering allocates nothing.

Any node styled on hover is made hit-testable automatically, even a plain `View` — including when
the pseudo-class is on an ancestor, as in `.card:hover .title`, where the card needs the raycast and
the title needs the restyle.

### There are no custom states

The registry is closed: `:hover`, `:active`, `:focus`, `:focus-visible`, `:disabled`, `:enter`,
`:exit`, `:root`, and the four structural ones. Every one of them is set by the framework, and a
pseudo-class the parser does not recognise is now a diagnostic rather than a bit nothing will ever
set — a misspelled `:hovr` used to register cleanly and then silently never apply.

State a component decides for itself is a **class**, not a pseudo-class:

```csharp
new View(Styles.Tile | (Styles.TileScoring & isScoring))
```

```css
.tile--scoring { background-color: #FFC53D; }
```

That is what the components in this project already did, and it has the advantage a pseudo-class
cannot offer: the class name is a generated constant, so a typo is a compile error.

Changing classes costs a re-match; changing a framework state costs only a re-resolve.

---

## Transitions, animations, enter and exit

```css
.btn { background-color: #3A2847; transition: background-color 180ms ease-out; }
.btn:hover { background-color: #463156; }
```

Timing functions: `linear`, `ease`, `ease-in`, `ease-out`, `ease-in-out`, `--out-cubic`,
`--out-back`, `cubic-bezier(x1, y1, x2, y2)`, `steps(n[, start | end | jump-none | jump-both])`,
`step-start` and `step-end`. `ease` and `ease-in-out` are the same curve. Transitions and keyframe
segments evaluate every curve through the same code, so a named easing looks identical in both.

A shorthand that contains a `var()` cannot be split into its parts until the variable is known, and
the `transition` and `animation` shorthands are not expanded later — so to drive motion from tokens,
write the longhands:

```css
.btn {
    transition-property: background-color;
    transition-duration: var(--duration-fast);
    transition-timing-function: var(--ease-standard);
}
```

Animatable: `background-color`, `color`, `border-color`, `opacity`, and the five transform channels (translate X/Y, scale X/Y, rotation).
Everything else — border width and style, radii, shadows, layout — changes on the frame the rule starts
applying. That is a snap, not a bug: motion needs a channel behind it, and these do not have one.
Naming one of them in a `transition` is silently a snap; naming one *inside* a `@keyframes` block is
a diagnostic, since a keyframe block describes nothing but the motion.

Transitions and animations run on **unscaled time**. A paused game sets `Time.timeScale` to zero,
and a pause menu whose own animations are frozen is worse than one with none.

Retargeting mid-flight starts from where the value currently is, so an interrupted hover reverses
smoothly rather than jumping.

### Animations

```css
@keyframes pulse {
    from { opacity: 0.45; transform: scale(0.9); }
    50%  { opacity: 1;    transform: scale(1.1); animation-timing-function: --out-back; }
    to   { opacity: 0.45; transform: scale(0.9); }
}

.beacon { animation: pulse 900ms ease-in-out infinite alternate; }
```

`animation` and its longhands — `-name`, `-duration`, `-timing-function`, `-delay`,
`-iteration-count`, `-direction`, `-fill-mode`, `-play-state`. `from`/`to` are `0%`/`100%`, a stop
may list several offsets (`0%, 100% { … }`), and a stop may carry its own
`animation-timing-function`, which shapes the segment *starting* there rather than the run as a
whole.

**`animation-delay` may be negative**, and then it does not delay: the animation opens as though it
had already been running that long, which is how a set of identical loops is spread out without
staggering when each one starts.

```css
.mote:nth-of-type(2) { animation: rise 19s linear infinite; animation-delay: -4s; }
```

It seeks rather than shifts phase, so it walks whole iterations — an animation opening three cycles
in lands in the right one, facing the right way under `alternate` — and a negative delay past the end
of a finite animation opens it already finished, `fill` deciding what shows. `transition-delay` has
no equivalent and a negative one is treated as zero.

A **running animation owns the properties it writes**, and the cascade has nothing to say about them
until it ends — animations outrank normal declarations in CSS, and they do here. When it ends,
`forwards` and `both` hold the last keyframe; otherwise the properties go back to the cascade, and a
`transition` on the same property eases them there from wherever the animation left them.

An animation **restarts when, and only when, its spec changes**. So `.x:hover { animation: … }`
starts on hover and stops on leaving, and a rule that keeps naming the same animation does not
restart it every restyle.

Whichever offsets a track omits are filled in from the node's own cascaded value, as CSS does: a
track starting at `50%` runs from whatever the rule says the property is.

**Entry animations do not need `:enter`.** An animation on the resting rule plays once when the node
mounts, which is what CSS does — and `:enter` is cleared one tick after the mount, so an animation
declared there would lose its spec and stop.

### Motion is per node, and cheap when absent

A node with neither a `transition` nor an `animation` allocates no channels and no player, and its
values are written straight through. The cost only appears where the motion is.

### Mount and unmount

```css
.toast { transition: opacity 400ms ease-out; }
.toast:enter { opacity: 0; }
.toast:exit  { opacity: 0; }
```

`:enter` applies on the frame the node mounts and is then cleared, so the transition plays from
there towards the resting style.

`:exit` holds the node on screen for the longer of its transition and its animation before releasing
it — the whole subtree, so a box does not fade out after its own text has vanished. It leaves the
layout immediately, so siblings reflow at once. A node re-appearing in that position mid-exit
cancels it. Descendants restyle with it, so `.panel:exit > .body { … }` animates a child out
alongside; only the outermost node of a removed subtree plays an exit, and everything beneath it
leaves when it does.

```css
.modal:exit { animation: fade-out 200ms ease-out; }
```

An exit needs a matching `transition` or `animation`, or there is no duration to hold for and the
node is released at once. An **infinite** animation counts as no duration rather than as forever: a
node that never finishes exiting is worse than one that snaps.

---

## Inline styles and custom properties

For values a stylesheet cannot know — a tile's board position, a bar's measured width, a rarity
colour from game data.

```csharp
new View(Styles.Cell)
{
    Style =
    {
        [Css.Left] = x,
        [Css.Top] = y,
        [Css.BackgroundColor] = Color.red,
        ["--accent"] = rarity.Colour,      // a custom property
    },
}
```

`Style` is a collection on a get-only member rather than a constructor parameter, which is why it is
the one thing that can share an initializer with `Children = { … }`. Where a local is already at hand,
assigning after the fact reads better than either:

```csharp
var cell = new View(Styles.Cell);
cell.Style[Css.Left] = x;
```

Typed handles exist for the properties inline styling actually reaches for — `Left`, `Right`, `Top`,
`Bottom`, `Width`, `Height`, `MinHeight`, `FontSize`, `TranslateX`, `TranslateY`, `BackgroundColor`,
`Color`, `BorderColor`, `Opacity`, `FlexGrow`, `Rotation`, `Scale`, `ScaleX`, `ScaleY` — grouped by value type so
`Css.Left` takes a `StyleLength`, `Css.Color` a `Color` and `Css.Opacity` a `float`.

A custom property holds a colour, a length, or a bare number — `--columns: 8`. The number matters
because `calc()` scales a length by a unitless one and every `Number` property reads one directly, so
a grid can keep its count beside its gap rather than baking a literal into each expression:

```css
.grid {
    --count: 8;
    --gap: 12px;
    --cell: calc((100% - var(--gap) * (var(--count) - 1)) / var(--count));
}
```

Anything else is carried through as its text and read in the syntax of whichever property it lands
in: a duration (`--fast: .15s`), a timing function (`--ease: cubic-bezier(.4, 0, .2, 1)`), a font
family (`--font-body: "Chakra Petch"`), a whole shadow (`--glow: 0 0 12px rgba(var(--accent), .5)`).
A value of several space-separated tokens is always kept as text, since read as one scalar it would
keep its first token and lose the rest.

`rgba(var(--x), alpha)` reads a colour variable and replaces its alpha. It works in any colour
property, in shadows, gradient stops and checkers, and inside another custom property — which is what
lets a theme define each colour once and derive every translucent wash from it:

```css
:root { --accent: #5BE9F2; --accent-wash: rgba(var(--accent), .08); }
.theme-amber { --accent: #FFB547; }   /* every wash follows */
```

This is the one place ReactiveUI departs from the CSS rule that a custom property's `var()` references
are resolved where the property is declared: an `rgba(var(…))` token, like a text token, is resolved
where it is *used*, which is what makes a class-based theme on any ancestor retint derived tokens.

Custom properties cascade and inherit like any other, which is what lets data-driven colour live in
CSS instead of being threaded through component props:

```css
.card { border: 3px solid var(--accent); }
.card:hover { border-color: var(--accent-hover); }
```

Inline style wins over the cascade and never participates in the style cache, so a per-instance
value can never mint a shared rule.

Three helpers cover the properties CSS writes as one and the model stores as several:

```csharp
Style.SetRadius(16f);                  // all four corners
Style.SetBorder(2f, Color.white);      // four widths, colours and styles
Style.SetBorder(2f, Color.white, BorderStyle.Dashed);
Style.SetGlow(accent, blur: 24f);      // a single centred box-shadow
```

Anything without a typed handle goes through the `PropId` indexer:

```csharp
Style[PropId.BackgroundGradient] = StyleValue.OfReference(Gradient.Radial(top, bottom));
```

Custom properties are the better answer wherever one exists — a scoped `--accent` reaches every
descendant rule, while an inline property only reaches the node it is set on.

---

## Motion: values that change every frame

Anything animating continuously — a falling tile, a drag, a counter ticking — should bypass render
and the cascade entirely.

```csharp
public Element Render()
{
    var handle = UseElementRef();

    UseEffectOnce(handle, static target =>
    {
        var driver = new GameObject("Bob").AddComponent<Bobber>();
        driver.Target = target;

        return () => UnityEngine.Object.Destroy(driver.gameObject);
    });

    return new Text(Styles.Prompt, new("PRESS ANY KEY"), elementRef: handle);
}
```

```csharp
private void Update() => Target.SetTranslate(0f, Mathf.Sin(Time.unscaledTime * 2f) * 12f);
```

`ElementRef` offers `SetTranslate`, `SetScale`, `SetRotation`, `ClearMotion`, `Size` and
`IsMounted`. Motion is an **offset on top** of the cascaded transform, so a stylesheet edit cannot
wipe out a drag in progress and a drag cannot permanently displace a styled position.

---

## Focus, navigation and hotkeys

`Pressable` elements register as focus targets automatically. Keyboard arrows and gamepad
stick/d-pad move focus; Enter, Space or the south button activate it.

Navigation picks the nearest candidate *in the direction of travel*, weighting alignment above raw
proximity — otherwise a control slightly nearer but well off-axis steals focus from the one the
player was clearly heading for.

`FocusManager` lives on the runtime — `UiRoot.Runtime.Focus`, or `UseFocus()` during a render — and
takes `ElementRef`s, so a component drives it through a handle from `UseElementRef`:

```csharp
var focus = uiRoot.Runtime!.Focus;

focus.Focus(handle);        // focus a specific node
focus.Blur();
focus.Move(Vector2.right);  // returns false when nothing lies that way
focus.Submit();
focus.PushScope(modal);     // confine navigation, for a modal
focus.PopScope(modal);
focus.IsFocused(handle);
```

A pressable that is part of a larger control — one segment of a segmented control, a slider's track —
passes `Unfocusable: true`: it still takes clicks, but navigation passes over it. A focused pressable
can also take the arrows for itself: `OnMove` is offered each direction first, and returning true
consumes it, so a settings row can step its value on left/right while up/down still move between rows.

A modal wants the scope for exactly as long as it is mounted, which is what the hooks are for:

```csharp
public Element Render(in ConfirmProps props)
{
    var dialog = UseElementRef();
    var confirm = UseElementRef();

    UseFocusScope(dialog);      // pushed after first layout, popped at unmount
    UseAutoFocus(confirm);      // the default action is focused when the dialog opens

    return new View(Styles.Dialog, dialog) { /* ... */ new Pressable(Styles.Confirm, elementRef: confirm) };
}
```

Pushing a scope drops any focus left outside it; popping restores whatever was focused before, so a
closed dialog hands focus back to the button that opened it. Navigation skips disabled pressables and
anything inside a subtree playing its `:exit` animation, and focusing a node inside a `Scroll` scrolls
it into view.

Without configuration the input driver reads devices directly — arrows, d-pad and left stick to
move; Enter, Space or the south button to activate — so navigation works out of the box. A game with
its own input map assigns **Navigate** and **Submit** actions on the `UiRoot` (or overrides
`CreateInputBindings`), and navigation then follows the player's rebinds and whichever maps the game
has enabled. Everything else — Cancel, tab switching, context actions — is a `UseInputAction` on the
component that owns it.

---

## Scroll and portals

```csharp
new Scroll(Styles.Inventory, new(ScrollAxis.Vertical))
{
    rows,
}
```

The `Scroll` node is the viewport, sized by the stylesheet. Its children lay out at natural size and
overflow it; the content rect is measured and resized after layout, and `ScrollRect` handles the
rest.

```csharp
new Portal { new View(Styles.Modal) { /* ... */ } }
```

A portal's children render into an overlay above the entire tree, escaping any parent's clipping and
draw order. The portal stays in the instance tree, so the cascade, lifecycle and selectors still see
it where it was declared — only its GameObjects move.

---

## Hot reload

Edit a `.css` file, save, and a running editor session restyles — including in Play Mode, with all
state intact. Stylesheets are data, so changing one never triggers a domain reload.

On reload every derived table is dropped and the tree re-matched. In-flight transitions are stopped
first, so nothing retargets from a mid-animation value against rules that no longer exist.

The reload rides on Unity's import of the file, so it is only as prompt as the editor is: a sheet
changed by something outside Unity — another tool, a script, a git checkout — restyles nothing until
the editor notices, and an `AssetDatabase.Refresh()` alone does not always count. Forcing the import
(`AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate)`) or the menu item below does.

Two menu items exist for when the automatic pass does not fire — the generated assets are written
after a delay past the domain reload, which an unfocused or headless editor can skip:

| Menu item | What it does |
| --------- | ------------ |
| **Tools ▸ ReactiveUI ▸ Rebuild Stylesheets** | Recompiles every `.css`, restyles the tree, and rewrites both the class-name list the compiler generates constants from and the build manifest. |
| **Tools ▸ ReactiveUI ▸ Rebuild Stylesheet Manifest** | Just the build manifest. |

---

## Builds

Sheets are compiled when they are imported, so a build carries compiled sheets and no CSS parser:
the CSS library (the package's build of [ExCSS](https://github.com/TylerBrinks/ExCSS)) is editor-only,
and loading a sheet at startup is a read rather than a parse. A player cannot load raw `.css` at
runtime as a result.

There is nothing to assign. Every `.css` in the project — `Assets/` and packages alike — is collected
into a generated `StyleSheetManifest` at `Resources/ReactiveUI/StyleSheets`, in the same order the
editor cascades them, and `UiRoot` loads it when no editor catalog has claimed the source. That
manifest is also what keeps the compiled sheets referenced, so they reach the player at all. Finding
no manifest is logged as an error rather than left to look like a layout bug.

A compiled sheet is tied to the build of the package that compiled it. Upgrading the package
recompiles every sheet on the next import, automatically.

---

## How it works

**Elements are declarations; instances are what persists.** `Render` describes intent, and the
reconciler matches that description against the mounted instance tree — each child against whatever
stood in its position last render, provided the type still agrees — creating, updating or destroying
instances to agree. GameObjects are pooled and recycled.

**A declaration is not an object.** An element is a `readonly struct` holding one integer: an index
into an arena that lives for a single render pass and is then dropped whole. Props go into a typed
array per props type, so nothing is boxed. Declaring a tree of a hundred nodes therefore allocates
**nothing** — which matters because memoisation cannot help here the way it does in React: a parent
that bails out has already built its children by the time it can compare them, so under the previous
object model a re-render of a 64-cell board allocated 64 elements in order to discover that 62 of
them had not changed.

What makes the arena reclaimable is that nothing outlives the pass. The committed props live on the
instance, a host copies the inline entries it was given, and the children comparison that used to
require keeping the previous element tree is gone — see [Memoisation](#memoisation).

**A component re-renders itself.** A `SetState` on a leaf rebuilds that subtree only; it never walks
up to an ancestor.

**Rendering and committing are separate.** Deciding what a node should look like is not the same as
writing it, and the second half is where the cost is: applying a style pushes forty-odd properties
into Yoga across the interop boundary, reconfigures the graphic and dirties its mesh. So the engine
hands back *interned* computed styles — an unchanged result is the same object — and a node whose
style resolved to what it already has applies nothing at all. This is what makes a parent's
re-render stop at children that did not change, rather than merely stopping at ones that did not
need re-rendering.

**Interaction is not a render.** Matching answers "could this rule ever apply" and ignores
pseudo-classes entirely, so it never changes when a pointer moves. Which rules apply *right now* is
a per-node condition mask, and the computed style behind it is cached. A node whose rules never
mention `:hover` produces the same cache key hovered or not.

**A resize is not a restyle.** Dragging a window moves the viewport every frame, while a media
query's answer flips a handful of times across the whole drag — so the two are kept apart. Every
frame the runtime re-evaluates the compiled conditions, which is a walk over a few dozen
comparisons; only when an *answer* actually moves does it drop the caches and re-match the tree.
`UseMedia` does the same thing one query at a time, holding its last boolean and re-rendering only
on a flip. `UseViewport` is the deliberate exception: it re-renders on every change, because every
pixel is a new answer to what it was asked.

**The frame** runs in `LateUpdate`, before uGUI rebuilds its canvas — which is what lets text
measure itself through TextMeshPro without re-entering uGUI's layout:

```text
render → overlay sync → layout → enter/exit → effects
```

The render step drains dirty components until none remain, capped at a few passes; blowing the cap
means a component is setting state during its own render, and it is reported as an error rather than
looping.

Adding a style property is three edits: an entry in `PropId`, a line in `PropertyRegistry`, and
whatever consumes it — `StyleApplier` for anything Yoga lays out, the host's `ApplyStyle` for anything
painted.

---

## Limitations

Known and deliberate:

- **A child's identity is its position.** There are no keys, so a list that changes length in the
  middle slides every later child onto its neighbour's instance — re-propping it instead of
  unmounting the one that went away. Render fixed slots when a list can be edited in the middle; see
  [Identity is position](#identity-is-position). Appends, truncations and stable-length lists are
  fine as they are.
- **Structural pseudo-classes are inert.** `:first-child`, `:last-child`, `:only-child` and `:empty`
  are registered, so they parse and cost nothing — but nothing sets them yet, so a rule using one
  never applies. `:nth-child(An+B)` and every other functional pseudo-class is rejected with a
  diagnostic.
- **No id selectors.** `#hud { … }` is rejected with a diagnostic: there is no id, and a GameObject is
  named after its class. Use a class of its own, and note that specificity has two columns as a
  result — classes and pseudo-classes, then type names.
- **An unknown pseudo-class is rejected.** The registry is closed and every bit in it is set by the
  framework, so `:hovr` — or a project's own `:scoring` — is a diagnostic rather than a rule that
  parses cleanly and never matches. State a component owns is a class.
- **Only host elements match by type.** `View`, `Text`, `Image`, `Pressable` and `Scroll`; a selector
  naming a component (`Card { … }`) is rejected, since a component's type does not reach the tree.
- **One transition and one animation per rule set.** `transition: a 1s, b 2s` is not supported and
  the last declaration wins; `animation: a 1s, b 2s` is rejected with a diagnostic.
- **`var()` has no fallback.** `var(--x, #fff)` parses but the fallback is ignored; an unresolvable
  reference drops the declaration, as CSS does.
- **A `calc()` may not mix units**, and one that reads a custom property may not sit inside a
  `box-shadow`, a `-rui-checker`, a gradient stop or a `transform` — those bake their lengths in
  when the sheet is built, so there is nowhere to keep an expression still waiting on a scope. A
  literal `calc()` works in all of them. The same goes for a `@keyframes` block, which is built
  once. Every one of these is a named diagnostic, not a silent misdraw.
- **No `::before`/`::after`, `z-index`, attribute selectors, `filter`, or
  `background-position/size/repeat`.** Pseudo-elements are rejected with a diagnostic; an unknown
  property is named in one.
- **`@media` covers `width`, `height`, `orientation`, `aspect-ratio`, `input-device` and
  `gamepad-layout` only.** No `pointer`, `hover`, `prefers-*`, `device-*` or `resolution`, no `or`,
  and no nested conditions (`((a) and (b))`, `not (a)` inside parentheses). An unknown feature, an
  unreadable value, an unknown media type and an unsupported form are each a named diagnostic that
  makes the query never match — deliberately never match rather than invert or partly apply. Sheets
  and `UseMedia` share one reader, so a query means the same thing in both. `@keyframes` and
  `@font-face` inside a block are ignored and named: a clip is built once and filed by name, and a
  face is registered when the sheet loads, so neither has anywhere to keep a condition.
- **A pseudo-class must be registered before its sheet is compiled.** `UiStates.Names` is handed to
  the CSS parser as the set of names it should accept, and sheets compile when the editor imports
  them, so a project registering its own should do so from an `[InitializeOnLoad]` hook (and again
  at runtime before the first `UiRoot`). The bits themselves are remapped by name when a sheet loads,
  so registration order does not have to match between editor and player.
- **`@scope` is explicit.** Every scope names its root; there is no implicit scope rooted at "the
  component that owns this sheet", and `@scope` inside a style rule is not supported yet.
- **`@import` orders; it does not include.** Every sheet is loaded once, globally, so importing a
  sheet twice, or conditionally, changes nothing but order.
- **`@mixin` ignores cascade layers.** A `@mixin` must be at the top level of a sheet, and the last
  definition of a name wins whatever layer its sheet is imported into.
- **`overflow: hidden` clips to the rect, not the corners.** A rounded box still spills its children
  over the rounding, because the clip is a `RectMask2D`.
- **Corner radii are circular.** An elliptical `border-radius: 20px / 10px` keeps the horizontal
  radius and drops the vertical one.
- **Inline custom properties are rebuilt only by their own node's render.** Changing an ancestor's
  class, variables or inherited properties re-matches and restyles every host below it, memoised
  components included — but a node whose element set `Style["--x"]` keeps the variable scope its last
  render built, because those values live only in that render's element. Theme through classes on an
  ancestor, not inline variables on a descendant.
- **A shorthand containing `var()` is not expanded.** `transition: opacity var(--fast)` is kept whole
  by the parser and never split; use the longhands, as in
  [Transitions](#transitions-animations-enter-and-exit).
- **Malformed CSS drops the offending rule** and logs a warning; it never fails the import.

---

## Diagnostics

Reported by the compiler, so they appear in the Unity console and in your IDE alike.

| Id | Severity | Meaning |
| -- | -------- | ------- |
| **RUI0001** | Error | A component, or a type it is nested in, is not `partial`, so its constructors cannot be generated. |
| **RUI0002** | Warning | A type with a colocated stylesheet is not `partial`, so its `Styles` table cannot be generated. |
| **RUI0003** | Warning | Two CSS classes map to the same C# member (`btn-label` and `btn_label`); the first is kept. |
| **RUI0004** | Error | A component is generic, which the generator cannot complete. |
| **RUI0005** | Warning | A stylesheet sits beside a `.cs` that declares no type of the same name, so its classes get no table. |
