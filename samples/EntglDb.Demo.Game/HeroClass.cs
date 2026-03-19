namespace EntglDb.Demo.Game;

public enum HeroClass
{
    Warrior,
    Mage,
    Rogue,
    Paladin,
    Ranger,
    Necromancer,
}

public record HeroClassProfile(
    string Emoji,
    string Description,
    int MinHp,          int MaxHp,
    int MinAttack,      int MaxAttack,
    int MinDefense,     int MaxDefense,
    int MinMp,          int MaxMp,
    int MinMagicAttack, int MaxMagicAttack);

public static class HeroClassFactory
{
    public static readonly Dictionary<HeroClass, HeroClassProfile> Profiles = new()
    {
        [HeroClass.Warrior] = new("⚔️",  "High HP & ATK, low magic",         120, 140,  18, 22,  8, 10,  20,  30,   4,  7),
        [HeroClass.Mage]    = new("🔮",  "Fragile but devastating magic",      65,  85,   6,  9,  2,  4,  85, 110,  20, 26),
        [HeroClass.Rogue]   = new("🗡️", "Agile — shines with Quick Strike",   85, 105,  15, 20,  4,  7,  40,  58,   8, 13),
        [HeroClass.Paladin] = new("🛡️", "Tough tank with solid holy magic",  105, 125,  11, 15,  9, 13,  50,  68,  12, 17),
        [HeroClass.Ranger]      = new("🏹",  "Versatile all-rounder",              90, 112,  13, 18,  5,  8,  44,  62,  10, 15),
        [HeroClass.Necromancer] = new("💀",  "Dark sorcerer — frail but deadly magic", 60, 78,  5,  8,  1,  3, 100, 130,  24, 32),
    };

    public static void ApplyInitialStats(Hero hero, HeroClass cls, Random rng)
    {
        var p = Profiles[cls];
        hero.HeroClass   = cls;
        hero.MaxHp       = rng.Next(p.MinHp,          p.MaxHp          + 1);
        hero.Hp          = hero.MaxHp;
        hero.Attack      = rng.Next(p.MinAttack,      p.MaxAttack      + 1);
        hero.Defense     = rng.Next(p.MinDefense,     p.MaxDefense     + 1);
        hero.MaxMp       = rng.Next(p.MinMp,          p.MaxMp          + 1);
        hero.Mp          = (int)(hero.MaxMp * 0.8); // start at inn cap
        hero.MagicAttack = rng.Next(p.MinMagicAttack, p.MaxMagicAttack + 1);
    }
}
