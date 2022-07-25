﻿using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TextControlBox_TestApp.TextControlBox.Helper;
using TextControlBox_TestApp.TextControlBox.Languages;
using TextControlBox_TestApp.TextControlBox.Renderer;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.ViewManagement;
using Windows.UI.ViewManagement.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Shapes;
using Color = Windows.UI.Color;
using Size = Windows.Foundation.Size;

namespace TextControlBox_TestApp.TextControlBox
{
    public partial class TextControlBox : UserControl
    {
        private string NewLineCharacter = "\r\n";
        private InputPane inputPane;
        private bool _ShowLineNumbers = true;
        private CodeLanguage _CodeLanguage = null;
        private CodeLanguages _CodeLanguages = CodeLanguages.None;
        private LineEnding _LineEnding = LineEnding.CRLF;
        private ObservableCollection<TextHighlight> _TextHighlights = new ObservableCollection<TextHighlight>();

        //Colors:
        CanvasSolidColorBrush TextColorBrush;
        CanvasSolidColorBrush SelectionTextBrush;
        CanvasSolidColorBrush CursorColorBrush;
        CanvasSolidColorBrush LineNumberColorBrush;

        bool ColorResourcesCreated = false;
        bool NeedsTextFormatUpdate = false;

        CanvasTextFormat TextFormat = null;
        CanvasTextLayout DrawnTextLayout = null;
        CanvasTextFormat LineNumberTextFormat = null;

        string _CurrentText = "";
        string RenderedText = "";
        string LineNumberTextToRender = "";

        //The line, which will be rendered first
        int NumberOfStartLine = 0;
        int NumberOfUnrenderedLinesToRenderStart = 0;

        //The space left for the linenumbers
        private float RenderingOffsetLeft
        {
            get
            {
                return (float)(-HorizontalScrollbar.Value) + (float)Canvas_LineNumber.Width;
            }
        }

        //Handle double and triple -Clicks:
        int PointerClickCount = 0;
        DispatcherTimer PointerClickTimer = new DispatcherTimer { Interval = new TimeSpan(0, 0, 0, 0, 200) };

        //FontSize and Zoom
        float MaxFontsize = 100;
        float MinFontSize = 3;

        //Charsize Gets Updated everytime fontsize changes
        float CharWidth = 0;
        float SingleLineHeight = 0;

        //CursorPosition
        CursorPosition _CursorPosition = new CursorPosition(0, 0);
        Line CurrentLine = null;
        CanvasTextLayout CurrentLineTextLayout = null;
        TextSelection TextSelection = null;

        //Store the lines in Lists
        private List<Line> TotalLines = new List<Line>();
        private List<Line> RenderedLines = new List<Line>();

        //Classes
        private readonly SelectionRenderer selectionrenderer;
        private readonly UndoRedo UndoRedo = new UndoRedo();

        public TextControlBox()
        {
            this.InitializeComponent();
            //Classes & Variables:
            selectionrenderer = new SelectionRenderer(SelectionColor);
            inputPane = InputPane.GetForCurrentView();

            //Events:
            Window.Current.CoreWindow.KeyDown += CoreWindow_KeyDown;
            Window.Current.CoreWindow.CharacterReceived += CoreWindow_CharacterReceived;
            _TextHighlights.CollectionChanged += _TextHighlights_CollectionChanged; ;
            InitialiseOnStart();
        }

        private void InitialiseOnStart()
        {
            if(TotalLines.Count == 0)
                AddNewLine();
        }

        //Assing the colors to the CanvasSolidColorBrush
        private void CreateColorResources(ICanvasResourceCreatorWithDpi resourceCreator)
        {
            if (ColorResourcesCreated)
                return;

            TextColorBrush = new CanvasSolidColorBrush(resourceCreator, TextColor);
            SelectionTextBrush = new CanvasSolidColorBrush(resourceCreator, SelectionColor);
            CursorColorBrush = new CanvasSolidColorBrush(resourceCreator, CursorColor);
            LineNumberColorBrush = new CanvasSolidColorBrush(resourceCreator, LineNumberColor);
            ColorResourcesCreated = true;
        }

        private void CheckFontSize()
        {
            if (FontSize < MinFontSize)
                FontSize = MinFontSize;
            if (FontSize > MaxFontsize)
                FontSize = MaxFontsize;
        }
        private void UpdateCursor()
        {
            Canvas_Cursor.Invalidate();
        }
        private void UpdateText()
        {
            ChangeCursor(CoreCursorType.IBeam);
            Canvas_Text.Invalidate();
        }
        private void UpdateSelection()
        {
            Canvas_Selection.Invalidate();
        }
        private void UpdateCharSize()
        {
            Size charsize = Utils.MeasureTextSize(CanvasDevice.GetSharedDevice(), "M", TextFormat);
            CharWidth = (float)(charsize.Width * 1.1f);
            SingleLineHeight = TextFormat.LineSpacing;
        }
        private void UpdateCurrentLineTextLayout()
        {
            CurrentLineTextLayout = CreateTextLayoutForLine(Canvas_Text, CursorPosition.LineNumber - 1);
        }

        private int GetLineContentWidth(Line line)
        {
            if (line == null || line.Content == null)
                return -1;
            return line.Content.Length;
        }
        private Line GetCurrentLine()
        {
            return GetLineFromIndex(CursorPosition.LineNumber - 1);
        }
        private Line GetLineFromIndex(int Index)
        {
            if (IndexIsInLines(Index))
                return TotalLines[Index];
            return null;
        }
        private bool IndexIsInLines(int Value)
        {
            return Value < TotalLines.Count && Value > -1;
        }
        private void RemoveLine(int Index)
        {
            if (Index < TotalLines.Count && Index > 0)
                TotalLines.RemoveAt(Index);
        }

