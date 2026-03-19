using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace EntglDb.Demo.Game.MonoGame.Screens;

/// <summary>Top-10 hero leaderboard, synced across P2P peers via EntglDb.</summary>
public sealed class LeaderboardScreen : GameScreen
{
    private readonly GameEngine _engine;
    private readonly string _nodeId;
    private readonly SpriteFont _titleFont;
    private readonly SpriteFont _font;
    private List<Hero> _heroes = [];
    private KeyboardState _prevKeys;

    public LeaderboardScreen(GameEngine engine, string nodeId, SpriteFont titleFont, SpriteFont font)
    {
        _engine = engine;
        _nodeId = nodeId;
        _titleFont = titleFont;
        _font = font;
    }

    public override void Enter()
    {
        _heroes = _engine.GetLeaderboard(10).ToList();
        _prevKeys = Keyboard.GetState();
    }

    public override void Update(GameTime gameTime)
    {
        var keys = Keyboard.GetState();
        if ((keys.IsKeyDown(Keys.Escape) && _prevKeys.IsKeyUp(Keys.Escape)) ||
            (keys.IsKeyDown(Keys.Enter)  && _prevKeys.IsKeyUp(Keys.Enter)))
            GoTo(null);
        _prevKeys = keys;
    }

    public override void Draw(SpriteBatch sb, GameTime gameTime)
    {
        var vp = sb.GraphicsDevice.Viewport;
        float y = 60;

        var titleSize = _titleFont.MeasureString("Leaderboard");
        sb.DrawString(_titleFont, "Leaderboard",
            new Vector2((vp.Width - titleSize.X) / 2f, y), new Color(255, 200, 0));
        y += titleSize.Y + 10;
        sb.DrawString(_font, "(synced across all nodes)",
            new Vector2((vp.Width - _font.MeasureString("(synced across all nodes)").X) / 2f, y), Color.Gray);
        y += 30;

        if (_heroes.Count == 0)
        {
            sb.DrawString(_font, "No heroes yet.", new Vector2(80, y), Color.Gray);
        }
        else
        {
            sb.DrawString(_font,
                $"{"#",-4} {"Hero",-16} {"Class",-14} {"Lv",-5} {"Kills",-8} {"Gold",-8} {"Status",-10} Node",
                new Vector2(80, y), Color.DimGray);
            y += 26;

            string[] medals = ["1st", "2nd", "3rd"];
            for (int i = 0; i < _heroes.Count; i++)
            {
                var h      = _heroes[i];
                string pos = i < medals.Length ? medals[i] : $"{i + 1}  ";
                string status = h.IsAlive ? "Alive" : "Fallen";
                string node   = h.NodeId == _nodeId ? "local" : h.NodeId;
                var color = h.IsAlive ? Color.LightGray : Color.DimGray;

                sb.DrawString(_font,
                    $"{pos,-4} {h.Name,-16} {h.HeroClass,-14} {h.Level,-5} {h.MonstersKilled,-8} {h.Gold,-8} {status,-10} {node}",
                    new Vector2(80, y), color);
                y += 24;
            }
        }

        sb.DrawString(_font, "Press Enter / Esc to go back.",
            new Vector2(80, vp.Height - 40), Color.DimGray);
    }
}
