using EntglDb.Core.Network;
using EntglDb.Network;
using Microsoft.Extensions.Hosting;
using Spectre.Console;

namespace EntglDb.Demo.Game;

/// <summary>
/// UI layer: reads player input via Spectre.Console, delegates all game logic
/// to <see cref="GameEngine"/>, and renders the results back to the terminal.
/// </summary>
public class GameService : BackgroundService
{
    private readonly GameDbContext _db;
    private readonly IEntglDbNode _node;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IPeerNodeConfigurationProvider _configProvider;
    private readonly Random _rng = new();

    private Hero? _currentHero;
    private string _nodeId = "";
    private GameEngine _engine = null!;

    public GameService(
        GameDbContext db,
        IEntglDbNode node,
        IHostApplicationLifetime lifetime,
        IPeerNodeConfigurationProvider configProvider)
    {
        _db = db;
        _node = node;
        _lifetime = lifetime;
        _configProvider = configProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = await _configProvider.GetConfiguration();
        _nodeId = config.NodeId;
        _engine = new GameEngine(_db, _nodeId, _rng);

        PrintBanner();
        await MainLoop(stoppingToken);
    }

    private void PrintBanner()
    {
        AnsiConsole.Write(new FigletText("Dungeon Crawler")
            .Centered()
            .Color(Color.Gold1));

        AnsiConsole.Write(new Panel(
                new Markup($"[grey]Node:[/] [cyan]{_nodeId}[/]\n[grey]Heroes sync across all connected peers![/]"))
        {
            Header = new PanelHeader(" EntglDb P2P RPG Demo ", Justify.Center),
            Border = BoxBorder.Double,
            BorderStyle = new Style(Color.DarkOrange),
            Padding = new Padding(2, 1),
        });
        AnsiConsole.WriteLine();
    }