        private void AddCharacter(string text)
        {
            if (CurrentLine == null)
                return;

            var SplittedText = text.Split(NewLineCharacter);

            //Nothing is selected
            if (TextSelection == null && SplittedText.Length == 1)
            {
                UndoRedo.RecordSingleLineUndo(CurrentLine, CursorPosition);

                if (CursorPosition.CharacterPosition > GetLineContentWidth(CurrentLine) - 1)
                    CurrentLine.AddToEnd(text);
                else
                    CurrentLine.AddText(text, CursorPosition.CharacterPosition);
                CursorPosition.CharacterPosition += text.Length;
            }
            else if (TextSelection == null && SplittedText.Length > 1)
            {
                int ItemRangeCount = TotalLines.Count > CursorPosition.LineNumber ? 2 : 1;

                UndoRedo.RecordMultiLineUndo(CursorPosition.LineNumber, TotalLines.GetRange(CursorPosition.LineNumber-1, 1), SplittedText.Length);
                Selection.InsertText(TextSelection, CursorPosition, TotalLines, text, NewLineCharacter);
            }
            else
            {
                List<Line> Lines = Selection.GetSelectedTextLines(TotalLines, TextSelection, NewLineCharacter);

                UndoRedo.RecordMultiLineUndo(CursorPosition.LineNumber, Lines, SplittedText.Length);
                CursorPosition = Selection.Replace(TextSelection, TotalLines, text, NewLineCharacter);

                selectionrenderer.ClearSelection();
                TextSelection = null;
                UpdateSelection();
            }

            Internal_CharacterAddedOrRemoved();
            UpdateText();
            UpdateCursor();
        }
        private void RemoveText(bool ControlIsPressed = false)
        {
            CurrentLine = GetCurrentLine();

            if (CurrentLine == null)
                return;

            if (TextSelection == null)
            {
                int StepsToMove = ControlIsPressed ? Cursor.CalculateStepsToMoveLeft(CurrentLine, CursorPosition.CharacterPosition) : 1;
                if (CursorPosition.CharacterPosition > 0)
                {
                    CurrentLine.Remove(CursorPosition.CharacterPosition - StepsToMove, StepsToMove);
                    CursorPosition.CharacterPosition -= StepsToMove;
                    Internal_CharacterAddedOrRemoved();
                }
                else if (CursorPosition.LineNumber > 1)
                {
                    //Move the cursor one line up, if the beginning of the line is reached
                    Line LineOnTop = TotalLines[CursorPosition.LineNumber - 2];
                    LineOnTop.AddToEnd(CurrentLine.Content);
                    TotalLines.Remove(CurrentLine);
                    CursorPosition.LineNumber -= 1;
                    CursorPosition.CharacterPosition = LineOnTop.Content.Length - CurrentLine.Content.Length;
                    Internal_CharacterAddedOrRemoved();
                }
            }
            else
            {
                AddCharacter(""); //Replace the selection by nothing
                selectionrenderer.ClearSelection();
                TextSelection = null;
                UpdateSelection();
            }

            UpdateText();
            UpdateCursor();
        }
        private void DeleteText(bool ControlIsPressed = false)
        {
            if (CurrentLine == null)
                return;

            if (TextSelection == null)
            {
                int StepsToMove = ControlIsPressed ? Cursor.CalculateStepsToMoveRight(CurrentLine, CursorPosition.CharacterPosition) : 1;
                if (CursorPosition.CharacterPosition < CurrentLine.Content.Length)
                {
                    CurrentLine.Remove(CursorPosition.CharacterPosition, StepsToMove);
                }
                else if (TotalLines.Count > CursorPosition.LineNumber)
                {
                    TotalLines.Remove(CurrentLine);
                    TotalLines[CursorPosition.LineNumber].Remove(CursorPosition.CharacterPosition, StepsToMove);
                }
            }
            else
            {
                AddCharacter(""); //Replace the selection by nothing
                selectionrenderer.ClearSelection();
                TextSelection = null;
                UpdateSelection();
            }


            UpdateText();
            UpdateCursor();
        }
        private void AddNewLine()
        {
            var NewLine = new Line();
            CursorPosition OldCursorPos = new CursorPosition(CursorPosition);

            if (CurrentLine != null)
            {
                if (CursorPosition.CharacterPosition < CurrentLine.Content.Length && CurrentLine.Content != "")
                {
                    string CurrentLineContent = CurrentLine.Content.Substring(0, CursorPosition.CharacterPosition);
                    string NewLineContent = CurrentLine.Content.Substring(CursorPosition.CharacterPosition);
                    CurrentLine.SetText(CurrentLineContent);
                    NewLine.SetText(NewLineContent);
                }
            }
            TotalLines.Insert(CursorPosition.LineNumber, NewLine);
            CursorPosition.Change(0, CursorPosition.LineNumber + 1);

            UndoRedo.RecordNewLineUndo(NewLine, OldCursorPos);

            if (CursorPosition.LineNumber > NumberOfStartLine + RenderedLines.Count)
            {
                ScrollOneLineDown();
            }
            Internal_CharacterAddedOrRemoved();

            UpdateText();
            UpdateCursor();
        }

