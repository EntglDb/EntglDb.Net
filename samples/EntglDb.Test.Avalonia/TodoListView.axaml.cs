using Avalonia.Controls;
using Avalonia.Interactivity;
using EntglDb.Core;
using EntglDb.Legacy;
using EntglDb.Sample.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EntglDb.Test.Avalonia;

public partial class TodoListView : UserControl
{
    private readonly IPeerDatabase _database;
    private readonly IPeerCollection<TodoList> _todoCollection;
    private TodoList? _selectedList;
    private List<TodoList> _allLists = new();

    public TodoListView()
    {
        InitializeComponent();
    }

    public TodoListView(IPeerDatabase database) : this()
    {
        _database = database;
        _todoCollection = _database.Collection<TodoList>();
        
        _ = LoadListsAsync();
    }

    private async Task LoadListsAsync()
    {
        try
        {
            var lists = await _todoCollection.Find(t => true);
            _allLists = lists.ToList();
            ListsBox.ItemsSource = _allLists.Select(l => $"{l.Name} ({l.Items.Count})").ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading lists: {ex.Message}");
        }
    }

    private void OnListSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (ListsBox.SelectedIndex < 0 || ListsBox.SelectedIndex >= _allLists.Count)
        {
            _selectedList = null;
            SelectedListTitle.Text = "Select a list";
            DeleteListButton.IsVisible = false;
            AddItemPanel.IsVisible = false;
            ItemsPanel.Children.Clear();
            return;
        }

        _selectedList = _allLists[ListsBox.SelectedIndex];
        SelectedListTitle.Text = _selectedList.Name;
        DeleteListButton.IsVisible = true;
        AddItemPanel.IsVisible = true;

        RenderItems();
    }

    private void RenderItems()
    {
        ItemsPanel.Children.Clear();

        if (_selectedList == null) return;

        foreach (var item in _selectedList.Items)
        {
            // Use global:: to avoid namespace conflict with EntglDb.Test.Avalonia
            var panel = new global::Avalonia.Controls.StackPanel 
            { 
                Orientation = global::Avalonia.Layout.Orientation.Horizontal, 
                Spacing = 10 
            };
            
            var checkbox = new global::Avalonia.Controls.CheckBox 
            { 
                Content = item.Task, 
                IsChecked = item.Completed,
                Tag = item 
            };
            checkbox.Click += OnItemCheckChanged;
            panel.Children.Add(checkbox);

            var delBtn = new global::Avalonia.Controls.Button 
            { 
                Content = "🗑", 
                Padding = new global::Avalonia.Thickness(5, 2),
                Tag = item
            };
            delBtn.Click += OnDeleteItemClicked;
            panel.Children.Add(delBtn);

            ItemsPanel.Children.Add(panel);
        }
    }

    private async void OnItemCheckChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is global::Avalonia.Controls.CheckBox cb && cb.Tag is TodoItem item && _selectedList != null)
        {
            item.Completed = cb.IsChecked ?? false;
            await _todoCollection.Put(_selectedList);
        }
    }

    private async void OnDeleteItemClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is global::Avalonia.Controls.Button btn && btn.Tag is TodoItem item && _selectedList != null)
        {
            _selectedList.Items.Remove(item);
            await _todoCollection.Put(_selectedList);
            RenderItems();
        }
    }

    private async void OnAddItemClicked(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NewItemTaskEntry.Text) || _selectedList == null) return;

        var newItem = new TodoItem { Task = NewItemTaskEntry.Text, Completed = false };
        _selectedList.Items.Add(newItem);
        
        await _todoCollection.Put(_selectedList);
        
        NewItemTaskEntry.Text = string.Empty;
        RenderItems();
    }

    private async void OnCreateListClicked(object? sender, RoutedEventArgs e)
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

    private async void OnDeleteListClicked(object? sender, RoutedEventArgs e)
    {
        if (_selectedList == null) return;

        await _todoCollection.Delete(_selectedList.Id);
        _selectedList = null;
        
        await LoadListsAsync();
        OnListSelected(null, null!);
    }
}
