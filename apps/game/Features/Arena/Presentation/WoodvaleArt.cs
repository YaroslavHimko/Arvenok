using System;
using System.Collections.Generic;
using System.Linq;
using Arvenok.Contracts;
using Godot;

namespace Arvenok.Game.Features.Arena.Presentation;

/// <summary>Client-only texture cache. No image resource is loaded by the dedicated world host.</summary>
internal sealed class WoodvaleArt
{
    private const string Root = "res://Assets/Art2D/woodvale/";
    private readonly Dictionary<string, Texture2D> _textures = new(StringComparer.Ordinal);
    private readonly Dictionary<CharacterClass, CharacterSkin> _characters;
    private readonly Dictionary<MonsterSpecies, MonsterSkin> _monsters;

    public WoodvaleArt(WorldArt definitions)
    {
        _characters = definitions.Characters.ToDictionary(c => c.Class);
        _monsters = definitions.Monsters.ToDictionary(m => m.Species);
        // Validate the complete supplied set at client startup, including currently unselected classes.
        foreach (CharacterSkin skin in definitions.Characters) foreach (string file in skin.Directions.Values)
            RequireSize(Texture(file), 96, 96);
        foreach (PropAsset prop in definitions.PropAssets) RequireSize(Texture(prop.File), prop.Width, prop.Height);
        foreach (MonsterSkin monster in definitions.Monsters) RequireSize(Texture(monster.File), monster.Width, monster.Height);
        RequireSize(Texture(definitions.TerrainAtlas), definitions.AtlasColumns * 32, definitions.AtlasRows * 32);
    }

    public Texture2D Texture(string relativePath)
    {
        if (!_textures.TryGetValue(relativePath, out Texture2D? texture))
        {
            texture = GD.Load<Texture2D>(Root + relativePath) ?? throw new InvalidOperationException($"Missing Woodvale texture: {relativePath}");
            _textures.Add(relativePath, texture);
        }
        return texture;
    }

    public Texture2D Entity(EntitySnapshot entity)
    {
        if (entity.Species is MonsterSpecies species) return Texture(_monsters[species].File);
        string direction = Math.Abs(entity.Facing.Y) >= Math.Abs(entity.Facing.X)
            ? entity.Facing.Y >= 0 ? "down" : "up" : entity.Facing.X >= 0 ? "right" : "left";
        return Texture(_characters[entity.Class].Directions[direction]);
    }

    private static void RequireSize(Texture2D texture, int width, int height)
    {
        if (texture.GetWidth() != width || texture.GetHeight() != height)
            throw new InvalidOperationException($"Texture dimensions disagree with manifest: {texture.ResourcePath}");
    }
}
