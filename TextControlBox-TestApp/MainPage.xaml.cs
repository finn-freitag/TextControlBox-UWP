﻿using System;
using System.Diagnostics;
using System.Text;
using TextControlBox;
using TextControlBox.Helper;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace TextControlBox_TestApp
{
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            this.InitializeComponent();
            Window.Current.CoreWindow.KeyDown += CoreWindow_KeyDown;

            TextControlBox.FontSize = 20;
            Load();

            /*TextControlBox.Design = new TextControlBoxDesign
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 59, 46, 69)),
                CursorColor = Color.FromArgb(255, 255, 100, 255),
                LineHighlighterColor = Color.FromArgb(255, 79, 66, 89),
                LineNumberBackground = Color.FromArgb(255, 59, 46, 69),
                LineNumberColor = Color.FromArgb(255, 93, 2, 163),
                TextColor = Color.FromArgb(255, 144, 0, 255),
                SelectionColor = Color.FromArgb(100, 144, 0, 255)
            };*/
        }
        private void Load()
        {
            TextControlBox.SelectCodeLanguageById("CSharp");
        }
        private string GenerateContent()
        {
            int Limit = 10;
            StringBuilder sb = new StringBuilder();
            for (int i = 1; i < Limit; i++)
            {
                sb.Append("Line" + i + (i == Limit - 1 ? "" : "\n"));
            }
            return sb.ToString();
        }

        private async void CoreWindow_KeyDown(Windows.UI.Core.CoreWindow sender, Windows.UI.Core.KeyEventArgs args)
        {
            bool ControlKey = Window.Current.CoreWindow.GetKeyState(Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            if (ControlKey && args.VirtualKey == Windows.System.VirtualKey.R)
            {
                TextControlBox.LoadText(GenerateContent());
            }
            if (ControlKey && args.VirtualKey == Windows.System.VirtualKey.E)
            {
                Load();
            }
            if (ControlKey && args.VirtualKey == Windows.System.VirtualKey.D)
            {
                TextControlBox.RequestedTheme = TextControlBox.RequestedTheme == ElementTheme.Dark ? 
                    ElementTheme.Light : TextControlBox.RequestedTheme == ElementTheme.Default ? ElementTheme.Light : ElementTheme.Dark;
                
                //TextControlBox.DuplicateLine(TextControlBox.CurrentLineIndex);
            }
            if (ControlKey && args.VirtualKey == Windows.System.VirtualKey.L)
            {
                //TextControlBox.DuplicateLine(TextControlBox.CurrentLineIndex);
            }
            if (ControlKey && args.VirtualKey == Windows.System.VirtualKey.O)
            {
                FileOpenPicker openPicker = new FileOpenPicker();
                openPicker.ViewMode = PickerViewMode.Thumbnail;
                openPicker.SuggestedStartLocation = PickerLocationId.Desktop;
                openPicker.FileTypeFilter.Add("*");

                var file = await openPicker.PickSingleFileAsync();
                if(file != null)
                {
                    string text = await FileIO.ReadTextAsync(file);
                    TextControlBox.LoadText(text);
                }
            }
        }
    }
}
