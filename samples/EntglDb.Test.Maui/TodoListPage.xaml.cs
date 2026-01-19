using EntglDb.Core;
using EntglDb.Sample.Shared;
using System.Collections.ObjectModel;

namespace EntglDb.Test.Maui;

public partial class TodoListPage : ContentPage
{
    private readonly IPeerDatabase _database;
    private readonly IPeerCollection<TodoList> _todoCollection;
    private TodoList? _selectedList;
    private ObservableCollection<string> _listNames = new();
    private List<TodoList> _allLists = new();

    public TodoListPage(IPeerDatabase database)
    {
        InitializeComponent();
        
        _database = database;
        _todoCollection = _database.Collection<TodoList>();
        
        ListsCollection.ItemsSource = _listNames;
        
        _ = LoadListsAsync();
    }

    private async Task LoadListsAsync()
    {
        try
        {
            var lists = await _todoCollection.Find(t => true);
            _allLists = lists.ToList();
            
            _listNames.Clear();
            foreach (var list in _allLists)
            {
                _listNames.Add($"{list.Name} ({list.Items.Count})");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to load lists: {ex.Message}", "OK");
        }
    }

    private void OnListSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.Count == 0)
        {
            _selectedList = null;
            SelectedListTitle.Text = "Select a list";
            DeleteListButton.IsVisible = false;
            AddItemPanel.IsVisible = false;
            ItemsPanel.Children.Clear();
            return;
        }

        var index = _listNames.IndexOf((string)e.CurrentSelection[0]);
        if (index >= 0 && index < _allLists.Count)
        {
            _selectedList = _allLists[index];
            SelectedListTitle.Text = _selectedList.Name;
            DeleteListButton.IsVisible = true;
            AddItemPanel.IsVisible = true;
            
            RenderItems();
        }
    }

    private void RenderItems()
    {
        ItemsPanel.Children.Clear();

        if (_selectedList == null) return;

        foreach (var item in _selectedList.Items)
        {
            var layout = new HorizontalStackLayout { Spacing = 10 };
            
            var checkbox = new CheckBox 
            { 
                IsChecked = item.Completed,
                VerticalOptions = LayoutOptions.Center
            };
            checkbox.CheckedChanged += async (s, e) =>
            {
                item.Completed = e.Value;
                await _todoCollection.Put(_selectedList);
            };
            layout.Children.Add(checkbox);

            var label = new Label 
            { 
                Text = item.Task,
                VerticalOptions = LayoutOptions.Center
            };
            layout.Children.Add(label);

            var deleteBtn = new Button 
            { 
                Text = "🗑",
                Padding = new Thickness(5, 2)
            };
            deleteBtn.Clicked += async (s, e) =>
            {
                _selectedList.Items.Remove(item);
                await _todoCollection.Put(_selectedList);
                RenderItems();
            };
            layout.Children.Add(deleteBtn);

            ItemsPanel.Children.Add(layout);
        }
    }

    private async void OnAddItemClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NewItemTaskEntry.Text) || _selectedList == null) return;

        var newItem = new TodoItem { Task = NewItemTaskEntry.Text, Completed = false };
        _selectedList.Items.Add(newItem);
        
        await _todoCollection.Put(_selectedList);
        
        NewItemTaskEntry.Text = string.Empty;
        RenderItems();
    }

    private async void OnCreateListClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NewListNameEntry.Text)) return;

        var newList = new TodoList 
        { 
            Name = NewListNameEntry.Text,
            Items = new List<TodoItem>()
        };

        await _todoCollection.Put(newList);
        
        NewListNameEntry.Text = string.Empty;
        await LoadListsAsync();
    }

    private async void OnDeleteListClicked(object? sender, EventArgs e)
    {
        if (_selectedList == null) return;

        bool confirm = await DisplayAlert("Confirm", $"Delete list '{_selectedList.Name}'?", "Yes", "No");
        if (!confirm) return;

        await _todoCollection.Delete(_selectedList.Id);
        _selectedList = null;
        
        await LoadListsAsync();
        ItemsPanel.Children.Clear();
        SelectedListTitle.Text = "Select a list";
        DeleteListButton.IsVisible = false;
        AddItemPanel.IsVisible = false;
    }
}
