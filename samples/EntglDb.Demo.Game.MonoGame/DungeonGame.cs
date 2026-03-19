using EntglDb.Demo.Game.MonoGame.Screens;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EntglDb.Demo.Game.MonoGame;

/// <summary>MonoGame entry point — loads fonts and drives the screen stack.</summary>
public sealed class DungeonGame : Microsoft.Xna.Framework.Game
{
    private readonly GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch = null!;
    private SpriteFont _defaultFont = null!;
    private SpriteFont _titleFont   = null!;
    private readonly ScreenManager _screenManager = new();

    private readonly GameEngine _engine;
    private readonly string _nodeId;

    public DungeonGame(GameEngine engine, string nodeId)
    {
        _engine  = engine;
        _nodeId  = nodeId;
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth  = 1024,
            PreferredBackBufferHeight = 768
        };
        Content.RootDirectory = "Content";
        IsMouseVisible        = true;
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _defaultFont = Content.Load<SpriteFont>("Fonts/DefaultFont");
        _titleFont   = Content.Load<SpriteFont>("Fonts/TitleFont");

        _screenManager.Push(new MainMenuScreen(_engine, _nodeId, _titleFont, _defaultFont));
    }

    protected override void Update(GameTime gameTime)
    {
        if (_screenManager.Current == null)
        {
            Exit();
            return;
        }
        _screenManager.Update(gameTime);
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(15, 15, 25));
        _spriteBatch.Begin(samplerState: Microsoft.Xna.Framework.Graphics.SamplerState.PointClamp);
        _screenManager.Draw(_spriteBatch, gameTime);
        _spriteBatch.End();
        base.Draw(gameTime);
    }
}
