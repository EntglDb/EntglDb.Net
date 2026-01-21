using EntglDb.Core.Storage;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace EntglDb.Test.Maui;

public partial class DatabasePage : ContentPage
{
    private readonly IPeerStore _store;
    public ObservableCollection<CollectionViewModel> Collections { get; } = new();
    public ICommand RefreshCommand { get; }

    public DatabasePage(IPeerStore store)
    {
        InitializeComponent();
        _store = store;
        RefreshCommand = new Command(async () => await LoadCollections());
        BindingContext = this;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadCollections();
    }

    private async Task LoadCollections()
    {
        try
        {
            var cols = await _store.GetCollectionsAsync();
            Collections.Clear();
            foreach (var c in cols)
            {
                Collections.Add(new CollectionViewModel { Name = c });
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to load collections: {ex.Message}", "OK");
        }
        finally
        {
            CollectionsRefreshView.IsRefreshing = false;
        }
    }

    private async void OnCollectionSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is CollectionViewModel selected)
        {
            CollectionsCollectionView.SelectedItem = null; // Deselect
            await Navigation.PushAsync(new CollectionPage(_store, selected.Name));
        }
    }
}

public class CollectionViewModel
{
    public string Name { get; set; }
}
