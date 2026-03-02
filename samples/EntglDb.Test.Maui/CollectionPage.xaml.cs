using EntglDb.Sample.Shared;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Input;

namespace EntglDb.Test.Maui;

public partial class CollectionPage : ContentPage
{
    private readonly SampleDbContext _db;
    public string CollectionName { get; }
    
    public ObservableCollection<DocumentViewModel> Documents { get; } = new();
    public ICommand RefreshCommand { get; }

    private int _documentCount;
    public int DocumentCount
    {
        get => _documentCount;
        set
        {
            _documentCount = value;
            OnPropertyChanged();
        }
    }

    public CollectionPage(SampleDbContext db, string collectionName)
    {
        InitializeComponent();
        _db = db;
        CollectionName = collectionName;
        Title = collectionName;
        RefreshCommand = new Command(async () => await LoadDocuments());
        BindingContext = this;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadDocuments();
    }

    private Task LoadDocuments()
    {
        try
        {
            Documents.Clear();
            if (CollectionName == "Users")
            {
                var allItems = _db.Users.FindAll().ToList();
                DocumentCount = allItems.Count;
                foreach (var item in allItems.Take(100))
                {
                    var json = JsonSerializer.Serialize(item);
                    var shortText = json.Length > 50 ? json[..50] + "..." : json;
                    Documents.Add(new DocumentViewModel { Key = item.Id, Timestamp = "-", Payload = json, ShortPayload = shortText });
                }
            }
            else if (CollectionName == "TodoLists")
            {
                var allItems = _db.TodoLists.FindAll().ToList();
                DocumentCount = allItems.Count;
                foreach (var item in allItems.Take(100))
                {
                    var json = JsonSerializer.Serialize(item);
                    var shortText = json.Length > 50 ? json[..50] + "..." : json;
                    Documents.Add(new DocumentViewModel { Key = item.Id, Timestamp = "-", Payload = json, ShortPayload = shortText });
                }
            }
        }
        catch (Exception ex)
        {
            _ = DisplayAlert("Error", $"Failed to load documents: {ex.Message}", "OK");
        }
        finally
        {
            DocsRefreshView.IsRefreshing = false;
        }
        return Task.CompletedTask;
    }

    private async void OnDocumentSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is DocumentViewModel doc)
        {
            DocsCollectionView.SelectedItem = null;
            await Navigation.PushAsync(new DocumentDetailPage(doc));
        }
    }
}

public class DocumentViewModel
{
    public string Key { get; set; }
    public string Timestamp { get; set; }
    public string Payload { get; set; }
    public string ShortPayload { get; set; }
}
