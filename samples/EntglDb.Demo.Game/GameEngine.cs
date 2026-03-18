namespace EntglDb.Demo.Game;

/// <summary>
/// Pure game business logic: hero management, combat, loot, and persistence.
/// Has no dependency on any UI framework — all methods return plain data result types.
/// </summary>
public class GameEngine
{
    private readonly GameDbContext _db;
    private readonly string _nodeId;
    private readonly Random _rng;

    /// <summary>MP cost for the Fireball spell.</summary>
    public const int SpellCost = 15;

    public GameEngine(GameDbContext db, string nodeId, Random rng)
    {
        _db = db;
        _nodeId = nodeId;
        _rng = rng;
    }

    // ── Heroes ───────────────────────────────────────────────────────────────

    public async Task<Hero> CreateHeroAsync(string name, HeroClass heroClass, CancellationToken ct)
    {
        var hero = new Hero { Name = name, NodeId = _nodeId };
        HeroClassFactory.ApplyInitialStats(hero, heroClass, _rng);
        await _db.Heroes.InsertAsync(hero);
        await _db.SaveChangesAsync(ct);
        return hero;
    }

    public IEnumerable<Hero> GetAliveHeroes() =>
        _db.Heroes.FindAll().Where(h => h.IsAlive).ToList();

    public async Task SaveHeroAsync(Hero hero, CancellationToken ct)
    {
        await _db.Heroes.UpdateAsync(hero);
        await _db.SaveChangesAsync(ct);
    }

    // ── Encounter ────────────────────────────────────────────────────────────

    /// <summary>25% chance of a chest encounter; otherwise returns false (monster fight).</summary>
    public bool IsChestEncounter() => _rng.Next(100) < 25;

    public Monster SpawnMonster(int heroLevel) => MonsterFactory.Spawn(heroLevel);

    // ── Combat ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes a single combat round.
    /// Mutates <paramref name="hero"/> (HP, MP) and <paramref name="monsterHp"/> in place
    /// and returns a result record the UI can render without any additional state.
    /// </summary>
    public BattleRoundResult ExecuteRound(
        Hero hero, ref int monsterHp, Monster monster, PlayerAction action)
    {
        int heroDmg;
        int hit1 = 0, hit2 = 0;
        int monsterDmgMult = 100;
        bool dodgeAttempt = false;
        int mpSpent = 0;

        switch (action)
        {
            case PlayerAction.QuickStrike:
                hit1    = Math.Max(1, (int)(hero.Attack * 0.6) - monster.Defense / 2 + _rng.Next(-2, 3));
                hit2    = Math.Max(1, (int)(hero.Attack * 0.6) - monster.Defense / 2 + _rng.Next(-2, 3));
                heroDmg = hit1 + hit2;
                break;
            case PlayerAction.PowerBlow:
                heroDmg       = Math.Max(1, (int)(hero.Attack * 1.7) - monster.Defense + _rng.Next(-2, 5));
                monsterDmgMult = 150;
                break;
            case PlayerAction.Parry:
                heroDmg       = Math.Max(1, hero.Attack / 2 - monster.Defense / 2 + _rng.Next(-1, 3));
                monsterDmgMult = 30;
                break;
            case PlayerAction.Dodge:
                heroDmg     = Math.Max(1, hero.Attack - monster.Defense + _rng.Next(-3, 4));
                dodgeAttempt = true;
                break;
            case PlayerAction.Fireball:
                heroDmg  = Math.Max(1, (int)(hero.MagicAttack * 1.5) + _rng.Next(-2, 5));
                hero.Mp -= SpellCost;
                mpSpent  = SpellCost;
                break;
            default: // Attack
                heroDmg = Math.Max(1, hero.Attack - monster.Defense + _rng.Next(-3, 4));
                break;
        }

        monsterHp -= heroDmg;
        bool monsterDefeated = monsterHp <= 0;

        int  monsterDamage      = 0;
        bool dodgedSuccessfully = false;
        bool heroDefeated       = false;

        if (!monsterDefeated)
        {
            int baseDmg = Math.Max(1, monster.Attack - hero.Defense + _rng.Next(-3, 4));

            if (dodgeAttempt && _rng.Next(100) < 55)
            {
                dodgedSuccessfully = true;
            }
            else
            {
                monsterDamage = dodgeAttempt
                    ? baseDmg
                    : Math.Max(1, (int)(baseDmg * monsterDmgMult / 100.0));
                hero.Hp   -= monsterDamage;
                heroDefeated = hero.Hp <= 0;
            }
        }

        return new BattleRoundResult(
            action, heroDmg, hit1, hit2,
            monsterDamage, monsterDmgMult,
            dodgeAttempt, dodgedSuccessfully,
            monsterDefeated, heroDefeated,
            hero.Hp, Math.Max(0, monsterHp), hero.Mp, mpSpent);
    }