        private void ClearSelectionIfNeeded()
        {
            //If the selection is visible, but is not getting set, clear the selection
            if (selectionrenderer.HasSelection && !selectionrenderer.IsSelecting)
            {
                selectionrenderer.ClearSelection();
                TextSelection = null;
                UpdateSelection();
            }
        }
        private void ClearSelection()
        {
            if (selectionrenderer.HasSelection)
            {
                selectionrenderer.ClearSelection();
                TextSelection = null;
                UpdateSelection();
            }
        }
        private bool CursorIsInUnrenderedRegion()
        {
            return CursorPosition.LineNumber < NumberOfUnrenderedLinesToRenderStart +1 || CursorPosition.LineNumber > NumberOfUnrenderedLinesToRenderStart + RenderedLines.Count-1;
        }
        private void StartSelectionIfNeeded()
        {
            if (!selectionrenderer.HasSelection)
                selectionrenderer.SelectionStartPosition = selectionrenderer.SelectionEndPosition = new CursorPosition(CursorPosition.CharacterPosition, CursorPosition.LineNumber - 1);
        }
        private bool SelectionIsNull()
        {
            return selectionrenderer.SelectionStartPosition == null || selectionrenderer.SelectionEndPosition == null;
        }
        private CanvasTextLayout CreateTextLayoutForLine(CanvasControl sender, int LineIndex)
        {
            if (LineIndex < TotalLines.Count)
                return TextRenderer.CreateTextLayout(sender, TextFormat, TotalLines[LineIndex].Content +"|", sender.Size);
            return null;
        }

        private void SelectDoubleClick(PointerRoutedEventArgs e)
        {
            //Calculate the Characterposition from the current pointerposition:
            int Characterpos = CursorRenderer.GetCharacterPositionFromPoint(GetCurrentLine(), CurrentLineTextLayout, e.GetCurrentPoint(Canvas_Text).Position, 0);
            //Use a function to calculate the steps from one the current position to the next letter or digit
            int StepsLeft = Cursor.CalculateStepsToMoveLeft2(CurrentLine, Characterpos);
            int StepsRight = Cursor.CalculateStepsToMoveRight2(CurrentLine, Characterpos);

            //Update variables
            selectionrenderer.SelectionStartPosition = new CursorPosition(Characterpos - StepsLeft, CursorPosition.LineNumber - 1);
            selectionrenderer.SelectionEndPosition = new CursorPosition(Characterpos + StepsRight, CursorPosition.LineNumber - 1);
            CursorPosition.CharacterPosition = selectionrenderer.SelectionEndPosition.CharacterPosition;      
            selectionrenderer.HasSelection = true;

            //Render it
            UpdateCursor();
            UpdateSelection();
        }

        private CodeLanguage GetCodeLanguage(CodeLanguages Languages)
        {
            switch (Languages)
            {
                case CodeLanguages.Csharp:
                    return new Csharp();
                case CodeLanguages.Gcode:
                    return new GCode();
                case CodeLanguages.Html:
                    return new Html();
            }
            return null;
        }
        private CodeLanguages GetCodeLanguages(CodeLanguage Language)
        {
            if (System.Object.ReferenceEquals(Language, new Csharp()))
                return CodeLanguages.Csharp;
            else if (System.Object.ReferenceEquals(Language, new GCode()))
                return CodeLanguages.Gcode;
            else if (System.Object.ReferenceEquals(Language, new Html()))
                return CodeLanguages.Html;
            else return CodeLanguages.None;
        }

        //Get the position of the cursor and set it to the CursorPosition variable
        private void UpdateCursorVariable(PointerRoutedEventArgs e)
        {
            CursorPosition.LineNumber = CursorRenderer.GetCursorLineFromPoint(Canvas_Text, e, SingleLineHeight, RenderedLines.Count, NumberOfStartLine, NumberOfUnrenderedLinesToRenderStart);
            UpdateCurrentLineTextLayout();
            CursorPosition.CharacterPosition = CursorRenderer.GetCharacterPositionFromPoint(GetCurrentLine(), CurrentLineTextLayout, e.GetCurrentPoint(Canvas_Text).Position, 0);
        }

        //Syntaxhighlighting
        private void UpdateSyntaxHighlighting()
        {
            if (_CodeLanguage == null || !SyntaxHighlighting)
                return;

            var Highlights = _CodeLanguage.Highlights;
            for (int i = 0; i < Highlights.Count; i++)
            {
                var matches = Regex.Matches(RenderedText, Highlights[i].Pattern);
                for (int j = 0; j < matches.Count; j++)
                {
                    DrawnTextLayout.SetColor(matches[j].Index, matches[j].Length, Highlights[i].Color);
                }
            }
        }
        private void UpdateTextHighlights(CanvasDrawingSession DrawingSession)
        {
            /*for (int i = 0; i < _TextHighlights.Count; i++)
            {
                TextHighlight th = _TextHighlights[i];
                int Start = 0;
                int End = 0;
                if (th.StartIndex < RenderStartIndex && th.EndIndex > RenderStartIndex)
                {
                    End = th.EndIndex;
                    Start = 0;
                }
                if(th.StartIndex > RenderStartIndex && th.StartIndex > RenderStartIndex)

                var regions = DrawnTextLayout.GetCharacterRegions(th.StartIndex - RenderStartIndex, th.EndIndex - th.StartIndex - RenderStartIndex);
                for(int j = 0; j< regions.Length; j++)
                {
                    DrawingSession.FillRectangle(new Windows.Foundation.Rect
                    {
                        Height = regions[j].LayoutBounds.Height,
                        Width = regions[j].LayoutBounds.Width,
                        X = regions[j].LayoutBounds.X,
                        Y = regions[j].LayoutBounds.Y
                    }, th.HighlightColor);
                }
            }*/
        }
        
