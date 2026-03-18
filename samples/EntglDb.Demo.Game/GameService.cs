using EntglDb.Core.Network;
using EntglDb.Network;
using Microsoft.Extensions.Hosting;
using Spectre.Console;

namespace EntglDb.Demo.Game;

public class GameService : BackgroundService
{
    private readonly GameDbContext _db;
    private readonly IEntglDbNode _node;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IPeerNodeConfigurationProvider _configProvider;
    private readonly Random _rng = new();

    private Hero? _currentHero;
    private string _nodeId = "";

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

        AnsiConsole.Write(new Rule(
            $"[bold]{Markup.Escape(h.Name)}[/] [grey]Lv.{h.Level}[/]  [{hpColor}]HP {h.Hp}/{h.MaxHp}[/]  [gold1]Gold {h.Gold}[/]")
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

        var hero = new Hero { Name = name, NodeId = _nodeId };

        await _db.Heroes.InsertAsync(hero);
        await _db.SaveChangesAsync(ct);

        _currentHero = hero;
        AnsiConsole.MarkupLine($"\n[green]✓ Hero '[bold]{Markup.Escape(name)}[/]' created! Ready for adventure.[/]\n");
    }

    private void LoadHero()
    {
        var heroes = _db.Heroes.FindAll().Where(h => h.IsAlive).ToList();
        if (heroes.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No heroes found. Create one first![/]");
            return;
        }

        var choices = heroes
            .Select(h =>
            {
                var origin = h.NodeId == _nodeId ? "" : $" ({h.NodeId})";
                return $"{h.Name} — Lv.{h.Level}  HP:{h.Hp}/{h.MaxHp}  Gold:{h.Gold}{origin}";
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

        // 25% chance to find a chest instead of a monster
        if (_rng.Next(100) < 25)
        {
            await OpenChest(hero, ct);
            return;
        }

        var monster = MonsterFactory.Spawn(hero.Level);

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
            AnsiConsole.MarkupLine(
                $"[grey]── Round {round} ──[/]  " +
                $"[{heroHpColor}]You: {hero.Hp}/{hero.MaxHp} HP[/]   " +
                $"[{monHpColor}]{monster.Emoji} {Markup.Escape(monster.Name)}: {monsterHp}/{monster.Hp} HP[/]");

            // Player chooses action
            var action = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("  [bold yellow]Choose your move:[/]")
                    .HighlightStyle(new Style(Color.Gold1))
                    .AddChoices(
                        "Attack        — Standard strike",
                        "Quick Strike  — Two fast hits (lower damage each)",
                        "Power Blow    — Heavy hit, you take 50% more damage",
                        "Parry         — Counter for half, take 70% less damage",
                        "Dodge         — 55% chance to fully evade, then strike"));

            // Resolve hero action based on first word
            int heroDmg;
            int monsterDmgMult = 100; // percentage of normal incoming damage
            bool dodgeAttempt = false;
            string heroLine;

            switch (action.Split(' ')[0])
            {
                case "Quick":
                    int h1 = Math.Max(1, (int)(hero.Attack * 0.6) - monster.Defense / 2 + _rng.Next(-2, 3));
                    int h2 = Math.Max(1, (int)(hero.Attack * 0.6) - monster.Defense / 2 + _rng.Next(-2, 3));
                    heroDmg = h1 + h2;
                    heroLine = $"[green]🗡  Quick Strike! [bold]{h1}[/] + [bold]{h2}[/] = [bold]{heroDmg}[/] dmg[/]";
                    break;
                case "Power":
                    heroDmg = Math.Max(1, (int)(hero.Attack * 1.7) - monster.Defense + _rng.Next(-2, 5));
                    monsterDmgMult = 150;
                    heroLine = $"[green]💪  Power Blow! [bold]{heroDmg}[/] dmg [dim](you're exposed!)[/][/]";
                    break;
                case "Parry":
                    heroDmg = Math.Max(1, hero.Attack / 2 - monster.Defense / 2 + _rng.Next(-1, 3));
                    monsterDmgMult = 30;
                    heroLine = $"[green]🛡  Parry & Counter! [bold]{heroDmg}[/] dmg [dim](blocking)[/][/]";
                    break;
                case "Dodge":
                    heroDmg = Math.Max(1, hero.Attack - monster.Defense + _rng.Next(-3, 4));
                    dodgeAttempt = true;
                    heroLine = $"[green]💨  Dodge & Strike! [bold]{heroDmg}[/] dmg[/]";
                    break;
                default: // Attack
                    heroDmg = Math.Max(1, hero.Attack - monster.Defense + _rng.Next(-3, 4));
                    heroLine = $"[green]⚔   Attack! [bold]{heroDmg}[/] dmg[/]";
                    break;
            }

            monsterHp -= heroDmg;
            AnsiConsole.MarkupLine($"  {heroLine} [grey]({monster.Emoji} HP: {Math.Max(0, monsterHp)})[/]");

            if (monsterHp <= 0) break;

            // Monster attacks
            int baseDmg = Math.Max(1, monster.Attack - hero.Defense + _rng.Next(-3, 4));

            if (dodgeAttempt && _rng.Next(100) < 55)
            {
                AnsiConsole.MarkupLine($"  [cyan]💨  Dodge! {monster.Emoji} {Markup.Escape(monster.Name)} misses![/]");
            }
            else
            {
                int incoming = dodgeAttempt ? baseDmg : Math.Max(1, (int)(baseDmg * monsterDmgMult / 100.0));
                hero.Hp -= incoming;
                string penaltyNote = monsterDmgMult == 150 ? " [red](Power Blow penalty!)[/]"
                                   : monsterDmgMult == 30  ? " [blue](Partially blocked!)[/]"
                                   : dodgeAttempt          ? " [yellow](Dodge failed!)[/]"
                                   : "";
                AnsiConsole.MarkupLine(
                    $"  [red]{monster.Emoji} {Markup.Escape(monster.Name)} hits for [bold]{incoming}[/]![/]{penaltyNote} " +
                    $"[grey](Your HP: {Math.Max(0, hero.Hp)})[/]");
            }

            AnsiConsole.WriteLine();
            round++;
            await Task.Delay(200, ct);
        }

        var battleLog = new BattleLog
        {
            HeroId = hero.Id,
            HeroName = hero.Name,
            MonsterName = monster.Name,
            NodeId = _nodeId,
        };

        if (hero.Hp > 0)
        {
            hero.Xp += monster.XpReward;
            hero.Gold += monster.GoldReward;
            hero.MonstersKilled++;
            battleLog.Victory = true;
            battleLog.XpGained = monster.XpReward;
            battleLog.GoldGained = monster.GoldReward;

            AnsiConsole.Write(new Panel(
                new Markup(
                    $"[bold green]🏆 Victory![/] You defeated [bold]{Markup.Escape(monster.Name)}[/]!\n" +
                    $"[cyan]+{monster.XpReward} XP[/]   [gold1]+{monster.GoldReward} Gold[/]"))
            {
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Green),
                Padding = new Padding(2, 1),
            });
            TryLevelUp(hero);
        }
        else
        {
            hero.Hp = 0;
            hero.IsAlive = false;
            battleLog.Victory = false;

            AnsiConsole.Write(new Panel(
                new Markup($"[bold red]💀 {Markup.Escape(hero.Name)} has fallen to the {Markup.Escape(monster.Name)}...[/]"))
            {
                Border = BoxBorder.Heavy,
                BorderStyle = new Style(Color.Red),
                Padding = new Padding(2, 1),
            });
        }

