namespace EntglDb.Test.Maui;

public partial class DocumentDetailPage : ContentPage
{
    public DocumentDetailPage(DocumentViewModel doc)
    {
        InitializeComponent();
        BindingContext = doc;
    }
}
