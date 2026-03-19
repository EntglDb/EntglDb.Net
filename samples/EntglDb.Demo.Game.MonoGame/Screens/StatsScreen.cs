using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace EntglDb.Demo.Game.MonoGame.Screens;

/// <summary>Hero statistics display screen.</summary>
public sealed class StatsScreen : GameScreen
{
    private readonly Hero _hero;
    private readonly string _nodeId;
    private readonly SpriteFont _titleFont;
    private readonly SpriteFont _font;
    private KeyboardState _prevKeys;

    public StatsScreen(Hero hero, string nodeId, SpriteFont titleFont, SpriteFont font)
    {
        _hero = hero;
        _nodeId = nodeId;
        _titleFont = titleFont;
        _font = font;
    }

    public override void Enter() => _prevKeys = Keyboard.GetState();

    public override void Update(GameTime gameTime)
    {
        var keys = Keyboard.GetState();
        if (keys.IsKeyDown(Keys.Escape) || keys.IsKeyDown(Keys.Enter))
            if (_prevKeys.IsKeyUp(Keys.Escape) && _prevKeys.IsKeyUp(Keys.Enter) ||
                keys.IsKeyDown(Keys.Escape) && _prevKeys.IsKeyUp(Keys.Escape) ||
                keys.IsKeyDown(Keys.Enter) && _prevKeys.IsKeyUp(Keys.Enter))
                GoTo(null); // pop back
        _prevKeys = keys;
    }

    public override void Draw(SpriteBatch sb, GameTime gameTime)
    {
        var vp = sb.GraphicsDevice.Viewport;
        float y = 60;

        var titleSize = _titleFont.MeasureString("Hero Stats");
        sb.DrawString(_titleFont, "Hero Stats",
            new Vector2((vp.Width - titleSize.X) / 2f, y), new Color(255, 200, 0));
        y += titleSize.Y + 20;

        var h = _hero;
        int xpNeeded = h.Level * 50;
        var profile  = HeroClassFactory.Profiles[h.HeroClass];

        DrawRow(sb, ref y, "Name",         h.Name);
        DrawRow(sb, ref y, "Class",        $"{h.HeroClass}  {profile.Description}");
        DrawRow(sb, ref y, "Level",        $"{h.Level}");
        DrawRow(sb, ref y, "HP",           $"{h.Hp} / {h.MaxHp}");
        DrawRow(sb, ref y, "MP",           $"{h.Mp} / {h.MaxMp}  (inn cap: {(int)(h.MaxMp * 0.8)})");
        DrawRow(sb, ref y, "Attack",       $"{h.Attack}");
        DrawRow(sb, ref y, "Magic Atk",    $"{h.MagicAttack}");
        DrawRow(sb, ref y, "Defense",      $"{h.Defense}");
        DrawRow(sb, ref y, "Gold",         $"{h.Gold}");
        DrawRow(sb, ref y, "XP",           $"{h.Xp} / {xpNeeded}");
        DrawRow(sb, ref y, "Kills",        $"{h.MonstersKilled}");
        DrawRow(sb, ref y, "Node",         h.NodeId);
        DrawRow(sb, ref y, "Status",       h.IsAlive ? "Alive" : "Fallen");

        sb.DrawString(_font, "Press Enter / Esc to go back.",
            new Vector2(80, vp.Height - 40), Color.DimGray);
    }

    private void DrawRow(SpriteBatch sb, ref float y, string label, string value)
    {
        sb.DrawString(_font, $"{label,-14} {value}", new Vector2(80, y), Color.LightGray);
        y += 26;
    }
}
