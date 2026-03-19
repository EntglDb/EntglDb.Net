using EntglDb.Demo.Game.MonoGame.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EntglDb.Demo.Game.MonoGame.Screens;

/// <summary>
/// Lists alive heroes and lets the player pick one to play.
/// </summary>
public sealed class HeroSelectScreen : GameScreen
{
    private readonly GameEngine _engine;
    private readonly string _nodeId;
    private readonly SpriteFont _titleFont;
    private readonly SpriteFont _font;
    private MenuWidget? _menu;
    private List<Hero> _heroes = [];

    public HeroSelectScreen(GameEngine engine, string nodeId, SpriteFont titleFont, SpriteFont font)
    {
        _engine = engine;
        _nodeId = nodeId;
        _titleFont = titleFont;
        _font = font;
    }

    public override void Enter()
    {
        _heroes = _engine.GetAliveHeroes().ToList();

        if (_heroes.Count == 0)
        {
            GoTo(new MainMenuScreen(_engine, _nodeId, _titleFont, _font));
            return;
        }

        var labels = _heroes
            .Select(h =>
            {
                var origin = h.NodeId == _nodeId ? "" : $" [{h.NodeId}]";
                return $"{ClassSymbol.For(h.HeroClass)} {h.Name} {h.HeroClass} Lv.{h.Level}  HP:{h.Hp}/{h.MaxHp}{origin}";
            })
            .Append("Cancel");

        _menu = new MenuWidget(labels);
        _menu.ItemConfirmed += OnHeroSelected;
    }

    private void OnHeroSelected(int index)
    {
        if (index >= _heroes.Count) // Cancel
        {
            GoTo(new MainMenuScreen(_engine, _nodeId, _titleFont, _font));
            return;
        }
        GoTo(new GameMenuScreen(_engine, _nodeId, _heroes[index], _titleFont, _font));
    }

    public override void Update(GameTime gameTime) => _menu?.Update();

    public override void Draw(SpriteBatch sb, GameTime gameTime)
    {
        if (_menu == null) return;
        var vp = sb.GraphicsDevice.Viewport;
        float y = 60;

        var titleSize = _titleFont.MeasureString("Select Hero");
        sb.DrawString(_titleFont, "Select Hero",
            new Vector2((vp.Width - titleSize.X) / 2f, y), new Color(255, 200, 0));
        y += titleSize.Y + 30;

        _menu.Draw(sb, _font, new Vector2(80, y), Color.LightGray, new Color(255, 200, 0));
    }
}
