using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace EntglDb.Demo.Game.MonoGame.Screens;

/// <summary>Displays the chest loot result and level-up (if any) before returning.</summary>
public sealed class ChestResultScreen : GameScreen
{
    private readonly ChestResult _result;
    private readonly Hero _hero;
    private readonly string _nodeId;
    private readonly SpriteFont _titleFont;
    private readonly SpriteFont _font;
    private KeyboardState _prevKeys;

    // Injected via GameMenuScreen for the back navigation
    private GameEngine? _engine;

    public ChestResultScreen(
        ChestResult result, Hero hero, string nodeId,
        SpriteFont titleFont, SpriteFont font,
        GameEngine? engine = null)
    {
        _result = result;
        _hero = hero;
        _nodeId = nodeId;
        _titleFont = titleFont;
        _font = font;
        _engine = engine;
    }

    public override void Enter() => _prevKeys = Keyboard.GetState();

    public override void Update(GameTime gameTime)
    {
        var keys = Keyboard.GetState();
        if ((keys.IsKeyDown(Keys.Enter) && _prevKeys.IsKeyUp(Keys.Enter)) ||
            (keys.IsKeyDown(Keys.Escape) && _prevKeys.IsKeyUp(Keys.Escape)))
        {
            if (_engine != null)
                GoTo(new GameMenuScreen(_engine, _nodeId, _hero, _titleFont, _font));
            else
                GoTo(null);
        }
        _prevKeys = keys;
    }

    public override void Draw(SpriteBatch sb, GameTime gameTime)
    {
        var vp = sb.GraphicsDevice.Viewport;
        float y = 80;

        var titleSize = _titleFont.MeasureString("You found a chest!");
        sb.DrawString(_titleFont, "You found a chest!",
            new Vector2((vp.Width - titleSize.X) / 2f, y), new Color(255, 200, 0));
        y += titleSize.Y + 30;

        sb.DrawString(_font, _result.Name, new Vector2(80, y), Color.Gold);
        y += 32;

        if (_result.GoldGained > 0)
        {
            sb.DrawString(_font, $"+{_result.GoldGained} Gold", new Vector2(80, y), Color.Gold);
            y += 26;
        }
        if (_result.XpGained > 0)
        {
            sb.DrawString(_font, $"+{_result.XpGained} XP", new Vector2(80, y), Color.Cyan);
            y += 26;
        }

        if (_result.LevelUp != null)
        {
            y += 16;
            sb.DrawString(_font,
                $"LEVEL UP!  Level {_result.LevelUp.NewLevel}   MaxHP {_result.LevelUp.MaxHp}  ATK {_result.LevelUp.Attack}  DEF {_result.LevelUp.Defense}",
                new Vector2(80, y), Color.Magenta);
        }

        sb.DrawString(_font, "Press Enter to continue.", new Vector2(80, vp.Height - 40), Color.DimGray);
    }
}
