using EntglDb.Demo.Game.MonoGame.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EntglDb.Demo.Game.MonoGame.Screens;

/// <summary>
/// Turn-based combat screen.
/// Each turn:
///   1. Displays round header (HP/MP bars).
///   2. Player selects an action via MenuWidget.
///   3. Calls GameEngine.ExecuteRound — gets BattleRoundResult.
///   4. Shows log entry for a brief pause, then next round.
/// After the loop, calls FinalizeBattleAsync and shows outcome.
/// </summary>
public sealed class DungeonScreen : GameScreen
{
    private enum Phase
    {
        ChooseAction,
        ShowResult,
        ShowOutcome,
        Done,
    }

    private const double ResultPauseSeconds = 1.8;

    private readonly GameEngine _engine;
    private readonly string _nodeId;
    private Hero _hero;
    private readonly SpriteFont _titleFont;
    private readonly SpriteFont _font;

    private Monster? _monster;
    private int _monsterHp;
    private int _round;

    private Phase _phase = Phase.ChooseAction;
    private MenuWidget? _actionMenu;
    private PlayerAction[] _actionKeys = [];

    private BattleRoundResult? _lastRoundResult;
    private BattleOutcome? _outcome;
    private double _pauseTimer;

    // Scrolling combat log (last N lines)
    private readonly List<string> _combatLog = [];
    private const int MaxLogLines = 8;

    public DungeonScreen(
        GameEngine engine, string nodeId, Hero hero,
        SpriteFont titleFont, SpriteFont font)
    {
        _engine = engine;
        _nodeId = nodeId;
        _hero   = hero;
        _titleFont = titleFont;
        _font   = font;
    }

    public override void Enter()
    {
        // 25% chest chance
        if (_engine.IsChestEncounter())
        {
            _ = OpenChestAndReturnAsync();
            return;
        }

        _monster   = _engine.SpawnMonster(_hero.Level);
        _monsterHp = _monster.Hp;
        _round     = 1;
        BuildActionMenu();
    }

    private async Task OpenChestAndReturnAsync()
    {
        var result = await _engine.OpenChestAsync(_hero, CancellationToken.None);
        GoTo(new ChestResultScreen(result, _hero, _nodeId, _titleFont, _font, _engine));
    }

    private void BuildActionMenu()
    {
        if (_monster == null) return;

        var items = new List<(PlayerAction Action, string Label)>
        {
            (PlayerAction.Attack,      "Attack        - Standard strike"),
            (PlayerAction.QuickStrike, "Quick Strike  - Two fast hits (lower damage)"),
            (PlayerAction.PowerBlow,   "Power Blow    - Heavy hit, you take 50% more damage"),
            (PlayerAction.Parry,       "Parry         - Counter for half, take 70% less damage"),
            (PlayerAction.Dodge,       "Dodge         - 55% chance to evade, then strike"),
        };
        if (_hero.Mp >= GameEngine.SpellCost)
            items.Add((PlayerAction.Fireball, $"Fireball      - Magic blast ({GameEngine.SpellCost} MP)"));

        _actionKeys = items.Select(i => i.Action).ToArray();
        _actionMenu = new MenuWidget(items.Select(i => i.Label));
        _actionMenu.ItemConfirmed += OnActionChosen;
    }

    private void OnActionChosen(int index)
    {
        if (_monster == null) return;

        var action = _actionKeys[index];
        _lastRoundResult = _engine.ExecuteRound(_hero, ref _monsterHp, _monster, action);

        AppendLog(RoundResultToString(_lastRoundResult, _monster));

        _phase = Phase.ShowResult;
        _pauseTimer = 0;
    }

