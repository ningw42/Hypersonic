using System.ComponentModel;

using Hypersonic.Core.Models;

namespace Hypersonic.Core.Tests.Models;

[TestClass]
public class PlaybackTrackTests
{
    [TestMethod]
    public void IsStarred_WhenChanged_RaisesPropertyChanged()
    {
        var track = new PlaybackTrack { Id = "s1", IsStarred = false };
        var raised = new List<string?>();
        ((INotifyPropertyChanged)track).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        track.IsStarred = true;

        CollectionAssert.Contains(raised, nameof(PlaybackTrack.IsStarred));
        Assert.AreEqual(1, raised.Count, "Only the IsStarred change should notify");
    }

    [TestMethod]
    public void IsStarred_SetToSameValue_DoesNotRaise()
    {
        var track = new PlaybackTrack { Id = "s1", IsStarred = true };
        var raised = new List<string?>();
        ((INotifyPropertyChanged)track).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        track.IsStarred = true;

        Assert.AreEqual(0, raised.Count, "Setting IsStarred to its current value must not notify");
    }

    [TestMethod]
    public void OtherProperty_WhenChanged_DoesNotRaise()
    {
        var track = new PlaybackTrack { Id = "s1" };
        var raised = new List<string?>();
        ((INotifyPropertyChanged)track).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        track.Title = "New Title";
        track.Album = "New Album";

        Assert.AreEqual(0, raised.Count, "Only IsStarred notifies; other properties stay plain auto-props");
    }
}
