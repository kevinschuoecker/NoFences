# ADR 0002: Plugin API versioning — numbered interfaces vs. capabilities

Status: accepted (direction), implementation deferred until pre-1.0 freeze

## Problem

The SDK grew v1→v4 with breaking interface changes. Before the public 1.0
freeze we must pick a model that stays binary-compatible for years:
numbered interfaces (`IFlowGridWidget2/3`), capability interfaces, or a
service-discovery mechanism.

## Analysis

The two sides of the API have fundamentally different compatibility rules:

- **Host side (`IWidgetHost`)**: implemented ONLY by FlowGrid, plugins only
  consume it. In .NET, adding members to an interface breaks *implementors*,
  not callers — so the host interface can grow additively without breaking
  existing plugin binaries. `IWidgetHost2/3/...` is unnecessary here.
- **Widget side (`IFlowGridWidget*`)**: implemented BY plugins. Any change
  to an implemented interface is binary-breaking. These must be frozen
  forever once published; new abilities must arrive as separate optional
  interfaces the host probes with `is`.

Numbered inheritance (`IFlowGridWidget3 : IFlowGridWidget2`) forces a linear
ladder: a widget wanting menu items must also implement click handling.
These concerns are orthogonal — the ladder is the wrong shape.

## Decision

1. Host side: keep the single `IWidgetHost`, grow it additively. Document
   that plugins must never implement it.
2. Widget side: before the 1.0 freeze, restructure into orthogonal
   capability interfaces — `IFlowGridWidget` (core) plus optional
   `IClickHandler`, `IMenuContributor`, `IControlProvider`. The current
   numbered interfaces remain as thin deprecated aliases for one release.
3. No service locator / `GetService<T>()` for now: it trades compile-time
   safety for indirection we don't need at this API size. Revisit only if
   the host surface grows past ~20 members.

## Rationale

Capability probing via `is` is idiomatic .NET, costs nothing at runtime,
keeps every published interface immutable, and matches how the host already
discovers `IFlowGridControlWidget`.

## Downsides

One-time migration for the four sample widgets; two API shapes coexist for
one release.

## Impact on plugins/API

Existing v2–v4 plugins keep loading during the transition release. After
the freeze, published interfaces never change again.

## Breaking changes

None at runtime during transition; the deprecated aliases are removed one
release after 1.0.
