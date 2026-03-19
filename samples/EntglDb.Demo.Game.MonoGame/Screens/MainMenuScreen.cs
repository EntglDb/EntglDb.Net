using EntglDb.Demo.Game.MonoGame.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EntglDb.Demo.Game.MonoGame.Screens;

/// <summary>
/// Opening screen: New Hero | Load Hero | Leaderboard | Quit.
/// </summary>
public sealed class MainMenuScreen : GameScreen
{
    private readonly GameEngine _engine;
    private readonly SpriteFont _titleFont;
    private readonly SpriteFont _font;
    private readonly string _nodeId;
    private readonly MenuWidget _menu;

    private static readonly string[] MenuItems =
        ["New Hero", "Load Hero", "Leaderboard", "Quit"];

    public MainMenuScreen(GameEngine engine, string nodeId, SpriteFont titleFont, SpriteFont font)
    {
        _engine = engine;
        _nodeId = nodeId;
        _titleFont = titleFont;
        _font = font;

        _menu = new MenuWidget(MenuItems);
        _menu.ItemConfirmed += OnMenuConfirmed;
    }

    private void OnMenuConfirmed(int index)
    {
        switch (MenuItems[index])
        {
            case "New Hero":
                GoTo(new CreateHeroScreen(_engine, _nodeId, _titleFont, _font));
                break;
            case "Load Hero":
                GoTo(new HeroSelectScreen(_engine, _nodeId, _titleFont, _font));
                break;
            case "Leaderboard":
                GoTo(new LeaderboardScreen(_engine, _nodeId, _titleFont, _font));
                break;
            case "Quit":
                GoTo(null);
                break;
        }
    }

    public override void Update(GameTime gameTime) => _menu.Update();

    public override void Draw(SpriteBatch sb, GameTime gameTime)
    {
        var viewport = sb.GraphicsDevice.Viewport;

        // Title
        var title = "Dungeon Crawler";
        var titleSize = _titleFont.MeasureString(title);
        sb.DrawString(_titleFont, title,
            new Vector2((viewport.Width - titleSize.X) / 2f, 60),
            new Color(255, 200, 0));

        // Subtitle
        var sub = $"Node: {_nodeId}   |   EntglDb P2P RPG Demo";
        var subSize = _font.MeasureString(sub);
        sb.DrawString(_font, sub,
            new Vector2((viewport.Width - subSize.X) / 2f, 110),
            Color.Gray);

        // Menu
        var menuOrigin = new Vector2(viewport.Width / 2f - 80, 200);
        _menu.Draw(sb, _font, menuOrigin, Color.LightGray, new Color(255, 200, 0));

        // Controls hint
        sb.DrawString(_font, "Up/Down: navigate   Enter: confirm",
            new Vector2(20, viewport.Height - 36), Color.DimGray);
    }
}
