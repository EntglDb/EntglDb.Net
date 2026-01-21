using EntglDb.Core.Storage;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace EntglDb.Test.Maui;

public partial class CollectionPage : ContentPage
{
    private readonly IPeerStore _store;
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

    public CollectionPage(IPeerStore store, string collectionName)
    {
        InitializeComponent();
        _store = store;
        CollectionName = collectionName;
        Title = collectionName; // Bindable but setting directly works well for Page Title
        RefreshCommand = new Command(async () => await LoadDocuments());
        BindingContext = this;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadDocuments();
    }

    private async Task LoadDocuments()
    {
        try
        {
            // Limit to 100 for now
            var docs = await _store.QueryDocumentsAsync(CollectionName, null, 0, 100, "Key", true);
            var count = await _store.CountDocumentsAsync(CollectionName, null);
            
            DocumentCount = count;
            Documents.Clear();
            foreach (var d in docs)
            {
                var jsonText = d.Content.ValueKind != System.Text.Json.JsonValueKind.Undefined ? d.Content.GetRawText() : "null";
                var shortText = jsonText.Trim().Replace("\n", "").Replace("\r", "");
                if (shortText.Length > 50) shortText = shortText.Substring(0, 50) + "...";
                
                Documents.Add(new DocumentViewModel
                {
                    Key = d.Key,
                    Timestamp = d.UpdatedAt.ToString(),
                    Payload = jsonText,
                    ShortPayload = shortText
                });
            }
        }
        catch (Exception ex)
        {
           await DisplayAlert("Error", $"Failed to load documents: {ex.Message}", "OK");
        }
        finally
        {
            DocsRefreshView.IsRefreshing = false;
        }
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