        await _db.Heroes.UpdateAsync(hero);
        await _db.BattleLogs.InsertAsync(battleLog);
        await _db.SaveChangesAsync(ct);
        _currentHero = hero;
        AnsiConsole.WriteLine();
    }

    private async Task OpenChest(Hero hero, CancellationToken ct)
    {
        // Weighted chest types: 40% wooden, 35% silver, 20% magic, 5% golden
        int roll = _rng.Next(100);
        string chestName;
        Color chestColor;
        int goldGain;
        int xpGain;

        if (roll < 40)
        {
            chestName = "📦 Wooden Chest"; chestColor = Color.SandyBrown;
            goldGain = _rng.Next(8, 26); xpGain = 0;
        }
        else if (roll < 75)
        {
            chestName = "🪙 Silver Chest"; chestColor = Color.Silver;
            goldGain = _rng.Next(20, 55); xpGain = 0;
        }
        else if (roll < 95)
        {
            chestName = "✨ Magic Chest"; chestColor = Color.Cyan1;
            goldGain = 0; xpGain = _rng.Next(20, 60);
        }
        else
        {
            chestName = "🏆 Golden Chest"; chestColor = Color.Gold1;
            goldGain = _rng.Next(60, 150); xpGain = _rng.Next(40, 80);
        }

        string rewardLine = (goldGain > 0 && xpGain > 0) ? $"[gold1]+{goldGain} Gold[/]   [cyan]+{xpGain} XP[/]"
                          : goldGain > 0                  ? $"[gold1]+{goldGain} Gold[/]"
                                                          : $"[cyan]+{xpGain} XP[/]";

        AnsiConsole.Write(new Panel(new Markup($"[bold]{chestName}[/]\n{rewardLine}"))
        {
            Header = new PanelHeader(" You found a chest! ", Justify.Center),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(chestColor),
            Padding = new Padding(2, 1),
        });

        hero.Gold += goldGain;
        hero.Xp   += xpGain;
        TryLevelUp(hero);

        await _db.Heroes.UpdateAsync(hero);
        await _db.SaveChangesAsync(ct);
        _currentHero = hero;
        AnsiConsole.WriteLine();
    }

    private void TryLevelUp(Hero hero)
    {
        int xpNeeded = hero.Level * 50;
        if (hero.Xp < xpNeeded) return;

        hero.Level++;
        hero.Xp    -= xpNeeded;
        hero.MaxHp += 15;
        hero.Hp     = hero.MaxHp;
        hero.Attack += 3;
        hero.Defense += 2;

        AnsiConsole.Write(new Panel(
            new Markup(
                $"[bold magenta]⭐ LEVEL UP!  →  Level {hero.Level}[/]\n" +
                $"MaxHP [bold]{hero.MaxHp}[/]   ATK [bold]{hero.Attack}[/]   DEF [bold]{hero.Defense}[/]"))
        {
            Border = BoxBorder.Double,
            BorderStyle = new Style(Color.Magenta1),
            Padding = new Padding(2, 0),
        });
    }

    private async Task RestAtInn(CancellationToken ct)
    {
        var hero = _currentHero!;
        int cost = hero.Level * 10;

        if (hero.Hp >= hero.MaxHp)
        {
            AnsiConsole.MarkupLine("[yellow]You're already at full health![/]");
            return;
        }
        if (hero.Gold < cost)
        {
            AnsiConsole.MarkupLine($"[red]Not enough gold! Need {cost}G (you have {hero.Gold}G)[/]");
            return;
        }

        hero.Gold -= cost;
        hero.Hp = hero.MaxHp;
        await _db.Heroes.UpdateAsync(hero);
        await _db.SaveChangesAsync(ct);

        AnsiConsole.MarkupLine($"[green]🏨 You rest at the inn. HP fully restored! (-{cost}G)[/]");
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

        table.AddRow("Name",    $"[bold]{Markup.Escape(h.Name)}[/]");
        table.AddRow("Level",   $"[yellow]{h.Level}[/]");
        table.AddRow("HP",      $"[green]{h.Hp} / {h.MaxHp}[/]");
        table.AddRow("Attack",  $"[red]{h.Attack}[/]");
        table.AddRow("Defense", $"[blue]{h.Defense}[/]");
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
        var logs = _db.BattleLogs.FindAll()
            .OrderByDescending(b => b.Timestamp)
            .Take(10)
            .ToList();

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
        var heroes = _db.Heroes.FindAll()
            .OrderByDescending(h => h.Level)
            .ThenByDescending(h => h.MonstersKilled)
            .Take(10)
            .ToList();

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
            table.AddRow(medal, $"[bold]{Markup.Escape(h.Name)}[/]",
                $"{h.Level}", $"{h.MonstersKilled}", $"{h.Gold}", status, node);
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