    /// <summary>
    /// Applies end-of-battle consequences to the hero (XP, gold, death),
    /// persists the hero and a new BattleLog, and returns the outcome summary.
    /// </summary>
    public async Task<BattleOutcome> FinalizeBattleAsync(
        Hero hero, Monster monster, bool victory, CancellationToken ct)
    {
        LevelUpResult? levelUp = null;
        int mpGained = 0;

        if (victory)
        {
            hero.Xp            += monster.XpReward;
            hero.Gold          += monster.GoldReward;
            hero.MonstersKilled++;

            int mpGain  = Math.Max(3, monster.XpReward / 8);
            int mpBefore = hero.Mp;
            hero.Mp     = Math.Min(hero.MaxMp, hero.Mp + mpGain);
            mpGained    = hero.Mp - mpBefore;

            levelUp = TryLevelUp(hero);
        }
        else
        {
            hero.Hp      = 0;
            hero.IsAlive = false;
        }

        var battleLog = new BattleLog
        {
            HeroId      = hero.Id,
            HeroName    = hero.Name,
            MonsterName = monster.Name,
            NodeId      = _nodeId,
            Victory     = victory,
            XpGained    = victory ? monster.XpReward   : 0,
            GoldGained  = victory ? monster.GoldReward : 0,
        };

        await _db.Heroes.UpdateAsync(hero);
        await _db.BattleLogs.InsertAsync(battleLog);
        await _db.SaveChangesAsync(ct);

        return new BattleOutcome(victory, battleLog.XpGained, battleLog.GoldGained, mpGained, levelUp);
    }

    // ── Chest ────────────────────────────────────────────────────────────────

    public async Task<ChestResult> OpenChestAsync(Hero hero, CancellationToken ct)
    {
        int roll = _rng.Next(100);
        ChestType type;
        string    name;
        int       goldGain, xpGain;

        if (roll < 40)
        {
            type = ChestType.Wooden; name = "📦 Wooden Chest";
            goldGain = _rng.Next(8, 26);   xpGain = 0;
        }
        else if (roll < 75)
        {
            type = ChestType.Silver; name = "🪙 Silver Chest";
            goldGain = _rng.Next(20, 55);  xpGain = 0;
        }
        else if (roll < 95)
        {
            type = ChestType.Magic; name = "✨ Magic Chest";
            goldGain = 0;                  xpGain = _rng.Next(20, 60);
        }
        else
        {
            type = ChestType.Golden; name = "🏆 Golden Chest";
            goldGain = _rng.Next(60, 150); xpGain = _rng.Next(40, 80);
        }

        hero.Gold += goldGain;
        hero.Xp   += xpGain;
        var levelUp = TryLevelUp(hero);

        await _db.Heroes.UpdateAsync(hero);
        await _db.SaveChangesAsync(ct);

        return new ChestResult(type, name, goldGain, xpGain, levelUp);
    }

    // ── Inn ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to rest at the inn. Mutates <paramref name="hero"/> on success and persists.
    /// The last 20% of MP above the inn cap must be earned through combat.
    /// </summary>
    public async Task<InnRestResult> RestAtInnAsync(Hero hero, CancellationToken ct)
    {
        int cost = hero.Level * 10;

        if (hero.Hp >= hero.MaxHp)
            return new InnRestResult(false, "already_full_hp", cost, 0);

        if (hero.Gold < cost)
            return new InnRestResult(false, "not_enough_gold", cost, 0);

        hero.Gold -= cost;
        hero.Hp    = hero.MaxHp;

        int mpCap      = (int)(hero.MaxMp * 0.8);
        int mpRestored = Math.Max(0, mpCap - hero.Mp);
        hero.Mp        = Math.Max(hero.Mp, mpCap);

        await _db.Heroes.UpdateAsync(hero);
        await _db.SaveChangesAsync(ct);

        return new InnRestResult(true, null, cost, mpRestored);
    }

    // ── Queries ──────────────────────────────────────────────────────────────

    public IEnumerable<BattleLog> GetRecentBattles(int count = 10) =>
        _db.BattleLogs.FindAll()
            .OrderByDescending(b => b.Timestamp)
            .Take(count)
            .ToList();

    public IEnumerable<Hero> GetLeaderboard(int count = 10) =>
        _db.Heroes.FindAll()
            .OrderByDescending(h => h.Level)
            .ThenByDescending(h => h.MonstersKilled)
            .Take(count)
            .ToList();

    // ── Internal helpers ─────────────────────────────────────────────────────

    private LevelUpResult? TryLevelUp(Hero hero)
    {
        int xpNeeded = hero.Level * 50;
        if (hero.Xp < xpNeeded) return null;

        hero.Level++;
        hero.Xp -= xpNeeded;

        var lp = HeroClassFactory.Profiles[hero.HeroClass];
        hero.MaxHp       += _rng.Next((lp.MaxHp - lp.MinHp) / 10 + 8,  (lp.MaxHp - lp.MinHp) / 10 + 18);
        hero.Hp           = hero.MaxHp;
        hero.Attack      += _rng.Next((lp.MaxAttack - lp.MinAttack) / 5 + 1,      (lp.MaxAttack - lp.MinAttack) / 5 + 5);
        hero.Defense     += _rng.Next((lp.MaxDefense - lp.MinDefense) / 5 + 1,    (lp.MaxDefense - lp.MinDefense) / 5 + 3);
        hero.MaxMp       += _rng.Next((lp.MaxMp - lp.MinMp) / 5 + 4,             (lp.MaxMp - lp.MinMp) / 5 + 12);
        hero.MagicAttack += _rng.Next((lp.MaxMagicAttack - lp.MinMagicAttack) / 5 + 1, (lp.MaxMagicAttack - lp.MinMagicAttack) / 5 + 4);
        // Level up restores MP to the 80% inn cap
        hero.Mp = Math.Max(hero.Mp, (int)(hero.MaxMp * 0.8));

        return new LevelUpResult(hero.Level, hero.MaxHp, hero.Attack, hero.Defense, hero.MaxMp, hero.MagicAttack);
    }
}