    private async Task MainLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (_currentHero == null)
                await RunMainMenu(ct);
            else
                await RunGameMenu(ct);
        }
    }

    private async Task RunMainMenu(CancellationToken ct)
    {
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[bold yellow]Main Menu[/]")
                .HighlightStyle(new Style(Color.Gold1))
                .AddChoices("New Hero", "Load Hero", "Leaderboard", "Network Peers", "Quit"));

        switch (choice)
        {
            case "New Hero":    await CreateHero(ct); break;
            case "Load Hero":   LoadHero(); break;
            case "Leaderboard": ShowLeaderboard(); break;
            case "Network Peers": ShowPeers(); break;
            case "Quit":
                _lifetime.StopApplication();
                break;
        }
    }

    private async Task RunGameMenu(CancellationToken ct)
    {
        var h = _currentHero!;
        double hpPct = (double)h.Hp / h.MaxHp;
        var hpColor = hpPct > 0.6 ? "green" : hpPct > 0.3 ? "yellow" : "red";
        double mpPct = (double)h.Mp / h.MaxMp;
        var mpColor = mpPct > 0.6 ? "blue" : mpPct > 0.3 ? "cyan" : "grey";
        var classEmoji = HeroClassFactory.Profiles[h.HeroClass].Emoji;

        AnsiConsole.Write(new Rule(
            $"{classEmoji} [bold]{Markup.Escape(h.Name)}[/] [grey]Lv.{h.Level} {h.HeroClass}[/]  [{hpColor}]HP {h.Hp}/{h.MaxHp}[/]  [{mpColor}]MP {h.Mp}/{h.MaxMp}[/]  [gold1]Gold {h.Gold}[/]")
        {
            Justification = Justify.Center,
            Style = Style.Parse("cyan"),
        });

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .HighlightStyle(new Style(Color.Gold1))
                .AddChoices(
                    "Explore Dungeon",
                    "Rest at Inn",
                    "View Stats",
                    "Battle History",
                    "Leaderboard",
                    "Network Peers",
                    "Back to Main Menu",
                    "Quit"));

        switch (choice)
        {
            case "Explore Dungeon":   await ExploreDungeon(ct); break;
            case "Rest at Inn":       await RestAtInn(ct); break;
            case "View Stats":        ShowStats(); break;
            case "Battle History":    ShowBattleHistory(); break;
            case "Leaderboard":       ShowLeaderboard(); break;
            case "Network Peers":     ShowPeers(); break;
            case "Back to Main Menu": _currentHero = null; break;
            case "Quit":
                _lifetime.StopApplication();
                break;
        }
    }

    private async Task CreateHero(CancellationToken ct)
    {
        var name = AnsiConsole.Prompt(
            new TextPrompt<string>("[bold yellow]Enter hero name:[/]")
                .Validate(n => !string.IsNullOrWhiteSpace(n)
                    ? ValidationResult.Success()
                    : ValidationResult.Error("Name cannot be empty")));

        // Build class choices with emoji + description
        var classChoices = HeroClassFactory.Profiles
            .Select(kvp => $"{kvp.Value.Emoji} {kvp.Key,-8} — {kvp.Value.Description}")
            .ToArray();

        var classKeys = HeroClassFactory.Profiles.Keys.ToArray();

        // Show stat ranges per class
        var rangeTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.DarkOrange)
            .Title("[bold orange1]Class Stat Ranges[/]")
            .AddColumn("Class")
            .AddColumn("HP")
            .AddColumn("ATK")
            .AddColumn("DEF")
            .AddColumn("MP")
            .AddColumn("MATK");
        foreach (var (cls, p) in HeroClassFactory.Profiles)
            rangeTable.AddRow(
                $"{p.Emoji} [bold]{cls}[/]",
                $"{p.MinHp}-{p.MaxHp}",
                $"[red]{p.MinAttack}-{p.MaxAttack}[/]",
                $"[blue]{p.MinDefense}-{p.MaxDefense}[/]",
                $"[cyan]{p.MinMp}-{p.MaxMp}[/]",
                $"[magenta]{p.MinMagicAttack}-{p.MaxMagicAttack}[/]");
        AnsiConsole.Write(rangeTable);
        AnsiConsole.WriteLine();

        var classChoice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[bold yellow]Choose your class:[/]")
                .HighlightStyle(new Style(Color.Gold1))
                .AddChoices(classChoices));

        int classIdx = Array.IndexOf(classChoices, classChoice);
        var heroClass = classKeys[classIdx];

        var hero = await _engine.CreateHeroAsync(name, heroClass, ct);

        var p2 = HeroClassFactory.Profiles[heroClass];
        AnsiConsole.Write(new Panel(
            new Markup(
                $"{p2.Emoji} [bold]{heroClass}[/] — {p2.Description}\n" +
                $"[green]HP {hero.MaxHp}[/]   [red]ATK {hero.Attack}[/]   [blue]DEF {hero.Defense}[/]   [cyan]MP {hero.MaxMp}[/]   [magenta]MATK {hero.MagicAttack}[/]"))
        {
            Header = new PanelHeader(" Stats rolled! ", Justify.Center),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.DarkOrange),
            Padding = new Padding(2, 1),
        });

        _currentHero = hero;
        AnsiConsole.MarkupLine($"\n[green]✓ Hero '[bold]{Markup.Escape(name)}[/]' created! Ready for adventure.[/]\n");
    }

    private void LoadHero()
    {
        var heroes = _engine.GetAliveHeroes().ToList();
        if (heroes.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No heroes found. Create one first![/]");
            return;
        }

        var choices = heroes
            .Select(h =>
            {
                var origin = h.NodeId == _nodeId ? "" : $" ({h.NodeId})";
                var emoji = HeroClassFactory.Profiles[h.HeroClass].Emoji;
                return $"{emoji} {h.Name} [{h.HeroClass}] — Lv.{h.Level}  HP:{h.Hp}/{h.MaxHp}  Gold:{h.Gold}{origin}";
            })
            .Append("Cancel")
            .ToArray();

        var selected = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[bold yellow]Select Hero[/]")
                .HighlightStyle(new Style(Color.Gold1))
                .AddChoices(choices));

        if (selected == "Cancel") return;

        int idx = Array.IndexOf(choices, selected);
        _currentHero = heroes[idx];
        AnsiConsole.MarkupLine($"\n[green]✓ Playing as [bold]{Markup.Escape(_currentHero.Name)}[/]![/]\n");
    }

    private async Task ExploreDungeon(CancellationToken ct)
    {
        var hero = _currentHero!;
        if (!hero.IsAlive)
        {
            AnsiConsole.MarkupLine("[red]💀 This hero has fallen. Create a new one.[/]");
            _currentHero = null;
            return;
        }

        if (_engine.IsChestEncounter())
        {
            var chestResult = await _engine.OpenChestAsync(hero, ct);
            RenderChestResult(chestResult);
            _currentHero = hero;
            AnsiConsole.WriteLine();
            return;
        }

        var monster = _engine.SpawnMonster(hero.Level);

        AnsiConsole.Write(new Panel(
            new Markup(
                $"[bold]{monster.Emoji} {Markup.Escape(monster.Name)}[/]\n" +
                $"[red]HP {monster.Hp}[/]   [yellow]ATK {monster.Attack}[/]   [blue]DEF {monster.Defense}[/]"))
        {
            Header = new PanelHeader(" A wild monster appears! ", Justify.Center),
            Border = BoxBorder.Heavy,
            BorderStyle = new Style(Color.Red),
            Padding = new Padding(2, 1),
        });
        AnsiConsole.WriteLine();
        await Task.Delay(400, ct);

        int monsterHp = monster.Hp;
        int round = 1;

        while (hero.Hp > 0 && monsterHp > 0)
        {
            // Round header with live HP
            double heroHpPct = (double)hero.Hp / hero.MaxHp;
            double monHpPct  = (double)monsterHp / monster.Hp;
            var heroHpColor  = heroHpPct > 0.5 ? "green" : heroHpPct > 0.25 ? "yellow" : "red";
            var monHpColor   = monHpPct  > 0.5 ? "red"   : monHpPct  > 0.25 ? "yellow" : "green";
            var heroMpColor  = hero.Mp >= GameEngine.SpellCost ? "blue" : "grey";
            AnsiConsole.MarkupLine(
                $"[grey]── Round {round} ──[/]  " +
                $"[{heroHpColor}]You: {hero.Hp}/{hero.MaxHp} HP[/]  [{heroMpColor}]MP {hero.Mp}/{hero.MaxMp}[/]   " +
                $"[{monHpColor}]{monster.Emoji} {Markup.Escape(monster.Name)}: {monsterHp}/{monster.Hp} HP[/]");

            var action = PromptPlayerAction(hero);
            var roundResult = _engine.ExecuteRound(hero, ref monsterHp, monster, action);
            RenderRoundResult(roundResult, monster);

            AnsiConsole.WriteLine();
            round++;
            await Task.Delay(200, ct);

            if (roundResult.MonsterDefeated || roundResult.HeroDefeated) break;
        }

        var outcome = await _engine.FinalizeBattleAsync(hero, monster, hero.Hp > 0, ct);
        RenderBattleOutcome(outcome, hero, monster);
        _currentHero = hero;
        AnsiConsole.WriteLine();
    }

    private static PlayerAction PromptPlayerAction(Hero hero)
    {
        var prompt = new SelectionPrompt<string>()
            .Title($"  [bold yellow]Choose your move:[/]  [blue]MP {hero.Mp}/{hero.MaxMp}[/]")
            .HighlightStyle(new Style(Color.Gold1))
            .AddChoices(
                "Attack        — Standard strike",
                "Quick Strike  — Two fast hits (lower damage each)",
                "Power Blow    — Heavy hit, you take 50% more damage",
                "Parry         — Counter for half, take 70% less damage",
                "Dodge         — 55% chance to fully evade, then strike");
        if (hero.Mp >= GameEngine.SpellCost)
            prompt.AddChoice($"Fireball      — Magic blast ({GameEngine.SpellCost} MP), ignores defense");

        var choice = AnsiConsole.Prompt(prompt);
        return choice.Split(' ')[0] switch
        {
            "Quick"    => PlayerAction.QuickStrike,
            "Power"    => PlayerAction.PowerBlow,
            "Parry"    => PlayerAction.Parry,
            "Dodge"    => PlayerAction.Dodge,
            "Fireball" => PlayerAction.Fireball,
            _          => PlayerAction.Attack,
        };
    }

    private static void RenderRoundResult(BattleRoundResult result, Monster monster)
    {
        string heroLine = result.Action switch
        {
            PlayerAction.QuickStrike =>
                $"[green]🗡  Quick Strike! [bold]{result.HeroHit1}[/] + [bold]{result.HeroHit2}[/] = [bold]{result.HeroDamage}[/] dmg[/]",
            PlayerAction.PowerBlow =>
                $"[green]💪  Power Blow! [bold]{result.HeroDamage}[/] dmg [dim](you\'re exposed!)[/][/]",
            PlayerAction.Parry =>
                $"[green]🛡  Parry & Counter! [bold]{result.HeroDamage}[/] dmg [dim](blocking)[/][/]",
            PlayerAction.Dodge =>
                $"[green]💨  Dodge & Strike! [bold]{result.HeroDamage}[/] dmg[/]",
            PlayerAction.Fireball =>
                $"[magenta]🔥  Fireball! [bold]{result.HeroDamage}[/] magic dmg [dim](-{result.MpSpent} MP)[/][/]",
            _ =>
                $"[green]⚔   Attack! [bold]{result.HeroDamage}[/] dmg[/]",
        };
        AnsiConsole.MarkupLine($"  {heroLine} [grey]({monster.Emoji} HP: {result.MonsterHpAfter})[/]");

        if (result.MonsterDefeated) return;

        if (result.DodgedSuccessfully)
        {
            AnsiConsole.MarkupLine($"  [cyan]💨  Dodge! {monster.Emoji} {Markup.Escape(monster.Name)} misses![/]");
        }
        else
        {
            string penaltyNote = result.MonsterDamagePercent == 150 ? " [red](Power Blow penalty!)[/]"
                               : result.MonsterDamagePercent == 30  ? " [blue](Partially blocked!)[/]"
                               : result.DodgeAttempt                ? " [yellow](Dodge failed!)[/]"
                               : "";
            AnsiConsole.MarkupLine(
                $"  [red]{monster.Emoji} {Markup.Escape(monster.Name)} hits for [bold]{result.MonsterDamage}[/]![/]{penaltyNote} " +
                $"[grey](Your HP: {Math.Max(0, result.HeroHpAfter)})[/]");
        }
    }

    private static void RenderBattleOutcome(BattleOutcome outcome, Hero hero, Monster monster)
    {
        if (outcome.Victory)
        {
            string mpLine = outcome.MpGained > 0 ? $"\n[blue]+{outcome.MpGained} MP[/]" : "";
            AnsiConsole.Write(new Panel(
                new Markup(
                    $"[bold green]🏆 Victory![/] You defeated [bold]{Markup.Escape(monster.Name)}[/]!\n" +
                    $"[cyan]+{outcome.XpGained} XP[/]   [gold1]+{outcome.GoldGained} Gold[/]{mpLine}"))
            {
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Green),
                Padding = new Padding(2, 1),
            });
        }
        else
        {
            AnsiConsole.Write(new Panel(
                new Markup($"[bold red]💀 {Markup.Escape(hero.Name)} has fallen to the {Markup.Escape(monster.Name)}...[/]"))
            {
                Border = BoxBorder.Heavy,
                BorderStyle = new Style(Color.Red),
                Padding = new Padding(2, 1),
            });
        }

        if (outcome.LevelUp != null)
            RenderLevelUp(outcome.LevelUp);
    }

    private static void RenderChestResult(ChestResult result)
    {
        var chestColor = result.Type switch
        {
            ChestType.Wooden => Color.SandyBrown,
            ChestType.Silver => Color.Silver,
            ChestType.Magic  => Color.Cyan1,
            _                => Color.Gold1,
        };

        string rewardLine = (result.GoldGained > 0 && result.XpGained > 0)
            ? $"[gold1]+{result.GoldGained} Gold[/]   [cyan]+{result.XpGained} XP[/]"
            : result.GoldGained > 0
            ? $"[gold1]+{result.GoldGained} Gold[/]"
            : $"[cyan]+{result.XpGained} XP[/]";

        AnsiConsole.Write(new Panel(new Markup($"[bold]{result.Name}[/]\n{rewardLine}"))
        {
            Header = new PanelHeader(" You found a chest! ", Justify.Center),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(chestColor),
            Padding = new Padding(2, 1),
        });

        if (result.LevelUp != null)
            RenderLevelUp(result.LevelUp);
    }

    private static void RenderLevelUp(LevelUpResult levelUp)
    {
        AnsiConsole.Write(new Panel(
            new Markup(
                $"[bold magenta]⭐ LEVEL UP!  →  Level {levelUp.NewLevel}[/]\n" +
                $"MaxHP [bold]{levelUp.MaxHp}[/]   ATK [bold]{levelUp.Attack}[/]   DEF [bold]{levelUp.Defense}[/]   MaxMP [bold]{levelUp.MaxMp}[/]   MATK [bold]{levelUp.MagicAttack}[/]"))
        {
            Border = BoxBorder.Double,
            BorderStyle = new Style(Color.Magenta1),
            Padding = new Padding(2, 0),
        });
    }

    private async Task RestAtInn(CancellationToken ct)
    {
        var hero = _currentHero!;
        var result = await _engine.RestAtInnAsync(hero, ct);

        if (!result.Rested)
        {
            if (result.FailReason == "already_full_hp")
                AnsiConsole.MarkupLine("[yellow]You're already at full health![/]");
            else
                AnsiConsole.MarkupLine($"[red]Not enough gold! Need {result.Cost}G (you have {hero.Gold}G)[/]");
            return;
        }

        string mpNote = result.MpRestored > 0
            ? $" [blue]+{result.MpRestored} MP[/] [grey](capped at 80% — kill monsters for more)[/]"
            : " [grey]MP already at 80%+ (kill monsters to reach max)[/]";
        AnsiConsole.MarkupLine($"[green]🏨 You rest at the inn. HP fully restored! (-{result.Cost}G)[/]{mpNote}");
    }

    private void ShowStats()
    {
        var h = _currentHero!;
        int xpNeeded = h.Level * 50;

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Cyan1)
            .Title("[bold cyan]Hero Stats[/]")
            .AddColumn(new TableColumn("[grey]Stat[/]"))
            .AddColumn(new TableColumn("[grey]Value[/]"));

        var cEmoji = HeroClassFactory.Profiles[h.HeroClass].Emoji;
        var cDesc  = HeroClassFactory.Profiles[h.HeroClass].Description;
        table.AddRow("Name",    $"[bold]{Markup.Escape(h.Name)}[/]");
        table.AddRow("Class",   $"{cEmoji} [bold orange1]{h.HeroClass}[/] [grey]— {cDesc}[/]");
        table.AddRow("Level",   $"[yellow]{h.Level}[/]");
        table.AddRow("HP",           $"[green]{h.Hp} / {h.MaxHp}[/]");
        table.AddRow("MP",           $"[blue]{h.Mp} / {h.MaxMp}[/] [grey](inn cap: {(int)(h.MaxMp * 0.8)})[/]");
        table.AddRow("Attack",       $"[red]{h.Attack}[/]");
        table.AddRow("Magic Attack", $"[magenta]{h.MagicAttack}[/]");
        table.AddRow("Defense",      $"[blue]{h.Defense}[/]");
        table.AddRow("Gold",    $"[gold1]{h.Gold}[/]");
        table.AddRow("XP",      $"[cyan]{h.Xp} / {xpNeeded}[/]");
        table.AddRow("Kills",   $"{h.MonstersKilled}");
        table.AddRow("Node",    $"[grey]{h.NodeId}[/]");
        table.AddRow("Status",  h.IsAlive ? "[green]Alive[/]" : "[red]Fallen[/]");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    private void ShowBattleHistory()
    {
        var logs = _engine.GetRecentBattles(10).ToList();

        if (logs.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No battles yet.[/]");
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Cyan1)
            .Title("[bold cyan]Recent Battles (all nodes)[/]")
            .AddColumn("Result")
            .AddColumn("Hero")
            .AddColumn("Monster")
            .AddColumn("Rewards")
            .AddColumn("Node");

        foreach (var log in logs)
        {
            var result  = log.Victory ? "[green]Victory[/]" : "[red]Defeat[/]";
            var rewards = log.Victory ? $"+{log.XpGained}XP  +{log.GoldGained}G" : "-";
            var node    = log.NodeId == _nodeId ? "[grey]local[/]" : $"[grey]{Markup.Escape(log.NodeId)}[/]";
            table.AddRow(result, Markup.Escape(log.HeroName), Markup.Escape(log.MonsterName), rewards, node);
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    private void ShowLeaderboard()
    {
        var heroes = _engine.GetLeaderboard(10).ToList();

        if (heroes.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No heroes yet.[/]");
            return;
        }

        var table = new Table()
            .Border(TableBorder.DoubleEdge)
            .BorderColor(Color.Gold1)
            .Title("[bold gold1]🏆 Leaderboard (synced across nodes)[/]")
            .AddColumn("#")
            .AddColumn("Hero")
            .AddColumn("Class")
            .AddColumn("Level")
            .AddColumn("Kills")
            .AddColumn("Gold")
            .AddColumn("Status")
            .AddColumn("Node");

        for (int i = 0; i < heroes.Count; i++)
        {
            var h      = heroes[i];
            var medal  = i switch { 0 => "🥇", 1 => "🥈", 2 => "🥉", _ => $"{i + 1}" };
            var status = h.IsAlive ? "[green]Alive[/]" : "[red]Fallen[/]";
            var node   = h.NodeId == _nodeId ? "[grey]local[/]" : $"[grey]{Markup.Escape(h.NodeId)}[/]";
            var lEmoji = HeroClassFactory.Profiles[h.HeroClass].Emoji;
            table.AddRow(medal, $"[bold]{Markup.Escape(h.Name)}[/]",
                $"{lEmoji} {h.HeroClass}", $"{h.Level}", $"{h.MonstersKilled}", $"{h.Gold}", status, node);
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    private void ShowPeers()
    {
        var peers = _node.Discovery.GetActivePeers().ToList();

        if (peers.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No peers connected. Run another node to sync![/]");
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Cyan1)
            .Title("[bold cyan]Connected Peers[/]")
            .AddColumn("Node ID")
            .AddColumn("Address");

        foreach (var p in peers)
            table.AddRow($"[bold]{Markup.Escape(p.NodeId)}[/]", $"{p.Address}");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }
}
