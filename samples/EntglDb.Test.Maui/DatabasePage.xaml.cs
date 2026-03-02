using EntglDb.Sample.Shared;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace EntglDb.Test.Maui;

public partial class DatabasePage : ContentPage
{
    private readonly SampleDbContext _db;
    public ObservableCollection<CollectionViewModel> Collections { get; } = new();
    public ICommand RefreshCommand { get; }

    public DatabasePage(SampleDbContext db)
    {
        InitializeComponent();
        _db = db;
        RefreshCommand = new Command(async () => await LoadCollections());
        BindingContext = this;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadCollections();
    }

    private Task LoadCollections()
    {
        try
        {
            Collections.Clear();
            Collections.Add(new CollectionViewModel { Name = "Users" });
            Collections.Add(new CollectionViewModel { Name = "TodoLists" });
        }
        catch (Exception ex)
        {
            _ = DisplayAlert("Error", $"Failed to load collections: {ex.Message}", "OK");
        }
        finally
        {
            CollectionsRefreshView.IsRefreshing = false;
        }
        return Task.CompletedTask;
    }

    private async void OnCollectionSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is CollectionViewModel selected)
        {
            CollectionsCollectionView.SelectedItem = null; // Deselect
            await Navigation.PushAsync(new CollectionPage(_db, selected.Name));
        }
    }
}

public class CollectionViewModel
{
    public string Name { get; set; }
}
