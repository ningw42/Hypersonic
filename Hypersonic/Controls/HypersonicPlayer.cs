using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Hypersonic.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Hypersonic.Controls;

public sealed class HypersonicPlayer : Control
{
    public DependencyProperty NowPlayingRecordingProperty = DependencyProperty.Register(
        nameof(NowPlayingRecording),
        typeof(IRecording),
        typeof(HypersonicPlayer),
        new PropertyMetadata(default(IRecording), new PropertyChangedCallback(OnNowPlayingRecordingChange)));

    public IRecording NowPlayingRecording
    {
        get => (IRecording)GetValue(this.NowPlayingRecordingProperty);
        set => SetValue(this.NowPlayingRecordingProperty, value);
    }

    public string Artist => this.NowPlayingRecording.Artist;

    public string Title => this.NowPlayingRecording.Title;

    public HypersonicPlayer()
    {
        this.DefaultStyleKey = typeof(HypersonicPlayer);
    }

    private static void OnNowPlayingRecordingChange(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        HypersonicPlayer labelControl = d as HypersonicPlayer;
        IRecording s = e.NewValue as IRecording;
    }
}
