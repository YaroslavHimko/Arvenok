# ADR 0001: Modular monorepo with an engine-independent simulation

- Status: accepted for the foundation
- Date: 2026-11-17

## Context

The existing Godot scene mixes combat, transport, input and drawing in one script. The intended game needs parallel work by gameplay, physics, art, animation, narrative, backend and operations teams. There is no cloud account, production fleet or established organization ownership in this repository.

## Decision

Keep a monorepo for atomic changes and a single build entry point. Move the Godot root to `apps/game`; place pure contracts, simulation and configuration in separate C# projects. Keep one Godot host and one RPC scene for the existing client/server prototype. Put authoring sources outside the engine import root. Reserve `services` and `infrastructure` as documented boundaries, with no pretend deployable resources. Record ownership and allowed project dependencies in a checked catalog.

Use the existing engine and target-framework versions for this refactor. Lock dependency resolution. Production runtime support and engine upgrades are separate, tested decisions.

## Consequences

Gameplay rules can be tested without starting Godot, and art sources do not enter the runtime importer automatically. The Godot Project Manager entry and launch paths change. Physics updates now advance rules on simulation ticks rather than render frames/wall-clock time. Root build policies apply to every project. Team review and branch protection still require repository administration.

Client and server currently share an assembly. Runtime presentation is omitted on servers, but the server export pipeline still needs implementation and verification. Many independent microservices, cloud-specific IaC and a shared global state framework would create operational commitments before their requirements are known; introduce them only with real ownership, APIs and tests.