    public override void Update(GameTime gameTime)
    {
        switch (_phase)
        {
            case Phase.ChooseAction:
                _actionMenu?.Update();
                break;

            case Phase.ShowResult:
                _pauseTimer += gameTime.ElapsedGameTime.TotalSeconds;
                if (_pauseTimer >= ResultPauseSeconds)
                {
                    if (_lastRoundResult!.MonsterDefeated || _lastRoundResult.HeroDefeated)
                    {
                        _phase = Phase.ShowOutcome;
                        _ = FinalizeAsync();
                    }
                    else
                    {
                        _round++;
                        BuildActionMenu();
                        _phase = Phase.ChooseAction;
                    }
                }
                break;

            case Phase.ShowOutcome:
                // Wait for async finalize; clicking Enter returns to menu
                break;

            case Phase.Done:
                // User pressed Enter in outcome screen — propagate upward
                break;
        }

        // Enter to dismiss outcome
        if (_phase == Phase.ShowOutcome && _outcome != null)
        {
            var keys = Microsoft.Xna.Framework.Input.Keyboard.GetState();
            if (keys.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Enter))
                GoTo(new GameMenuScreen(_engine, _nodeId, _hero, _titleFont, _font));
        }
    }

    private async Task FinalizeAsync()
    {
        _outcome = await _engine.FinalizeBattleAsync(
            _hero, _monster!, _hero.Hp > 0, CancellationToken.None);
        _phase = Phase.ShowOutcome;
    }

    public override void Draw(SpriteBatch sb, GameTime gameTime)
    {
        if (_monster == null) return;
        var vp = sb.GraphicsDevice.Viewport;
        float y = 16;

        // ── Status bar ────────────────────────────────────────────────────────
        DrawHeroBar(sb, ref y);
        DrawMonsterBar(sb, ref y);
        y += 16;

        // ── Round header ──────────────────────────────────────────────────────
        if (_phase == Phase.ChooseAction || _phase == Phase.ShowResult)
        {
            sb.DrawString(_font, $"--- Round {_round} ---", new Vector2(80, y), Color.DimGray);
            y += 28;
        }

        // ── Combat log ────────────────────────────────────────────────────────
        foreach (var line in _combatLog)
        {
            sb.DrawString(_font, line, new Vector2(80, y), Color.White);
            y += 22;
        }
        y += 10;

        // ── Action menu / outcome ─────────────────────────────────────────────
        switch (_phase)
        {
            case Phase.ChooseAction when _actionMenu != null:
                sb.DrawString(_font, "Choose your move:", new Vector2(80, y), Color.LightGray);
                y += 28;
                _actionMenu.Draw(sb, _font, new Vector2(100, y), Color.LightGray, new Color(255, 200, 0));
                break;

            case Phase.ShowResult:
                sb.DrawString(_font, "...", new Vector2(80, y), Color.DimGray);
                break;

            case Phase.ShowOutcome when _outcome != null:
                DrawOutcome(sb, y, vp);
                break;
        }
    }

    private void DrawHeroBar(SpriteBatch sb, ref float y)
    {
        var hpColor  = _hero.Hp > _hero.MaxHp * 0.5f ? Color.Green : _hero.Hp > _hero.MaxHp * 0.25f ? Color.Yellow : Color.Red;
        sb.DrawString(_font,
            $"{_hero.Name}  HP {_hero.Hp}/{_hero.MaxHp}  MP {_hero.Mp}/{_hero.MaxMp}",
            new Vector2(20, y), hpColor);
        y += 26;
    }

    private void DrawMonsterBar(SpriteBatch sb, ref float y)
    {
        if (_monster == null) return;
        float pct    = (float)_monsterHp / _monster.Hp;
        var hpColor  = pct > 0.5f ? Color.OrangeRed : pct > 0.25f ? Color.Yellow : Color.LimeGreen;
        sb.DrawString(_font,
            $"{_monster.Name}  HP {Math.Max(0, _monsterHp)}/{_monster.Hp}",
            new Vector2(20, y), hpColor);
        y += 26;
    }

    private void DrawOutcome(SpriteBatch sb, float y, Viewport vp)
    {
        if (_outcome == null) return;
        var color = _outcome.Victory ? Color.Gold : Color.Red;
        string msg = _outcome.Victory
            ? $"Victory!  +{_outcome.XpGained} XP  +{_outcome.GoldGained} Gold"
              + (_outcome.MpGained > 0 ? $"  +{_outcome.MpGained} MP" : "")
            : $"{_hero.Name} has fallen...";
        sb.DrawString(_titleFont, msg,
            new Vector2((vp.Width - _titleFont.MeasureString(msg).X) / 2f, y), color);

        if (_outcome.LevelUp != null)
        {
            y += 50;
            string lvMsg = $"LEVEL UP!  Level {_outcome.LevelUp.NewLevel}   MaxHP {_outcome.LevelUp.MaxHp}  ATK {_outcome.LevelUp.Attack}  DEF {_outcome.LevelUp.Defense}";
            sb.DrawString(_font, lvMsg, new Vector2(80, y), Color.Magenta);
        }

        sb.DrawString(_font, "Press Enter to continue.",
            new Vector2(80, vp.Height - 40), Color.DimGray);
    }

    private void AppendLog(string line)
    {
        _combatLog.Add(line);
        if (_combatLog.Count > MaxLogLines)
            _combatLog.RemoveAt(0);
    }

    private static string RoundResultToString(BattleRoundResult r, Monster monster)
    {
        string heroLine = r.Action switch
        {
            PlayerAction.QuickStrike => $"Quick Strike: {r.HeroHit1}+{r.HeroHit2}={r.HeroDamage} dmg",
            PlayerAction.PowerBlow   => $"Power Blow: {r.HeroDamage} dmg (exposed!)",
            PlayerAction.Parry       => $"Parry & Counter: {r.HeroDamage} dmg",
            PlayerAction.Dodge       => $"Dodge & Strike: {r.HeroDamage} dmg",
            PlayerAction.Fireball    => $"Fireball: {r.HeroDamage} magic dmg (-{r.MpSpent} MP)",
            _                        => $"Attack: {r.HeroDamage} dmg",
        };
        string monsterLine = r.MonsterDefeated
            ? $"{monster.Name} defeated!"
            : r.DodgedSuccessfully
            ? $"{monster.Name} misses!"
            : $"{monster.Name} hits for {r.MonsterDamage}";
        return $"{heroLine}   {monsterLine}";
    }
}
