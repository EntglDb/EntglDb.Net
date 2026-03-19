using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace EntglDb.Demo.Game.MonoGame.Screens;

/// <summary>Recent battle log (last 10 entries across all peers).</summary>
public sealed class BattleHistoryScreen : GameScreen
{
    private readonly GameEngine _engine;
    private readonly string _nodeId;
    private readonly SpriteFont _titleFont;
    private readonly SpriteFont _font;
    private List<BattleLog> _logs = [];
    private KeyboardState _prevKeys;

    public BattleHistoryScreen(GameEngine engine, string nodeId, SpriteFont titleFont, SpriteFont font)
    {
        _engine = engine;
        _nodeId = nodeId;
        _titleFont = titleFont;
        _font = font;
    }

    public override void Enter()
    {
        _logs = _engine.GetRecentBattles(10).ToList();
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

        var titleSize = _titleFont.MeasureString("Recent Battles");
        sb.DrawString(_titleFont, "Recent Battles",
            new Vector2((vp.Width - titleSize.X) / 2f, y), new Color(255, 200, 0));
        y += titleSize.Y + 20;

        if (_logs.Count == 0)
        {
            sb.DrawString(_font, "No battles yet.", new Vector2(80, y), Color.Gray);
        }
        else
        {
            sb.DrawString(_font,
                $"{"Result",-10} {"Hero",-16} {"Monster",-18} {"Rewards",-18} {"Node"}",
                new Vector2(80, y), Color.DimGray);
            y += 26;

            foreach (var log in _logs)
            {
                string result  = log.Victory ? "Victory" : "Defeat";
                string rewards = log.Victory ? $"+{log.XpGained}XP +{log.GoldGained}G" : "-";
                string node    = log.NodeId == _nodeId ? "local" : log.NodeId;
                var color      = log.Victory ? Color.LimeGreen : Color.OrangeRed;

                sb.DrawString(_font,
                    $"{result,-10} {log.HeroName,-16} {log.MonsterName,-18} {rewards,-18} {node}",
                    new Vector2(80, y), color);
                y += 24;
            }
        }

        sb.DrawString(_font, "Press Enter / Esc to go back.",
            new Vector2(80, vp.Height - 40), Color.DimGray);
    }
}