        //Draw characters to textbox
        private void CoreWindow_CharacterReceived(CoreWindow sender, CharacterReceivedEventArgs args)
        {
            //Prevent key-entering if control key is pressed 
            var ctrl = Window.Current.CoreWindow.GetKeyState(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
            if (ctrl)
                return;

            char character = (char)args.KeyCode;

            if (args.KeyCode == 13) //Enter
                return;

            if (args.KeyCode == 8) //Back-key was pressed
                return;

            AddCharacter(character.ToString());
        }
        //Handle keyinputs
        private void CoreWindow_KeyDown(Windows.UI.Core.CoreWindow sender, Windows.UI.Core.KeyEventArgs e)
        {
            var ctrl = Window.Current.CoreWindow.GetKeyState(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
            var shift = Window.Current.CoreWindow.GetKeyState(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);
            if (ctrl && !shift)
            {
                switch (e.VirtualKey)
                {
                    case VirtualKey.Up:
                        ScrollOneLineUp();
                        break;
                    case VirtualKey.Down:
                        ScrollOneLineDown();
                        break;
                    case VirtualKey.Z:
                        Undo();
                        UpdateText();
                        break;
                    case VirtualKey.V:
                        Paste();
                        break;
                    case VirtualKey.C:
                        Copy();
                        break;
                    case VirtualKey.X:
                        Cut();
                        break;
                    case VirtualKey.A:
                        SelectAll();
                        break;
                }

                if (e.VirtualKey != VirtualKey.Left && e.VirtualKey != VirtualKey.Right && e.VirtualKey != VirtualKey.Back && e.VirtualKey != VirtualKey.Delete)
                    return;
                    
            }

            switch (e.VirtualKey)
            {
                case VirtualKey.Enter:
                    AddNewLine();
                    break;
                case VirtualKey.Back:
                    RemoveText(ctrl);
                    break;
                case VirtualKey.Delete:
                    DeleteText(ctrl);
                    break;
                case VirtualKey.Left:
                    {
                        if (shift)
                        {
                            StartSelectionIfNeeded();
                            if (SelectionIsNull())
                                return;
                            selectionrenderer.HasSelection = true;
                            selectionrenderer.IsSelecting = true;
                            CursorPosition = selectionrenderer.SelectionEndPosition = Cursor.MoveSelectionLeft(selectionrenderer.SelectionEndPosition, TotalLines);
                            CursorPosition.Change(CursorPosition.CharacterPosition, CursorPosition.LineNumber + 1);
                            UpdateSelection();
                            selectionrenderer.IsSelecting = false;
                        }
                        else
                        {
                            ClearSelectionIfNeeded();
                            CursorPosition = Cursor.RelativeToAbsolute(Cursor.MoveLeft(CursorPosition, TotalLines), NumberOfUnrenderedLinesToRenderStart);
                        }
                        UpdateCursor();
                        if (CursorIsInUnrenderedRegion())
                            ScrollOneLineUp();
                        break;
                    }
                case VirtualKey.Right:
                    {
                        if (shift)
                        {
                            StartSelectionIfNeeded();
                            if (SelectionIsNull())
                                return;

                            selectionrenderer.IsSelecting = true;
                            CursorPosition = selectionrenderer.SelectionEndPosition = Cursor.MoveSelectionRight(selectionrenderer.SelectionEndPosition, TotalLines, CurrentLine);
                            CursorPosition = CursorPosition.ChangeLineNumber(CursorPosition, CursorPosition.LineNumber + 1);
                            UpdateSelection();
                            selectionrenderer.IsSelecting = false;
                        }
                        else
                        {
                            ClearSelectionIfNeeded();
                            CursorPosition = Cursor.RelativeToAbsolute(Cursor.MoveRight(CursorPosition, TotalLines, GetCurrentLine()), NumberOfUnrenderedLinesToRenderStart);
                        }
                        UpdateCursor();
                        if (CursorIsInUnrenderedRegion())
                            ScrollOneLineDown();
                        break;
                    }
                case VirtualKey.Down:
                    {
                        if (shift)
                        {
                            StartSelectionIfNeeded();
                            if (SelectionIsNull())
                                return;

                            selectionrenderer.IsSelecting = true;
                            var NewCurPos = selectionrenderer.SelectionEndPosition = Cursor.MoveSelectionDown(selectionrenderer.SelectionEndPosition, TotalLines);
                            CursorPosition.Change(NewCurPos.CharacterPosition, NewCurPos.LineNumber + 1);
                            UpdateSelection();
                            selectionrenderer.IsSelecting = false;
                        }
                        else
                        {
                            //Move the cursor to the bottom of the selection, if Down is pressed and text is selected
                            if (!SelectionIsNull())
                            {
                                CursorPosition = Cursor.RelativeToAbsolute(Selection.GetMax(TextSelection.EndPosition, TextSelection.StartPosition), NumberOfUnrenderedLinesToRenderStart);
                            }
                            ClearSelectionIfNeeded();
                            CursorPosition = Cursor.RelativeToAbsolute(Cursor.MoveDown(CursorPosition, TotalLines), NumberOfUnrenderedLinesToRenderStart);
                        }
                        if (CursorIsInUnrenderedRegion())
                            ScrollBottomIntoView();
                        UpdateCursor();
                        break;
                    }
                case VirtualKey.Up:
                    {
                        if (shift)
                        {
                            StartSelectionIfNeeded();
                            if (SelectionIsNull())
                                return;
                            
                            selectionrenderer.IsSelecting = true;
                            var NewCurPos = CursorPosition = selectionrenderer.SelectionEndPosition = Cursor.MoveSelectionUp(selectionrenderer.SelectionEndPosition, TotalLines);
                            CursorPosition.Change(NewCurPos.CharacterPosition, NewCurPos.LineNumber + 1);
                            UpdateSelection();
                            selectionrenderer.IsSelecting = false;
                        }
                        else
                        {
                            ClearSelectionIfNeeded();
                            CursorPosition = CursorPosition = Cursor.RelativeToAbsolute(Cursor.MoveUp(CursorPosition, TotalLines), NumberOfUnrenderedLinesToRenderStart);
                        }

                        if (CursorIsInUnrenderedRegion())
                        {
                            ScrollTopIntoView();
                        }
                        UpdateCursor();
                        break;
                    }
                case VirtualKey.Escape:
                    {
                        ClearSelection();
                        break;
                    }
            }
        }

        //Draw the selection
        private void Canvas_Selection_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (selectionrenderer.IsSelecting)
                selectionrenderer.HasSelection = true;

            selectionrenderer.IsSelecting = false;
        }
        private void Canvas_Selection_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (selectionrenderer.IsSelecting)
            {
                UpdateCursorVariable(e);
                UpdateCursor();

                selectionrenderer.SelectionEndPosition = new CursorPosition(CursorPosition.CharacterPosition, CursorPosition.LineNumber-1);
                UpdateSelection();
                e.Handled = true;
            }
        }
        private void Canvas_Selection_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            PointerClickCount++;

