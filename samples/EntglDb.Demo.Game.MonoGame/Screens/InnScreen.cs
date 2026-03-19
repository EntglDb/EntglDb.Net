using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace EntglDb.Demo.Game.MonoGame.Screens;

/// <summary>Inn rest screen — delegates to GameEngine.RestAtInnAsync.</summary>
public sealed class InnScreen : GameScreen
{
    private readonly GameEngine _engine;
    private readonly string _nodeId;
    private Hero _hero;
    private readonly SpriteFont _titleFont;
    private readonly SpriteFont _font;

    private string _message = "Resting...";
    private Color _messageColor = Color.LightGray;
    private bool _done;
    private KeyboardState _prevKeys;

    public InnScreen(GameEngine engine, string nodeId, Hero hero, SpriteFont titleFont, SpriteFont font)
    {
        _engine = engine;
        _nodeId = nodeId;
        _hero = hero;
        _titleFont = titleFont;
        _font = font;
    }

    public override void Enter()
    {
        _prevKeys = Keyboard.GetState();
        _ = RestAsync();
    }

    private async Task RestAsync()
    {
        var result = await _engine.RestAtInnAsync(_hero, CancellationToken.None);

        if (!result.Rested)
        {
            _message = result.FailReason == "already_full_hp"
                ? "You are already at full health!"
                : $"Not enough gold! Need {result.Cost}G.";
            _messageColor = Color.Red;
        }
        else
        {
            _message = $"You rest at the inn. HP fully restored! (-{result.Cost}G)";
            if (result.MpRestored > 0)
                _message += $"  +{result.MpRestored} MP (capped at 80%)";
            _messageColor = Color.LimeGreen;
        }
        _done = true;
    }

    public override void Update(GameTime gameTime)
    {
        if (!_done) return;
        var keys = Keyboard.GetState();
        if (keys.IsKeyDown(Keys.Enter) && _prevKeys.IsKeyUp(Keys.Enter))
            GoTo(new GameMenuScreen(_engine, _nodeId, _hero, _titleFont, _font));
        _prevKeys = keys;
    }

    public override void Draw(SpriteBatch sb, GameTime gameTime)
    {
        var vp = sb.GraphicsDevice.Viewport;
        var titleSize = _titleFont.MeasureString("The Inn");
        sb.DrawString(_titleFont, "The Inn",
            new Vector2((vp.Width - titleSize.X) / 2f, 80), new Color(255, 200, 0));

        sb.DrawString(_font, _message, new Vector2(80, 180), _messageColor);
        if (_done)
            sb.DrawString(_font, "Press Enter to continue.", new Vector2(80, vp.Height - 40), Color.DimGray);
    }
}
