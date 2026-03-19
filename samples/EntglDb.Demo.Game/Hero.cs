using System.ComponentModel.DataAnnotations;

namespace EntglDb.Demo.Game;

public class Hero
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string NodeId { get; set; } = string.Empty;
    public int Level { get; set; } = 1;
    public int Hp { get; set; } = 100;
    public int MaxHp { get; set; } = 100;
    public int Attack { get; set; } = 15;
    public int Defense { get; set; } = 5;
    public int Gold { get; set; } = 0;
    public int Xp { get; set; } = 0;
    public int MonstersKilled { get; set; } = 0;
    public bool IsAlive { get; set; } = true;
    public HeroClass HeroClass { get; set; } = HeroClass.Warrior;
    public int Mp { get; set; } = 50;
    public int MaxMp { get; set; } = 50;
    public int MagicAttack { get; set; } = 10;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