            if (PointerClickCount == 3)
            {
                Debug.WriteLine("TripleClicked");
                SelectLine(CursorPosition.LineNumber);
                PointerClickCount = 0;
                return;
            }
            else if (PointerClickCount == 2)
            {
                SelectDoubleClick(e);
                Debug.WriteLine("DoubleClicked");
            }
            else
            {
                bool IsShiftPressed = Window.Current.CoreWindow.GetKeyState(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);

                //Show the onscreenkeyboard if no physical keyboard is attached
                inputPane.TryShow();

                bool LeftButtonPressed = e.GetCurrentPoint(sender as UIElement).Properties.IsLeftButtonPressed;
                //Shift + click = set selection
                if (IsShiftPressed && LeftButtonPressed)
                {
                    UpdateCursorVariable(e);

                    selectionrenderer.SelectionEndPosition = new CursorPosition(CursorPosition.CharacterPosition, CursorPosition.LineNumber - 1);
                    selectionrenderer.HasSelection = true;
                    selectionrenderer.IsSelecting = false;
                    UpdateSelection();
                    return;
                }

                if (LeftButtonPressed)
                {
                    UpdateCursorVariable(e);
                    selectionrenderer.SelectionStartPosition = new CursorPosition(CursorPosition.CharacterPosition, CursorPosition.LineNumber - 1);
                    if (selectionrenderer.HasSelection)
                    {
                        selectionrenderer.ClearSelection();
                        TextSelection = null;
                        UpdateSelection();
                    }
                    else
                    {
                        selectionrenderer.IsSelecting = true;
                        selectionrenderer.HasSelection = true;
                    }
                }
                UpdateCursor();
            }

            PointerClickTimer.Start();
            PointerClickTimer.Tick += (s, t) =>
            {
                PointerClickTimer.Stop();
                PointerClickCount = 0;
            };

            e.Handled = true;
        }
        private void Canvas_Selection_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var ctrl = Window.Current.CoreWindow.GetKeyState(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
            var shift = Window.Current.CoreWindow.GetKeyState(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);
            var delta = e.GetCurrentPoint(this).Properties.MouseWheelDelta;

            //Zoom using mousewheel
            if (ctrl)
            {
                FontSize += delta / 100;
                CheckFontSize();
                NeedsTextFormatUpdate = true;
                ScrollLineIntoView(TotalLines.IndexOf(CurrentLine));
                Canvas_Text.Invalidate();
                Canvas_Selection.Invalidate();
                e.Handled = true;
            }
            //Scroll horizontal using mousewheel
            else if (shift)
            {
                HorizontalScrollbar.Value -= delta;
            }
            //Scroll vertical using mousewheel
            else
            {
                VerticalScrollbar.Value -= delta;
            }
        }

