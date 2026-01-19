using EntglDb.Core;
using EntglDb.Sample.Shared;

namespace EntglDb.Test.Maui;

public partial class ConflictDemoPage : ContentPage
{
    private readonly IPeerDatabase _database;

    public ConflictDemoPage(IPeerDatabase database)
    {
        InitializeComponent();
        _database = database;
    }

    private async void OnRunDemoClicked(object? sender, EventArgs e)
    {
        ((Button)sender!).IsEnabled = false;
        LogLabel.Text = "Running demo...\n";

        try
        {
            var todoCollection = _database.Collection<TodoList>();

            var list = new TodoList
            {
                Name = "Shopping Demo",
                Items = new List<TodoItem>
                {
                    new TodoItem { Task = "Buy milk", Completed = false },
                    new TodoItem { Task = "Buy bread", Completed = false }
                }
            };

            await todoCollection.Put(list);
            LogLabel.Text += $"✓ Created '{list.Name}'\n";
            await Task.Delay(100);

            var listA = await todoCollection.Get(list.Id);
            if (listA != null)
            {
                listA.Items[0].Completed = true;
                listA.Items.Add(new TodoItem { Task = "Buy eggs", Completed = false });
                await todoCollection.Put(listA);
                LogLabel.Text += "📝 Edit A: milk ✓, +eggs\n";
            }

            await Task.Delay(100);

            var listB = await todoCollection.Get(list.Id);
            if (listB != null)
            {
                listB.Items[1].Completed = true;
                listB.Items.Add(new TodoItem { Task = "Buy cheese", Completed = false });
                await todoCollection.Put(listB);
                LogLabel.Text += "📝 Edit B: bread ✓, +cheese\n\n";
            }

            await Task.Delay(200);

            var merged = await todoCollection.Get(list.Id);
            if (merged != null)
            {
                var resolver = Preferences.Default.Get("ConflictResolver", "Merge");
                LogLabel.Text += $"🔀 Result ({resolver}):\n";
                
                foreach (var item in merged.Items)
                {
                    var status = item.Completed ? "✓" : " ";
                    LogLabel.Text += $"  [{status}] {item.Task}\n";
                }

                LogLabel.Text += $"\n{merged.Items.Count} items total\n";
                if (resolver == "Merge")
                    LogLabel.Text += "✓ Both edits preserved (merged by id)";
                else
                    LogLabel.Text += "⚠ Last write wins (Edit B only)";
            }
        }
        catch (Exception ex)
        {
            LogLabel.Text += $"\nError: {ex.Message}";
        }
        finally
        {
            ((Button)sender!).IsEnabled = true;
        }
    }
}
