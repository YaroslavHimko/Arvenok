using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Arvenok.Contracts;

namespace Arvenok.Simulation;

public sealed partial class WorldSimulation
{
    private readonly Dictionary<Guid, GroundLoot> _groundLoot = new();
    private readonly Random _lootRandom;
    public GroundLoot[] GroundDrops() => _groundLoot.Values.OrderBy(l => l.Id).ToArray();
    private void ExpireLoot()
    { foreach (Guid id in _groundLoot.Values.Where(l => Time >= l.ExpiresAt).Select(l => l.Id).ToArray()) _groundLoot.Remove(id); }
    private void SpawnMonsterLoot(CombatEntity monster, long life, IReadOnlyDictionary<long, ExperienceRecipient> recipients)
    {
        if (monster.ExperienceLedger!.Life != life || monster.LootedLife >= life) return;
        monster.LootedLife = life;
        var eligible = monster.ExperienceLedger.EligibleContributions(monster.MaxHp, monster.Position, Time, recipients);
        Guid owner = eligible.OrderByDescending(p => p.Damage).ThenBy(p => _entities[p.Id].Character!.Id)
            .Select(p => _entities[p.Id].Character!.Id).FirstOrDefault();
        if (owner == Guid.Empty) return;
        Guid death = Guid.NewGuid(); int index = 0;
        foreach (ItemAmount amount in Economy.Roll(monster.Monster!.Spawn.Species, _lootRandom))
        {
            float angle = index++ * 2.4f;
            // Sweeping from the valid corpse position keeps every token on the same reachable land.
            Vector2 position = Map.Sweep(monster.Position, monster.Position + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 24);
            var item = new ItemStack(Guid.NewGuid(), amount.ItemId, amount.Quantity, amount.Properties);
            Guid id = Guid.NewGuid();
            _groundLoot.Add(id, new(id, death, item, owner, position, Time, Time + Economy.ProtectionSeconds, Time + Economy.ProtectionSeconds + Economy.PublicSeconds));
        }
    }
    private string? ValidatePickup(CombatEntity actor, Guid id)
    {
        if (!_groundLoot.TryGetValue(id, out GroundLoot? drop) || Time >= drop.ExpiresAt) return "Loot is gone or expired.";
        if (Time < drop.PublicAt && drop.OwnerId != actor.Character!.Id) return "Loot is protected for its contributor.";
        if (Vector2.DistanceSquared(actor.Position, drop.Position) > Economy.PickupRange * Economy.PickupRange || !Map.CanTraverse(actor.Position, drop.Position))
            return "Move within 2 tiles of reachable loot.";
        return null;
    }
}