        //Scrolling and Zooming
        private void VerticalScrollbar_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            Canvas_Text.Invalidate();
            Canvas_Selection.Invalidate();
            UpdateCursor();
        }
        private void HorizontalScrollbar_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            Canvas_Text.Invalidate();
            Canvas_Selection.Invalidate();
        }

        private void Canvas_Text_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            //Clear the rendered lines, to fill them with new lines
            RenderedLines.Clear();
            //Create resources and layouts:
            if (NeedsTextFormatUpdate || TextFormat == null)
            {
                if(_ShowLineNumbers)
                    LineNumberTextFormat = TextRenderer.CreateLinenumberTextFormat(FontSize);
                TextFormat = TextRenderer.CreateCanvasTextFormat(FontSize);
                UpdateCharSize();
            }

            CreateColorResources(args.DrawingSession);

            //Calculate number of lines that needs to be rendered
            int NumberOfLinesToBeRendered = (int)(this.ActualHeight / SingleLineHeight);
            NumberOfStartLine = (int)(VerticalScrollbar.Value / SingleLineHeight);
            NumberOfUnrenderedLinesToRenderStart = NumberOfStartLine;
            
            //Measure textposition and apply the value to the scrollbar
            VerticalScrollbar.Maximum = (TotalLines.Count + 1) * SingleLineHeight - Scroll.ActualHeight;
            VerticalScrollbar.ViewportSize = this.ActualHeight;

            StringBuilder LineNumberContent = new StringBuilder();
            if (_ShowLineNumbers)
            {
                //Calculate the linenumbers             
                float LineNumberWidth = (float)Utils.MeasureTextSize(CanvasDevice.GetSharedDevice(), (TotalLines.Count).ToString(), LineNumberTextFormat).Width;
                Canvas_LineNumber.Width = LineNumberWidth + 10 + SpaceBetweenLineNumberAndText;
                Canvas_Text.Margin = new Thickness(RenderingOffsetLeft, 0, 0, 0);
            }
            else
                Canvas_LineNumber.Width = SpaceBetweenLineNumberAndText;

            //Get all the lines, which needs to be rendered, from the array with all lines
            StringBuilder TextToRender = new StringBuilder();
            for (int i = NumberOfStartLine; i < NumberOfStartLine + NumberOfLinesToBeRendered; i++)
            {
                if (i < TotalLines.Count)
                {
                    Line item = TotalLines[i];
                    RenderedLines.Add(item);
                    TextToRender.AppendLine(item.Content);
                    if(_ShowLineNumbers)
                        LineNumberContent.AppendLine((i + 1).ToString());
                }
            }

            if (_ShowLineNumbers)
                LineNumberTextToRender = LineNumberContent.ToString();

            RenderedText = TextToRender.ToString();
            //Clear the StringBuilder:
            TextToRender.Clear();

            //Get text from longest line in whole text
            int LongestLineLenght = Utils.GetLongestLineLenght(TotalLines);

            //Measure horizontal Width of longest line and apply to scrollbar
            HorizontalScrollbar.Maximum = LongestLineLenght * CharWidth;
            HorizontalScrollbar.ViewportSize = this.ActualWidth;

            //Create the textlayout --> apply the Syntaxhighlighting --> render it
            DrawnTextLayout = TextRenderer.CreateTextResource(sender, DrawnTextLayout, TextFormat, RenderedText, new Size { Height = sender.Size.Height, Width = this.ActualWidth });
            UpdateSyntaxHighlighting();
            args.DrawingSession.DrawTextLayout(DrawnTextLayout, (float)(-HorizontalScrollbar.Value), SingleLineHeight, TextColorBrush);
            
            //UpdateTextHighlights(args.DrawingSession);
            
            if(_ShowLineNumbers)
                Canvas_LineNumber.Invalidate();
        }
        private void Canvas_Selection_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            if (selectionrenderer.SelectionStartPosition != null && selectionrenderer.SelectionEndPosition != null)
            {
                if (selectionrenderer.SelectionStartPosition.LineNumber != selectionrenderer.SelectionEndPosition.LineNumber &&
                    selectionrenderer.SelectionStartPosition.CharacterPosition != selectionrenderer.SelectionEndPosition.CharacterPosition)
                {
                    selectionrenderer.HasSelection = true;
                }
            }

            if (selectionrenderer.HasSelection)
            {
                TextSelection = selectionrenderer.DrawSelection(DrawnTextLayout, RenderedLines, args, RenderingOffsetLeft, SingleLineHeight / 4, NumberOfUnrenderedLinesToRenderStart, RenderedLines.Count, new ScrollBarPosition(HorizontalScrollbar.Value, VerticalScrollbar.Value));
            }
        }
        private void Canvas_Cursor_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            CurrentLine = GetCurrentLine();
            if (CurrentLine == null)
                return;

            UpdateCurrentLineTextLayout();

            //Calculate the distance to the top for the cursorposition and render the cursor
            float RenderPosition = (float)((CursorPosition.LineNumber - NumberOfUnrenderedLinesToRenderStart - 1) * SingleLineHeight) + SingleLineHeight / 4;
            CursorRenderer.RenderCursor(CurrentLineTextLayout, CursorPosition.CharacterPosition, RenderingOffsetLeft, RenderPosition, FontSize, args, CursorColorBrush);
        }
        private void Canvas_LineNumber_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            if (LineNumberTextToRender.Length == 0 || !_ShowLineNumbers)
                return;

            CanvasTextLayout LineNumberLayout = TextRenderer.CreateTextLayout(sender, LineNumberTextFormat, LineNumberTextToRender, (float)sender.Size.Width, (float)sender.Size.Height);
            args.DrawingSession.DrawTextLayout(LineNumberLayout, 0, SingleLineHeight, LineNumberColorBrush);
        }

        //Functions:
        /// <summary>
        /// Select the line specified by the index
        /// </summary>
        /// <param name="index">The index of the line to select</param>
        /// <param name="CursorAtStart">Select whether the cursor moves to start or end of the line</param>
        public void SelectLine(int index, bool CursorAtStart = false)
        {
            selectionrenderer.SelectionStartPosition = new CursorPosition(0, index - 1);
            CursorPosition pos = selectionrenderer.SelectionEndPosition = new CursorPosition(TotalLines[index-1].Content.Length, index - 1);
            CursorPosition.LineNumber = index;
            CursorPosition.CharacterPosition = CursorAtStart ? 0 : pos.CharacterPosition;

            UpdateSelection();
            UpdateCursor();
        }
        public void SetText(string text)
        {
            TotalLines.Clear();
            CurrentText = text;
            selectionrenderer.ClearSelection();

            //Get the LineEnding
            LineEnding = LineEndings.FindLineEnding(text);

            //Split the lines by the current LineEnding
            var lines = text.Split(NewLineCharacter);
            for (int i = 0; i < lines.Length; i++)
            {
                TotalLines.Add(new Line (lines[i]));
            }

            Debug.WriteLine("Loaded " + lines.Length + " lines with the lineending " + LineEnding.ToString());
            UpdateText();
            UpdateSelection();
        }
        public async void Paste()
        {
            DataPackageView dataPackageView = Clipboard.GetContent();
            if (dataPackageView.Contains(StandardDataFormats.Text))
            {
                string Text = LineEndings.ChangeLineEndings(await dataPackageView.GetTextAsync(), LineEnding);
                AddCharacter(Text);
                UpdateText();
                ClearSelection();
            }
        }
        public void Copy()
        {
            DataPackage dataPackage = new DataPackage();
            dataPackage.SetText(LineEndings.ChangeLineEndings(SelectedText(), LineEnding));
            dataPackage.RequestedOperation = DataPackageOperation.Copy;
            Clipboard.SetContent(dataPackage);
        }
        public void Cut()
        {
            DataPackage dataPackage = new DataPackage();
            dataPackage.SetText(LineEndings.ChangeLineEndings(SelectedText(), LineEnding));
            DeleteText(); //Delete the selected text
            dataPackage.RequestedOperation = DataPackageOperation.Move;
            Clipboard.SetContent(dataPackage);
            ClearSelection();
        }
        public string SelectedText()
        {
            return Selection.GetSelectedText(TextSelection, TotalLines, NewLineCharacter);
        }
        public string GetText()
        {
            return String.Join(NewLineCharacter, TotalLines.Select(item => item.Content));
        }
        public void SetSelection(CursorPosition StartPosition, CursorPosition EndPosition = null)
        {            
            //Nothing gets selected:
            if(EndPosition == null || (StartPosition.LineNumber == EndPosition.LineNumber && StartPosition.CharacterPosition == EndPosition.CharacterPosition))
            {
                CursorPosition = new CursorPosition(StartPosition.CharacterPosition, StartPosition.LineNumber - 1);
            }
            else
            {
                selectionrenderer.SelectionStartPosition = new CursorPosition(StartPosition.CharacterPosition, StartPosition.LineNumber - 1);
                selectionrenderer.SelectionEndPosition = new CursorPosition(EndPosition.CharacterPosition, StartPosition.LineNumber - 1);
                selectionrenderer.HasSelection = true;
                Canvas_Selection.Invalidate();
            }
            UpdateCursor();
        }
        public void SelectAll()
        {
            selectionrenderer.SelectionStartPosition = new CursorPosition(0, 0);
            selectionrenderer.SelectionEndPosition = new CursorPosition(TotalLines[TotalLines.Count-1].Content.Length, TotalLines.Count);
            selectionrenderer.HasSelection = true;
            Canvas_Selection.Invalidate();
        }
        public void Undo()
        {
            var sel = UndoRedo.Undo(TotalLines, this, NewLineCharacter);
            if (sel == null)
                return;

            selectionrenderer.SelectionStartPosition = sel.StartPosition;
            selectionrenderer.SelectionEndPosition = sel.EndPosition;
            selectionrenderer.HasSelection = true;
            selectionrenderer.IsSelecting = false;
            Debug.WriteLine(sel.ToString());

            UpdateSelection();
        }
        public void Redo()
        {
            UndoRedo.Redo(TotalLines, this);
        }
        public void ScrollOneLineUp()
        {
            VerticalScrollbar.Value -= SingleLineHeight;
        }
        public void ScrollOneLineDown()
        {
            VerticalScrollbar.Value += SingleLineHeight;
        }
        public void ScrollLineIntoView(int Line)
        {
            VerticalScrollbar.Value = Line * SingleLineHeight;
        }
        public void ScrollTopIntoView()
        {
            VerticalScrollbar.Value = (CursorPosition.LineNumber) * SingleLineHeight;
        }
        public void ScrollBottomIntoView()
        {
            VerticalScrollbar.Value = (CursorPosition.LineNumber - RenderedLines.Count + 1) * SingleLineHeight;
        }

        //Properties:
        public ObservableCollection<TextHighlight> TextHighlights
        {
            get => _TextHighlights;
        }
        public bool SyntaxHighlighting { get; set; } = true;
        public CodeLanguage CustomCodeLanguage
        {
            get => _CodeLanguage;
            set
            {
                _CodeLanguage = value;
                UpdateSyntaxHighlighting();
            }
        }
        public CodeLanguages CodeLanguage
        {
            get => _CodeLanguages;
            set
            {
                _CodeLanguage = GetCodeLanguage(value);
                UpdateSyntaxHighlighting();
            }
        }
        public LineEnding LineEnding
        {
            get => _LineEnding;
            set
            {
                NewLineCharacter = LineEndings.LineEndingToString(value);
                _LineEnding = value;
            }
        }
        public float SpaceBetweenLineNumberAndText = 0;
        public CursorPosition CursorPosition
        {
            get => _CursorPosition;
            set { _CursorPosition = new CursorPosition(value.CharacterPosition, value.LineNumber + (int)(VerticalScrollbar.Value / SingleLineHeight)); UpdateCursor(); }
        }
        public new float FontSize = 18;
        public string CurrentText { get => _CurrentText; set { _CurrentText = value; Internal_TextChanged(); } }
        public Color TextColor = Color.FromArgb(255, 255, 255, 255);
        public Color SelectionColor = Color.FromArgb(100, 0, 100, 255);
        public Color CursorColor = Color.FromArgb(255, 255, 255, 255);
        public Color LineNumberColor = Color.FromArgb(255, 0, 150, 255);
        public bool ShowLineNumbers
        {
            get
            {
                return _ShowLineNumbers;
            }
            set
            {
                _ShowLineNumbers = value;
                UpdateText();
            }
        }

        //Internal events:
        private void Internal_TextChanged()
        {

        }
        private void Internal_CursorChanged()
        {

        }
        private void Internal_CharacterAddedOrRemoved()
        {
            if (CursorIsInUnrenderedRegion())
                ScrollLineIntoView(CursorPosition.LineNumber);
        }
        private void UserControl_FocusEngaged(Control sender, FocusEngagedEventArgs args)
        {
            inputPane.TryShow();
            ChangeCursor(CoreCursorType.IBeam);
        }

        private void UserControl_FocusDisengaged(Control sender, FocusDisengagedEventArgs args)
        {
            inputPane.TryHide();
            ChangeCursor(CoreCursorType.Arrow);
        }
        private void _TextHighlights_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            //Nothing has changed
            if (e.NewItems.Count == 0 && e.OldItems.Count == 0)
                return;

            UpdateText();
        }

        //Cursor:
        private void ChangeCursor(CoreCursorType CursorType)
        {
            Window.Current.CoreWindow.PointerCursor = new CoreCursor(CursorType, 0);
        }
        private void Canvas_LineNumber_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            ChangeCursor(CoreCursorType.UpArrow);
        }
        private void Canvas_LineNumber_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            ChangeCursor(CoreCursorType.IBeam);
        }
    }

    public class Line
    {
        public string Content { get; set; } = "";

        public Line(string Content = "")
        {
            this.Content = Content;
        }
        public void SetText(string Value)
        {
            Content = Value;
        }
        public void AddText(string Value, int Position)
        {
            if (Content == "")
                AddToEnd(Value);
            else
                Content = Content.Insert(Position, Value);
        }
        public void AddToEnd(string Value)
        {
            Content += Value;
        }
        public void AddToStart(string Value)
        {
            Content = Content.Insert(0, Value);
        }
        public string Remove(int Index, int Count = -1)
        {
            if (Index >= Content.Length || Index < 0)
                return Content;

            try
            {
                if (Count == -1)
                    Content = Content.Remove(Index);
                else
                    Content = Content.Remove(Index, Count);
            }
            catch (ArgumentOutOfRangeException )
            {
            }
            return Content;
        }
        public string Substring(int Index, int Count = -1)
        {
            if (Index >= Content.Length)
                Content = "";
            else if (Count == -1)
                Content = Content.Substring(Index);
            else
                Content = Content.Substring(Index, Count);
            return Content;
        }
        public void ReplaceText(int Start, int End, string Text)
        {
            int end = Math.Max(Start, End);
            int start = Math.Min(Start, End);

            Debug.WriteLine(start + "::" + end + "::" + Content.Length);
            if (start == 0 && end >= Content.Length)
                Content = "";
            else
            {
                Content = Content.Remove(End) + Text + Content.Remove(0, start); 
            }
        }
    }
    public enum CodeLanguages
    {
        Csharp, Gcode, Html, None
    }
    public class TextHighlight
    {
        public int StartIndex { get; }
        public int EndIndex { get; }
        public Color HighlightColor { get; }

        public TextHighlight(int StartIndex, int EndIndex, Color HighlightColor)
        {
            this.StartIndex = StartIndex;
            this.EndIndex = EndIndex;
            this.HighlightColor = HighlightColor;
        }
    }
    public class CodeLanguage
    {
        public string Name { get; set; }
        public List<SyntaxHighlights> Highlights = new List<SyntaxHighlights>();
    }
    public class SyntaxHighlights
    {
        public SyntaxHighlights(string Pattern, Windows.UI.Color Color)
        {
            this.Pattern = Pattern;
            this.Color = Color;
        }

        public string Pattern { get; set; }
        public Color Color { get; set; }
    }
    public class ScrollBarPosition
    {
        public ScrollBarPosition(ScrollBarPosition ScrollBarPosition)
        {
            this.ValueX = ScrollBarPosition.ValueX;
            this.ValueY = ScrollBarPosition.ValueY;
        }
        public ScrollBarPosition(double ValueX = 0, double ValueY = 0)
        {
            this.ValueX = ValueX;
            this.ValueY = ValueY;
        }
        
        public double ValueX { get; set; }
        public double ValueY { get; set; }
    }

    public enum LineEnding
    {
        LF, CRLF, CR
    }
}
public static class Extensions
{
    public static string ToDelimitedString<T>(this IEnumerable<T> source, string delimiter, Func<T, string> func)
    {
        return String.Join(delimiter, source.Select(func).ToArray());
    }
}