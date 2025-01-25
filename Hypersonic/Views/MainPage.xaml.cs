using System.Diagnostics;
using Hypersonic.Client;
using Hypersonic.Client.Models.Responses;
using Hypersonic.Models;
using Hypersonic.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace Hypersonic.Views;

public sealed partial class MainPage : Page
{
    public MainViewModel ViewModel
    {
        get;
    }

    public ISubsonicClient SubsonicClient
    {
        get; set;
    }

    public MainPage()
    {
        ViewModel = App.GetService<MainViewModel>();
        InitializeComponent();

        this.SubsonicClient = new SubsonicClient(
            new HttpClient(),
            "https://navidrome.ningw.net/rest",
            "ning",
            "m6PdJPLMDhmHXvGsR6ZiRqJdwRV6r8",
            "1.16.1",
            "Hypersonic");
    }

    private async void AutoSuggestBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        var suitableItems = new List<SuggestionItem>();
        Search3Response search3Response = await this.SubsonicClient.Search3Async(sender.Text);

        if (search3Response.SearchResult3.Artists != null)
        {
            foreach (var artist in search3Response.SearchResult3.Artists)
            {
                var item = new SuggestionItem
                {
                    Text = artist.Name,
                    ImageUrl = artist.ArtistImageUrl,
                };
                Debug.WriteLine(item.ImageUrl);
                suitableItems.Add(item);
            }
        }

        sender.ItemsSource = suitableItems;
    }

    private void AutoSuggestBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {

    }

    private void AutoSuggestBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {

    }
}
