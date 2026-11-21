using System.Collections.Generic;
using System.Numerics;

namespace Arvenok.Contracts;

/// <summary>Engine-independent authored content. Asset paths are relative to the art collection.</summary>
public sealed record TerrainTile(string Id, int AtlasX, int AtlasY, bool Walkable);
public readonly record struct TerrainCell(int X, int Y, string TileId);
public sealed record PropAsset(string Id, string File, int Width, int Height, WorldRect? Footprint);
public sealed record PropPlacement(string Id, string AssetId, Vector2 Position);
public sealed record CharacterSkin(CharacterClass Class, IReadOnlyDictionary<string, string> Directions);
public sealed record MonsterSkin(MonsterSpecies Species, string File, int Width, int Height);
public sealed record WorldArt(string TerrainAtlas, int AtlasColumns, int AtlasRows,
    IReadOnlyList<TerrainTile> Tiles, IReadOnlyList<TerrainCell> Cells,
    IReadOnlyList<PropAsset> PropAssets, IReadOnlyList<PropPlacement> Props,
    IReadOnlyList<WorldRect> RockFormations, IReadOnlyList<CharacterSkin> Characters, IReadOnlyList<MonsterSkin> Monsters);

public sealed record MonsterSpawn(string Id, MonsterSpecies Species, Vector2 Position, int MaxHp,
    float MoveSpeed, float RoamRadius, double IdleMinSeconds, double IdleMaxSeconds, double RespawnSeconds, int Level = 1, int BaseExperience = 10);
