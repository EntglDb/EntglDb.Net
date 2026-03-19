using EntglDb.Demo.Game.MonoGame.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EntglDb.Demo.Game.MonoGame.Screens;

/// <summary>
/// Screen for entering a new hero name and choosing a class.
/// </summary>
public sealed class CreateHeroScreen : GameScreen
{
    private enum Step { EnterName, ChooseClass, Done }

    private readonly GameEngine _engine;
    private readonly string _nodeId;
    private readonly SpriteFont _titleFont;
    private readonly SpriteFont _font;

    private Step _step = Step.EnterName;
    private readonly TextInputWidget _nameInput = new(24);
    private string _pendingName = "";
    private MenuWidget? _classMenu;
    private HeroClass[] _classKeys = [];

    public CreateHeroScreen(GameEngine engine, string nodeId, SpriteFont titleFont, SpriteFont font)
    {
        _engine = engine;
        _nodeId = nodeId;
        _titleFont = titleFont;
        _font = font;

        _nameInput.Confirmed += name =>
        {
            _pendingName = name.Trim();
            _classKeys = HeroClassFactory.Profiles.Keys.ToArray();
            _classMenu = new MenuWidget(
                _classKeys.Select(c => $"{ClassSymbol.For(c)} {c,-12} {HeroClassFactory.Profiles[c].Description}"));
            _classMenu.ItemConfirmed += OnClassConfirmed;
            _step = Step.ChooseClass;
        };
    }

    private void OnClassConfirmed(int index)
    {
        _step = Step.Done;
        var chosen = _classKeys[index];
        _ = CreateAndAdvanceAsync(chosen);
    }

    private async Task CreateAndAdvanceAsync(HeroClass cls)
    {
        var hero = await _engine.CreateHeroAsync(_pendingName, cls, CancellationToken.None);
        GoTo(new GameMenuScreen(_engine, _nodeId, hero, _titleFont, _font));
    }

    public override void Update(GameTime gameTime)
    {
        if (_step == Step.EnterName) _nameInput.Update(gameTime);
        else if (_step == Step.ChooseClass) _classMenu?.Update();
    }

    public override void Draw(SpriteBatch sb, GameTime gameTime)
    {
        var viewport = sb.GraphicsDevice.Viewport;
        float y = 60;

        DrawTitle(sb, "Create Hero", viewport, ref y);
        y += 20;

        if (_step == Step.EnterName)
        {
            sb.DrawString(_font, "Enter hero name:", new Vector2(80, y), Color.LightGray);
            y += 30;
            _nameInput.Draw(sb, _font, new Vector2(80, y), Color.White);
            y += 40;
            sb.DrawString(_font, "Press Enter to confirm.", new Vector2(80, y), Color.DimGray);
        }
        else if (_step == Step.ChooseClass && _classMenu != null)
        {
            sb.DrawString(_font, $"Name: {_pendingName}", new Vector2(80, y), Color.Cyan);
            y += 30;
            sb.DrawString(_font, "Choose your class:", new Vector2(80, y), Color.LightGray);
            y += 36;

            // Stat range table header
            sb.DrawString(_font, $"{"Class",-22} {"HP",-8} {"ATK",-8} {"DEF",-8} {"MP",-8} {"MATK",-8}",
                new Vector2(80, y), Color.DimGray);
            y += 24;

            foreach (var (cls, p) in HeroClassFactory.Profiles)
            {
                sb.DrawString(_font,
                    $"  {cls,-20} {p.MinHp}-{p.MaxHp,-5} {p.MinAttack}-{p.MaxAttack,-5} {p.MinDefense}-{p.MaxDefense,-5} {p.MinMp}-{p.MaxMp,-5} {p.MinMagicAttack}-{p.MaxMagicAttack}",
                    new Vector2(80, y), Color.Gray);
                y += 22;
            }
            y += 10;
            _classMenu.Draw(sb, _font, new Vector2(80, y), Color.LightGray, new Color(255, 200, 0));
        }
        else
        {
            sb.DrawString(_font, "Creating hero...", new Vector2(80, y), Color.Yellow);
        }
    }

    private void DrawTitle(SpriteBatch sb, string text, Viewport vp, ref float y)
    {
        var size = _titleFont.MeasureString(text);
        sb.DrawString(_titleFont, text, new Vector2((vp.Width - size.X) / 2f, y), new Color(255, 200, 0));
        y += size.Y + 10;
    }
}
