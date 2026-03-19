using EntglDb.Demo.Game.MonoGame.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EntglDb.Demo.Game.MonoGame.Screens;

/// <summary>
/// In-game menu (Explore, Rest, Stats, History, Leaderboard, Back, Quit).
/// Displays the current hero status bar at the top.
/// </summary>
public sealed class GameMenuScreen : GameScreen
{
    private readonly GameEngine _engine;
    private readonly string _nodeId;
    private Hero _hero;
    private readonly SpriteFont _titleFont;
    private readonly SpriteFont _font;
    private readonly MenuWidget _menu;

    private static readonly string[] MenuItems =
    [
        "Explore Dungeon",
        "Rest at Inn",
        "View Stats",
        "Battle History",
        "Leaderboard",
        "Back to Main Menu",
        "Quit",
    ];

    public GameMenuScreen(
        GameEngine engine, string nodeId, Hero hero,
        SpriteFont titleFont, SpriteFont font)
    {
        _engine = engine;
        _nodeId = nodeId;
        _hero   = hero;
        _titleFont = titleFont;
        _font   = font;

        _menu = new MenuWidget(MenuItems);
        _menu.ItemConfirmed += OnMenuConfirmed;
    }

    private void OnMenuConfirmed(int index)
    {
        switch (MenuItems[index])
        {
            case "Explore Dungeon":
                GoTo(new DungeonScreen(_engine, _nodeId, _hero, _titleFont, _font));
                break;
            case "Rest at Inn":
                GoTo(new InnScreen(_engine, _nodeId, _hero, _titleFont, _font));
                break;
            case "View Stats":
                GoTo(new StatsScreen(_hero, _nodeId, _titleFont, _font));
                break;
            case "Battle History":
                GoTo(new BattleHistoryScreen(_engine, _nodeId, _titleFont, _font));
                break;
            case "Leaderboard":
                GoTo(new LeaderboardScreen(_engine, _nodeId, _titleFont, _font));
                break;
            case "Back to Main Menu":
                GoTo(new MainMenuScreen(_engine, _nodeId, _titleFont, _font));
                break;
            case "Quit":
                GoTo(null);
                break;
        }
    }

    public override void Update(GameTime gameTime) => _menu.Update();

    public override void Draw(SpriteBatch sb, GameTime gameTime)
    {
        var vp = sb.GraphicsDevice.Viewport;

        // --- Hero status bar ---
        string statusBar =
            $"{ClassSymbol.For(_hero.HeroClass)} {_hero.Name}  Lv.{_hero.Level} {_hero.HeroClass}   " +
            $"HP {_hero.Hp}/{_hero.MaxHp}   MP {_hero.Mp}/{_hero.MaxMp}   Gold {_hero.Gold}";
        sb.DrawString(_font, statusBar, new Vector2(20, 16), Color.Cyan);

        // --- Menu ---
        float y = 80;
        var titleSize = _titleFont.MeasureString("What do you do?");
        sb.DrawString(_titleFont, "What do you do?",
            new Vector2((vp.Width - titleSize.X) / 2f, y), new Color(255, 200, 0));

        _menu.Draw(sb, _font, new Vector2(vp.Width / 2f - 100, y + titleSize.Y + 20),
            Color.LightGray, new Color(255, 200, 0));
    }
}
