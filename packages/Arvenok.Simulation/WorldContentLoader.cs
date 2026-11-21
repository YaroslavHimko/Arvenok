using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Arvenok.Contracts;

namespace Arvenok.Simulation;

/// <summary>Validates authored map data and the supplied art manifest into shared immutable read models.</summary>
public static class WorldContentLoader
{
    public static WorldDefinition LoadDefault() => Parse(ReadResource("Arvenok.Art.Woodvale"), ReadResource("Arvenok.World.Forest"));

    private static string ReadResource(string name)
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidDataException($"Missing embedded world data: {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static WorldDefinition Parse(string manifestJson, string layoutJson)
    {
        using JsonDocument manifestDocument = JsonDocument.Parse(manifestJson);
        using JsonDocument layoutDocument = JsonDocument.Parse(layoutJson);
        JsonElement manifest = manifestDocument.RootElement, layout = layoutDocument.RootElement;
        Require(layout.GetProperty("schemaVersion").GetInt32() == 1, "Unsupported world schema.");
        int tileSize = layout.GetProperty("tileSize").GetInt32();
        int columns = layout.GetProperty("widthTiles").GetInt32(), rows = layout.GetProperty("heightTiles").GetInt32();
        Require(tileSize == CombatBalance.PixelsPerTile && tileSize == manifest.GetProperty("tile_size_px").GetInt32(), "Tile units disagree.");
        Require(columns is > 0 and <= 256 && rows is > 0 and <= 256, "Invalid map dimensions.");
        float radius = layout.GetProperty("bodyRadius").GetSingle();
        Require(float.IsFinite(radius) && radius > 0 && radius < tileSize / 2f, "Invalid body radius.");
        int atlasColumns = manifest.GetProperty("terrain_columns").GetInt32(), atlasRows = manifest.GetProperty("terrain_rows").GetInt32();
        var tiles = new Dictionary<string, TerrainTile>(StringComparer.Ordinal);
        foreach (JsonProperty property in manifest.GetProperty("terrain_tiles").EnumerateObject())
        {
            JsonElement xy = property.Value.GetProperty("atlas_xy");
            int x = xy[0].GetInt32(), y = xy[1].GetInt32();
            Require(x >= 0 && x < atlasColumns && y >= 0 && y < atlasRows, "Atlas coordinate outside texture.");
            Require(!tiles.Values.Any(t => t.AtlasX == x && t.AtlasY == y), "Duplicate atlas coordinate.");
            tiles.Add(property.Name, new TerrainTile(property.Name, x, y, property.Value.GetProperty("walkable").GetBoolean()));
        }
        string[] variants = layout.GetProperty("groundVariants").EnumerateArray().Select(e => e.GetString()!).ToArray();
        Require(variants.Length > 0 && variants.All(id => tiles.TryGetValue(id, out TerrainTile? t) && t.Walkable), "Invalid ground variants.");
        var cells = new TerrainCell[columns * rows];
        for (int y = 0; y < rows; y++) for (int x = 0; x < columns; x++)
        {
            int choice = ((x * 73856093 ^ y * 19349663) & int.MaxValue) % variants.Length;
            cells[y * columns + x] = new TerrainCell(x, y, variants[choice]);
        }
        foreach (JsonElement patch in layout.GetProperty("terrainPatches").EnumerateArray())
        {
            string id = patch.GetProperty("tile").GetString()!;
            Require(tiles.ContainsKey(id), $"Unknown terrain tile: {id}");
            JsonElement r = patch.GetProperty("rect");
            int x = r[0].GetInt32(), y = r[1].GetInt32(), w = r[2].GetInt32(), h = r[3].GetInt32();
            Require(x >= 0 && y >= 0 && w > 0 && h > 0 && x + w <= columns && y + h <= rows, "Terrain patch outside map.");
            for (int py = y; py < y + h; py++) for (int px = x; px < x + w; px++) cells[py * columns + px] = new TerrainCell(px, py, id);
        }

        var formations = layout.GetProperty("rockFormations").EnumerateArray().Select(Rectangle).ToArray();
        var obstacles = formations.ToList();
        var propAssets = new Dictionary<string, PropAsset>(StringComparer.Ordinal);
        JsonElement footprints = layout.GetProperty("propFootprints");
        foreach (JsonProperty property in manifest.GetProperty("props").EnumerateObject())
        {
            JsonElement size = property.Value.GetProperty("image_size"), footprint = footprints.GetProperty(property.Name);
            propAssets.Add(property.Name, new PropAsset(property.Name, AssetPath(property.Value.GetProperty("file")),
                PositiveSize(size[0]), PositiveSize(size[1]), footprint.ValueKind == JsonValueKind.Null ? null : Rectangle(footprint)));
        }
        var props = new List<PropPlacement>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement item in layout.GetProperty("props").EnumerateArray())
        {
            string id = item.GetProperty("id").GetString()!, asset = item.GetProperty("asset").GetString()!;
            Require(!string.IsNullOrWhiteSpace(id) && ids.Add(id), "Prop IDs must be unique.");
            Require(propAssets.TryGetValue(asset, out PropAsset? definition), $"Unknown prop: {asset}");
            Vector2 position = Point(item.GetProperty("position"));
            Require(position.X >= 0 && position.Y >= 0 && position.X <= columns * tileSize && position.Y <= rows * tileSize, "Prop outside world.");
            props.Add(new PropPlacement(id, asset, position));
            if (definition!.Footprint is WorldRect footprint)
                obstacles.Add(footprint with { X = position.X + footprint.X, Y = position.Y + footprint.Y });
        }
        var characters = new List<CharacterSkin>();
        JsonElement frames = manifest.GetProperty("characters").GetProperty("frames");
        foreach (CharacterClass kind in Enum.GetValues<CharacterClass>())
        {
            JsonElement character = frames.GetProperty(kind.ToString().ToLowerInvariant());
            var directions = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string direction in new[] { "down", "left", "right", "up" }) directions.Add(direction, AssetPath(character.GetProperty(direction)));
            characters.Add(new CharacterSkin(kind, new ReadOnlyDictionary<string, string>(directions)));
        }
        var monsterSkins = new List<MonsterSkin>();
        foreach (MonsterSpecies species in Enum.GetValues<MonsterSpecies>())
        {
            JsonElement skin = manifest.GetProperty("monsters").GetProperty(species.ToString().ToLowerInvariant());
            JsonElement size = skin.GetProperty("image_size");
            monsterSkins.Add(new MonsterSkin(species, AssetPath(skin.GetProperty("file")), PositiveSize(size[0]), PositiveSize(size[1])));
        }
        var monsters = new List<MonsterSpawn>();
        ids.Clear();
        foreach (JsonElement spawn in layout.GetProperty("monsterSpawns").EnumerateArray())
        {
            string id = spawn.GetProperty("id").GetString()!;
            Require(!string.IsNullOrWhiteSpace(id) && ids.Add(id), "Monster spawn IDs must be unique.");
            Require(Enum.TryParse(spawn.GetProperty("species").GetString(), out MonsterSpecies species) && Enum.IsDefined(species), "Unknown monster species.");
            int hp = spawn.GetProperty("maxHp").GetInt32();
            float speed = spawn.GetProperty("moveSpeedTiles").GetSingle() * tileSize, roam = spawn.GetProperty("roamRadiusTiles").GetSingle() * tileSize;
            JsonElement idle = spawn.GetProperty("idleSeconds");
            double min = idle[0].GetDouble(), max = idle[1].GetDouble(), respawn = spawn.GetProperty("respawnSeconds").GetDouble();
            Require(hp > 0 && float.IsFinite(speed) && speed > 0 && float.IsFinite(roam) && roam >= radius
                && double.IsFinite(min) && double.IsFinite(max) && min >= 1 && max <= 3 && max >= min
                && double.IsFinite(respawn) && respawn > 0, "Invalid monster movement, health, idle or respawn settings.");
            int level = spawn.GetProperty("level").GetInt32(), experience = spawn.GetProperty("baseExp").GetInt32();
            Require(level is >= 1 and <= 10 && experience is >= 0 and <= 1000000, "Invalid monster level/EXP budget.");
            monsters.Add(new MonsterSpawn(id, species, Point(spawn.GetProperty("position")), hp, speed, roam, min, max, respawn, level, experience));
        }
        return new WorldDefinition(columns * tileSize, rows * tileSize, tileSize, radius, obstacles.AsReadOnly(),
            Array.AsReadOnly(layout.GetProperty("protectedRegions").EnumerateArray().Select(Rectangle).ToArray()),
            Array.AsReadOnly(layout.GetProperty("playerSpawns").EnumerateArray().Select(Point).ToArray()))
        {
            Water = WaterRectangles(cells, tiles, columns, rows, tileSize).AsReadOnly(),
            MonsterSpawns = monsters.AsReadOnly(),
            Art = new WorldArt(AssetPath(manifest.GetProperty("terrain_atlas")), atlasColumns, atlasRows,
                tiles.Values.ToList().AsReadOnly(), Array.AsReadOnly(cells), propAssets.Values.ToList().AsReadOnly(), props.AsReadOnly(),
                Array.AsReadOnly(formations), characters.AsReadOnly(), monsterSkins.AsReadOnly())
        };
    }

    private static List<WorldRect> WaterRectangles(TerrainCell[] cells, Dictionary<string, TerrainTile> tiles, int columns, int rows, int size)
    {
        // Coalesce adjacent blocked cells; collision/path queries should not scan every individual water tile.
        var rectangles = new List<WorldRect>();
        var previous = new Dictionary<(int X, int Width), int>();
        for (int y = 0; y < rows; y++) for (int x = 0; x < columns;)
        {
            if (tiles[cells[y * columns + x].TileId].Walkable) { x++; continue; }
            int start = x;
            while (x < columns && !tiles[cells[y * columns + x].TileId].Walkable) x++;
            var key = (start, x - start);
            if (previous.TryGetValue(key, out int index) && rectangles[index].Y + rectangles[index].Height == y * size)
                rectangles[index] = rectangles[index] with { Height = rectangles[index].Height + size };
            else { previous[key] = rectangles.Count; rectangles.Add(new WorldRect(start * size, y * size, (x - start) * size, size)); }
        }
        return rectangles;
    }

    private static int PositiveSize(JsonElement value)
    {
        int size = value.GetInt32(); Require(size > 0, "Invalid image dimensions."); return size;
    }
    private static string AssetPath(JsonElement element)
    {
        string path = element.GetString() ?? "";
        Require(path.StartsWith("assets/", StringComparison.Ordinal) && path.EndsWith(".png", StringComparison.Ordinal)
            && !path.Contains("..", StringComparison.Ordinal) && !path.Contains('\\'), "Asset must be a relative PNG in the collection.");
        return path;
    }
    private static Vector2 Point(JsonElement value)
    {
        Vector2 point = new(value[0].GetSingle(), value[1].GetSingle());
        Require(float.IsFinite(point.X) && float.IsFinite(point.Y), "Invalid world coordinate."); return point;
    }
    private static WorldRect Rectangle(JsonElement value)
    {
        Vector2 point = Point(value);
        float width = value[2].GetSingle(), height = value[3].GetSingle();
        Require(float.IsFinite(width) && float.IsFinite(height) && width > 0 && height > 0, "Invalid footprint.");
        return new WorldRect(point.X, point.Y, width, height);
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
