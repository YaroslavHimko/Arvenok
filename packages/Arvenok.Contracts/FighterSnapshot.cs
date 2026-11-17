using System.Numerics;

namespace Arvenok.Contracts;

/// <summary>Immutable gameplay state exposed to presentation and transport adapters.</summary>
/// <remarks>The Godot adapter explicitly maps fields to RPC arguments; this is not a wire serializer.</remarks>
public readonly record struct FighterSnapshot(long Id, Vector2 Position, int Hp, bool Casting);
