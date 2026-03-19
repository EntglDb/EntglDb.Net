namespace EntglDb.Demo.Game;

public enum PlayerAction { Attack, QuickStrike, PowerBlow, Parry, Dodge, Fireball }

/// <summary>
/// Result of a single combat round, containing everything the UI needs to render.
/// </summary>
public record BattleRoundResult(
    PlayerAction Action,
    int HeroDamage,
    int HeroHit1,               // first hit value (QuickStrike only)
    int HeroHit2,               // second hit value (QuickStrike only)
    int MonsterDamage,
    int MonsterDamagePercent,   // 30 = Parry, 100 = normal, 150 = Power Blow
    bool DodgeAttempt,
    bool DodgedSuccessfully,
    bool MonsterDefeated,
    bool HeroDefeated,
    int HeroHpAfter,
    int MonsterHpAfter,
    int HeroMpAfter,
    int MpSpent);

/// <summary>Result of a level-up, used by both combat and chest outcomes.</summary>
public record LevelUpResult(int NewLevel, int MaxHp, int Attack, int Defense, int MaxMp, int MagicAttack);

/// <summary>Overall outcome of a battle (win or loss), returned after the loop ends.</summary>
public record BattleOutcome(
    bool Victory,
    int XpGained,
    int GoldGained,
    int MpGained,
    LevelUpResult? LevelUp);

/// <summary>Result of resting at the inn.</summary>
public record InnRestResult(
    bool Rested,
    string? FailReason,   // "already_full_hp" | "not_enough_gold"
    int Cost,
    int MpRestored);

public enum ChestType { Wooden, Silver, Magic, Golden }

/// <summary>Result of opening a treasure chest.</summary>
public record ChestResult(
    ChestType Type,
    string Name,
    int GoldGained,
    int XpGained,
    LevelUpResult? LevelUp);
