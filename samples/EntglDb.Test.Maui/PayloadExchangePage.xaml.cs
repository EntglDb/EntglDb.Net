using EntglDb.Core;
using EntglDb.Legacy;
using System.Text.Json;

namespace EntglDb.Test.Maui;

public partial class PayloadExchangePage : ContentPage
{
    private readonly IPeerDatabase _database;

    public PayloadExchangePage(IPeerDatabase database)
    {
        InitializeComponent();
        _database = database;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        GenerateRandomPayload();
    }

    private void GenerateRandomPayload()
    {
        var random = new Random();
        var itemCount = random.Next(50, 100); // 50 to 100 items to make it substantial
        var items = new List<object>();

        for (int i = 0; i < itemCount; i++)
        {
            items.Add(new 
            {
                id = Guid.NewGuid().ToString(),
                name = $"Item-{i}-{Guid.NewGuid().ToString().Substring(0, 8)}",
                value = random.NextDouble() * 1000,
                category = random.Next(0, 2) == 0 ? "A" : "B",
                tags = new[] { "tag1", "tag2", $"random-{random.Next(100)}" },
                details = "Lorem ipsum dolor sit amet, consectetur adipiscing elit. " + Guid.NewGuid()
            });
        }

        var payload = new 
        {
            id = Guid.NewGuid().ToString(),
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            type = "LargeDataset",
            description = "Generated payload for compression testing",
            flags = new { active = true, verified = random.Next(0, 2) == 1 },
            items = items,
            metadata = new 
            {
                source = "MAUI Test App",
                version = "1.0",
                hash = Guid.NewGuid().ToString() // Ensure high entropy
            }
        };

        var options = new JsonSerializerOptions { WriteIndented = true };
        JsonEditor.Text = JsonSerializer.Serialize(payload, options);
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        var collection = CollectionEntry.Text?.Trim();
        var jsonText = JsonEditor.Text;

        if (string.IsNullOrWhiteSpace(collection))
        {
            await DisplayAlert("Error", "Please enter a collection name.", "OK");
            return;
        }

        if (string.IsNullOrWhiteSpace(jsonText))
        {
            await DisplayAlert("Error", "Please enter a JSON payload.", "OK");
            return;
        }

        try
        {
            // Validate JSON
            var jsonElement = JsonSerializer.Deserialize<JsonElement>(jsonText);
            
            // Extract ID if present, otherwise generate one? 
            // For now, let's assume the user puts an ID or we generate a random one if missing?
            // Actually Document constructor usually takes an ID.
            // Let's try to find an 'id' property.
            string id = Guid.NewGuid().ToString();

            if (jsonElement.TryGetProperty("id", out var idProp))
            {
                id = idProp.ToString();
            }
            else if (jsonElement.TryGetProperty("Id", out idProp))
            {
                id = idProp.ToString();
            }

            var collectionRef = _database.Collection(collection);
            
            // PeerCollection.Put handles HLC ticking and Oplog appending.
            // We pass the parsed JsonElement. PeerCollection.Put expects an object to serialize, 
            // but passing a JsonElement works because JsonSerializer handles it correctly.
            // We need to use Put(key, document)
            
            await collectionRef.Put(id, jsonElement);
            
            StatusLabel.Text = $"Saved document {id} to '{collection}' at {DateTime.Now.ToLongTimeString()}";
            StatusLabel.TextColor = Colors.Green;
        }
        catch (JsonException jex)
        {
             await DisplayAlert("JSON Error", $"Invalid JSON: {jex.Message}", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to save: {ex.Message}", "OK");
            StatusLabel.Text = $"Error: {ex.Message}";
            StatusLabel.TextColor = Colors.Red;
        }
    }
}
