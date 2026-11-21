using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Arvenok.Simulation;

public readonly record struct ExperienceRecipient(long Id, int Level, bool Alive, Vector2 Position);
public readonly record struct ExperienceAward(long EntityId, int BaseShare, int Experience);

/// <summary>One bounded damage window per monster. Life numbers prevent replay without an ever-growing death-ID set.</summary>
public sealed class MonsterExperienceLedger
{
    public const double CreditSeconds = 15;
    public const float CreditRadius = 12 * CombatBalance.PixelsPerTile;
    private readonly Queue<(long Actor, int Damage, double Time)> _hits = new();
    private long _paidLife;
    public long Life { get; private set; } = 1;
    public int TrackedHits => _hits.Count;
    public void Record(long actor, int damage, int remainingHp, double time)
    {
        Prune(time);
        int effective = Math.Min(damage, remainingHp);
        if (actor <= 0 || effective <= 0 || _paidLife == Life) return;
        _hits.Enqueue((actor, effective, time));
        // Existing admitted attack rates are far below this bound, even at the player cap.
        while (_hits.Count > 4096) _hits.Dequeue();
    }
    public void Prune(double time) { while (_hits.TryPeek(out var hit) && hit.Time < time - CreditSeconds) _hits.Dequeue(); }
    public void RemoveAttacker(long id)
    {
        var retained = _hits.Where(h => h.Actor != id).ToArray(); _hits.Clear();
        foreach (var hit in retained) _hits.Enqueue(hit);
    }
    public void Respawn() { _hits.Clear(); Life++; }
    public IReadOnlyList<(long Id, long Damage)> EligibleContributions(int maxHp, Vector2 position, double time,
        IReadOnlyDictionary<long, ExperienceRecipient> recipients)
    {
        Prune(time);
        return _hits.GroupBy(h => h.Actor).Select(g => (Id: g.Key, Damage: g.Sum(h => (long)h.Damage)))
            .Where(p => p.Damage * 10 >= maxHp && recipients.TryGetValue(p.Id, out var recipient) && recipient.Alive
                && Vector2.DistanceSquared(position, recipient.Position) <= CreditRadius * CreditRadius).ToArray();
    }
    public IReadOnlyList<ExperienceAward> Resolve(long life, int maxHp, int baseExperience, int monsterLevel, Vector2 position,
        double time, IReadOnlyDictionary<long, ExperienceRecipient> recipients)
    {
        if (life != Life || _paidLife == life) return Array.Empty<ExperienceAward>();
        _paidLife = life;
        var eligible = EligibleContributions(maxHp, position, time, recipients);
        _hits.Clear();
        if (eligible.Count == 0 || baseExperience <= 0) return Array.Empty<ExperienceAward>();
        long total = eligible.Sum(p => p.Damage);
        var shares = eligible.Select(p => (p.Id, Share: (int)(baseExperience * p.Damage / total), Remainder: baseExperience * p.Damage % total))
            .OrderByDescending(p => p.Remainder).ThenBy(p => p.Id).ToArray();
        int remainder = baseExperience - shares.Sum(p => p.Share);
        return shares.Select((p, index) =>
        {
            int share = p.Share + (index < remainder ? 1 : 0);
            int difference = recipients[p.Id].Level - monsterLevel;
            int amount = difference <= 2 ? share : difference == 3 ? (share + 1) / 2 : difference == 4 ? (share + 2) / 4 : 0;
            return new ExperienceAward(p.Id, share, amount);
        }).OrderBy(p => p.EntityId).ToArray();
    }
}
