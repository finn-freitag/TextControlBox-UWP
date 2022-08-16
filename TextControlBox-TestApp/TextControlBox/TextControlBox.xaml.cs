﻿using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TextControlBox_TestApp.TextControlBox.Helper;
using TextControlBox_TestApp.TextControlBox.Languages;
using TextControlBox_TestApp.TextControlBox.Renderer;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Color = Windows.UI.Color;
using Size = Windows.Foundation.Size;

namespace TextControlBox_TestApp.TextControlBox
{
    public partial class TextControlBox : UserControl
    {
        private string NewLineCharacter = "\r\n";
        private string TabCharacter = "\t";
        private InputPane inputPane;
        private bool _ShowLineNumbers = true;
        private CodeLanguage _CodeLanguage = null;
        private CodeLanguages _CodeLanguages = CodeLanguages.None;
        private LineEnding _LineEnding = LineEnding.CRLF;
        private ObservableCollection<TextHighlight> _TextHighlights = new ObservableCollection<TextHighlight>();
        private bool _ShowLineHighlighter = true;
        private int _FontSize = 18;
        private int _ZoomFactor = 100; //%
        private float ZoomedFontSize = 0;
        private int MaxFontsize = 125;
        private int MinFontSize = 3;
        private int OldZoomFactor = 0;

        //Colors:
        CanvasSolidColorBrush TextColorBrush;
        CanvasSolidColorBrush CursorColorBrush;
        CanvasSolidColorBrush LineNumberColorBrush;
        CanvasSolidColorBrush LineHighlighterBrush;

        bool ColorResourcesCreated = false;
        bool NeedsTextFormatUpdate = false;
        bool GotKeyboardInput = false;
        bool ScrollEventsLocked = false;

        CanvasTextFormat TextFormat = null;
        CanvasTextLayout DrawnTextLayout = null;
        CanvasTextFormat LineNumberTextFormat = null;

        string RenderedText = "";
        string LineNumberTextToRender = "";

        //The line, which will be rendered first
        int NumberOfStartLine = 0;
        int NumberOfUnrenderedLinesToRenderStart = 0;

        //Handle double and triple -clicks:
        int PointerClickCount = 0;
        DispatcherTimer PointerClickTimer = new DispatcherTimer { Interval = new TimeSpan(0, 0, 0, 0, 200) };

        //Gets updated everytime fontsize changes
        float SingleLineHeight = 0;

        //CursorPosition
        CursorPosition _CursorPosition = new CursorPosition(0, 1);
        CursorPosition OldCursorPosition = null;
        Line CurrentLine = null;
        CanvasTextLayout CurrentLineTextLayout = null;
        TextSelection TextSelection = null;
        TextSelection OldTextSelection = null;

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
            Window.Current.CoreWindow.PointerMoved += CoreWindow_PointerMoved;
            _TextHighlights.CollectionChanged += _TextHighlights_CollectionChanged;
            InitialiseOnStart();
        }

        private void InitialiseOnStart()
        {
            if (TotalLines.Count == 0)
                TotalLines.Add(new Line());
        }

        //Assing the colors to the CanvasSolidColorBrush
        private void CreateColorResources(ICanvasResourceCreatorWithDpi resourceCreator)
        {
            if (ColorResourcesCreated)
                return;

            TextColorBrush = new CanvasSolidColorBrush(resourceCreator, TextColor);
            CursorColorBrush = new CanvasSolidColorBrush(resourceCreator, CursorColor);
            LineNumberColorBrush = new CanvasSolidColorBrush(resourceCreator, LineNumberColor);
            LineHighlighterBrush = new CanvasSolidColorBrush(resourceCreator, LineHighlighterColor);
            ColorResourcesCreated = true;
        }

        private void UpdateZoom()
        {
            ZoomedFontSize = (float)_FontSize * (float)_ZoomFactor / 100;
            if (ZoomedFontSize < MinFontSize)
                ZoomedFontSize = MinFontSize;
            if (ZoomedFontSize > MaxFontsize)
                ZoomedFontSize = MaxFontsize;

            if (_ZoomFactor > 400)
                _ZoomFactor = 400;
            if (_ZoomFactor < 4)
                _ZoomFactor = 4;

            if (_ZoomFactor != OldZoomFactor)
            {
                OldZoomFactor = _ZoomFactor;
                ZoomChanged?.Invoke(this, _ZoomFactor);
            }

            NeedsTextFormatUpdate = true;
            UpdateScrollToShowCursor();
            UpdateText();
            Canvas_Selection.Invalidate();
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
            SingleLineHeight = TextFormat.LineSpacing;
        }
        private void UpdateCurrentLineTextLayout()
        {
            CurrentLineTextLayout = CreateTextLayoutForLine(Canvas_Text, CursorPosition.LineNumber - 1);
        }
        private void SetSelection(TextSelection Selection)
        {
            if (Selection == null)
                return;

            selectionrenderer.SelectionStartPosition = Selection.StartPosition;
            selectionrenderer.SelectionEndPosition = Selection.EndPosition;
            selectionrenderer.HasSelection = true;
            selectionrenderer.IsSelecting = false;
            UpdateSelection();
        }

        private int GetLineContentWidth(Line line)
        {
            if (line == null || line.Content == null)
                return -1;
            return line.Length;
        }
        private Line GetCurrentLine()
        {
            return ListHelper.GetLine(TotalLines, CursorPosition.LineNumber - 1);
        }

        private void UpdateScrollToShowCursor()
        {
            if (NumberOfStartLine + RenderedLines.Count <= CursorPosition.LineNumber - 1)
                ScrollBottomIntoView();
            else if(NumberOfStartLine > CursorPosition.LineNumber)
                ScrollTopIntoView();
        }

        private void AddCharacter(string text)
        {
            if (CurrentLine == null)
                return;

            var SplittedText = text.Split(NewLineCharacter);

            //Nothing is selected
            if (TextSelection == null && SplittedText.Length == 1)
            {
                if (text.Length == 1)
                    UndoRedo.EnteringText += text;
                else
                {
                    UndoRedo.EnteringText = "";
                    UndoRedo.RecordSingleLineUndo(CurrentLine, CursorPosition);
                }

                if (CursorPosition.CharacterPosition > GetLineContentWidth(CurrentLine) - 1)
                    CurrentLine.AddToEnd(text);
                else
                    CurrentLine.AddText(text, CursorPosition.CharacterPosition);
                CursorPosition.CharacterPosition += text.Length;
            }
            else if (TextSelection == null && SplittedText.Length > 1)
            {
                UndoRedo.RecordMultiLineUndo(CursorPosition.LineNumber, ListHelper.GetLine(TotalLines, CursorPosition.LineNumber - 1).Content, SplittedText.Length);
                CursorPosition = Selection.InsertText(TextSelection, CursorPosition, TotalLines, text, NewLineCharacter);
            }
            else
            {
                string SelectedLines = Selection.GetSelectedTextWithoutCharacterPos(TotalLines, TextSelection, NewLineCharacter);
                //Check whether the startline and endline are completely selected to calculate the number of lines to delete
                CursorPosition StartLine = Selection.GetMin(TextSelection.StartPosition, TextSelection.EndPosition);
                int DeleteCount = StartLine.CharacterPosition == 0 ? 0 : 1;
                if (DeleteCount == 0)
                {
                    CursorPosition EndLine = Selection.GetMax(TextSelection.StartPosition, TextSelection.EndPosition);
                    DeleteCount = EndLine.CharacterPosition == ListHelper.GetLine(TotalLines, EndLine.LineNumber).Length ? 0 : 1;
                }

                UndoRedo.RecordMultiLineUndo(StartLine.LineNumber + 1, SelectedLines, text.Length == 0 ? DeleteCount : SplittedText.Length, TextSelection);
                CursorPosition = Selection.Replace(TextSelection, TotalLines, text, NewLineCharacter);

                selectionrenderer.ClearSelection();
                TextSelection = null;
                UpdateSelection();
            }

            UpdateScrollToShowCursor();
            UpdateText();
            UpdateCursor();
            Internal_TextChanged();
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
                    UndoRedo.RecordSingleLineUndo(CurrentLine, CursorPosition);
                    CurrentLine.Remove(CursorPosition.CharacterPosition - StepsToMove, StepsToMove);
                    CursorPosition.CharacterPosition -= StepsToMove;
                }
                else if (CursorPosition.LineNumber > 1)
                {
                    UndoRedo.RecordMultiLineUndo(CursorPosition.LineNumber, ListHelper.GetLine(TotalLines, CursorPosition.LineNumber - 1).Content, 0);

                    //Move the cursor one line up, if the beginning of the line is reached
                    Line LineOnTop = ListHelper.GetLine(TotalLines, CursorPosition.LineNumber - 2);
                    LineOnTop.AddToEnd(CurrentLine.Content);
                    TotalLines.Remove(CurrentLine);
                    CursorPosition.LineNumber -= 1;
                    CursorPosition.CharacterPosition = LineOnTop.Length - CurrentLine.Length;
                }
            }
            else
            {
                AddCharacter(""); //Replace the selection by nothing
                selectionrenderer.ClearSelection();
                TextSelection = null;
                UpdateSelection();
            }

            UpdateScrollToShowCursor();
            UpdateText();
            UpdateCursor();
            Internal_TextChanged();
        }
        private void DeleteText(bool ControlIsPressed = false)
        {
            if (CurrentLine == null)
                return;

            if (TextSelection == null)
            {
                int StepsToMove = ControlIsPressed ? Cursor.CalculateStepsToMoveRight(CurrentLine, CursorPosition.CharacterPosition) : 1;

                if (CursorPosition.CharacterPosition < CurrentLine.Length)
                {
                    UndoRedo.RecordSingleLineUndo(CurrentLine, CursorPosition);
                    CurrentLine.Remove(CursorPosition.CharacterPosition, StepsToMove);
                }
                else if (TotalLines.Count > CursorPosition.LineNumber)
                {
                    Line LineToAdd = CursorPosition.LineNumber < TotalLines.Count ? ListHelper.GetLine(TotalLines, CursorPosition.LineNumber) : null;
                    if (LineToAdd != null)
                    {
                        UndoRedo.RecordMultiLineUndo(CursorPosition.LineNumber, CurrentLine.Content + LineToAdd.Content, 1);
                        CurrentLine.Content += LineToAdd.Content;
                        TotalLines.Remove(LineToAdd);
                    }
                }
            }
            else
            {
                AddCharacter(""); //Replace the selection by nothing
                selectionrenderer.ClearSelection();
                TextSelection = null;
                UpdateSelection();
            }

            UpdateScrollToShowCursor();
            UpdateText();
            UpdateCursor();
            Internal_TextChanged();
        }
        private void AddNewLine()
        {
            if (TotalLines.Count == 0)
            {
                TotalLines.Add(new Line());
                return;
            }

            CursorPosition NormalCurPos = CursorPosition.ChangeLineNumber(CursorPosition, CursorPosition.LineNumber - 1);
            CursorPosition StartLinePos = new CursorPosition(TextSelection == null ? NormalCurPos : Selection.GetMin(TextSelection));
            CursorPosition EndLinePos = new CursorPosition(TextSelection == null ? NormalCurPos : Selection.GetMax(TextSelection));

            int Index = StartLinePos.LineNumber;
            if (Index >= TotalLines.Count)
                Index = TotalLines.Count - 1;
            if (Index < 0)
                Index = 0;

            Line EndLine = new Line();
            Line StartLine = ListHelper.GetLine(TotalLines, Index);

            //If the whole text is selected
            if (Selection.WholeTextSelected(TextSelection, TotalLines))
            {
                UndoRedo.RecordNewLineUndo(GetText(), 2, StartLinePos.LineNumber, TextSelection);

                TotalLines.Clear();
                TotalLines.Add(EndLine);
                CursorPosition = new CursorPosition(0, 1);

                ClearSelection();
                UpdateText();
                UpdateSelection();
                UpdateCursor();
                Internal_TextChanged();
                return;
            }

            //Undo
            string Lines;
            if (TextSelection == null)
                Lines = StartLine.Content;
            else
                Lines = Selection.GetSelectedTextWithoutCharacterPos(TotalLines, TextSelection, NewLineCharacter);

            int UndoDeleteCount = 2;
            //Whole lines are selected
            if (TextSelection != null && StartLinePos.CharacterPosition == 0 && EndLinePos.CharacterPosition == ListHelper.GetLine(TotalLines, EndLinePos.LineNumber).Length)
            {
                UndoDeleteCount = 1;
            }

            UndoRedo.RecordNewLineUndo(Lines, UndoDeleteCount, StartLinePos.LineNumber, TextSelection);

            if (TextSelection != null)
            {
                CursorPosition = Selection.Remove(TextSelection, TotalLines, NewLineCharacter);
                ClearSelection();
                //Inline selection
                if (StartLinePos.LineNumber == EndLinePos.LineNumber)
                {
                    StartLinePos.CharacterPosition = CursorPosition.CharacterPosition;
                }
            }

            string[] SplittedLine = Utils.SplitAt(StartLine.Content, StartLinePos.CharacterPosition);
            StartLine.SetText(SplittedLine[1]);
            EndLine.SetText(SplittedLine[0]);

            //Add the line to the collection
            ListHelper.Insert(TotalLines, EndLine, StartLinePos.LineNumber);

            CursorPosition.LineNumber += 1;
            CursorPosition.CharacterPosition = 0;

            if (TextSelection == null && CursorPosition.LineNumber - 1 == RenderedLines.Count + NumberOfUnrenderedLinesToRenderStart)
                ScrollOneLineDown();
            else
                UpdateScrollToShowCursor();

            UpdateText();
            UpdateSelection();
            UpdateCursor();
            Internal_TextChanged();
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
            return CursorPosition.LineNumber < NumberOfUnrenderedLinesToRenderStart + 1 || CursorPosition.LineNumber > NumberOfUnrenderedLinesToRenderStart + RenderedLines.Count - 1;
        }
        private void StartSelectionIfNeeded()
        {
            if (!selectionrenderer.HasSelection)
                selectionrenderer.SelectionStartPosition = selectionrenderer.SelectionEndPosition = new CursorPosition(CursorPosition.CharacterPosition, CursorPosition.LineNumber - 1);
        }
        private bool SelectionIsNull()
        {
            if (TextSelection == null)
                return true;
            return selectionrenderer.SelectionStartPosition == null || selectionrenderer.SelectionEndPosition == null;
        }
        private CanvasTextLayout CreateTextLayoutForLine(CanvasControl sender, int LineIndex)
        {
            if (LineIndex < TotalLines.Count)
                return TextRenderer.CreateTextLayout(sender, TextFormat, ListHelper.GetLine(TotalLines, LineIndex).Content + "|", sender.Size);
            return null;
        }

        private void SelectDoubleClick(Point Point)
        {
            //Calculate the Characterposition from the current pointerposition:
            int Characterpos = CursorRenderer.GetCharacterPositionFromPoint(GetCurrentLine(), CurrentLineTextLayout, Point, 0);
            //Use a function to calculate the steps from one the current position to the next letter or digit
            int StepsLeft = Cursor.CalculateStepsToMoveLeft2(CurrentLine, Characterpos);
            int StepsRight = Cursor.CalculateStepsToMoveRight2(CurrentLine, Characterpos);

            //Update variables
            selectionrenderer.SelectionStartPosition = new CursorPosition(Characterpos - StepsLeft, CursorPosition.LineNumber - 1);
            selectionrenderer.SelectionEndPosition = new CursorPosition(Characterpos + StepsRight, CursorPosition.LineNumber - 1);
            CursorPosition.CharacterPosition = selectionrenderer.SelectionEndPosition.CharacterPosition;
            selectionrenderer.HasSelection = true;

            //Render it
            UpdateSelection();
            UpdateCursor();
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
        private void UpdateCursorVariable(Point Point)
        {
            CursorPosition.LineNumber = CursorRenderer.GetCursorLineFromPoint(Point, SingleLineHeight, RenderedLines.Count, NumberOfStartLine, NumberOfUnrenderedLinesToRenderStart);

            UpdateCurrentLineTextLayout();
            CursorPosition.CharacterPosition = CursorRenderer.GetCharacterPositionFromPoint(GetCurrentLine(), CurrentLineTextLayout, Point, (float)-HorizontalScrollbar.Value);
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
            //Don't enter any of these characters as chracter -> 
            if (args.KeyCode == 13 || //Enter
                args.KeyCode == 9 || //Tab
                args.KeyCode == 8)  //Back
                return;

            //Prevent key-entering if control key is pressed 
            var ctrl = Window.Current.CoreWindow.GetKeyState(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
            var menu = Window.Current.CoreWindow.GetKeyState(VirtualKey.Menu).HasFlag(CoreVirtualKeyStates.Down);
            if (ctrl && !menu || menu && !ctrl)
                return;

            char character = (char)args.KeyCode;

            if (!GotKeyboardInput)
            {
                UndoRedo.RecordSingleLineUndo(CurrentLine, CursorPosition);
                GotKeyboardInput = true;
            }

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
                case VirtualKey.Space:
                    UndoRedo.RecordUndoOnPress(CurrentLine, CursorPosition);
                    break;
                case VirtualKey.Enter:
                    UndoRedo.RecordUndoOnPress(CurrentLine, CursorPosition);
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

            //Tab-key
            if (e.VirtualKey == VirtualKey.Tab)
            {
                TextSelection Selection;
                if (shift)
                    Selection = TabKey.MoveTabBack(TotalLines, TextSelection, CursorPosition, TabCharacter, NewLineCharacter, UndoRedo);
                else
                    Selection = TabKey.MoveTab(TotalLines, TextSelection, CursorPosition, TabCharacter, NewLineCharacter, UndoRedo);

                if (Selection != null)
                {
                    if (Selection.EndPosition == null)
                    {
                        CursorPosition = Selection.StartPosition;
                    }
                    else
                    {

                        selectionrenderer.SelectionStartPosition = Selection.StartPosition;
                        CursorPosition = selectionrenderer.SelectionEndPosition = Selection.EndPosition;
                        selectionrenderer.HasSelection = true;
                        selectionrenderer.IsSelecting = false;
                    }
                }
                UpdateText();
                UpdateSelection();
                UpdateCursor();
            }
        }

        //Draw the selection
        private void CoreWindow_PointerMoved(CoreWindow sender, PointerEventArgs args)
        {
            Point PointerPosition = args.CurrentPoint.Position;

            if (selectionrenderer.IsSelecting)
            {
                double CanvasWidth = Math.Round(this.ActualWidth, 2);
                double CurPosX = Math.Round(PointerPosition.X, 2);
                if (CurPosX > CanvasWidth - 100)
                {
                    HorizontalScrollbar.Value -= (CurPosX > CanvasWidth + 30 ? -20 : (-CanvasWidth - CurPosX) / 150);
                }
                else if (CurPosX < 100)
                {
                    HorizontalScrollbar.Value += CurPosX < -30 ? -20 : -(100 - CurPosX) / 10;
                }
            }
        }

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
                UpdateCursorVariable(e.GetCurrentPoint(Canvas_Selection).Position);
                UpdateCursor();

                selectionrenderer.SelectionEndPosition = new CursorPosition(CursorPosition.CharacterPosition, CursorPosition.LineNumber - 1);
                UpdateSelection();
            }
        }
        private void Canvas_Selection_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            Point PointerPosition = e.GetCurrentPoint(sender as UIElement).Position;

            PointerClickCount++;
            if (PointerClickCount == 3)
            {
                SelectLine(CursorPosition.LineNumber);
                PointerClickCount = 0;
                return;
            }
            else if (PointerClickCount == 2)
            {
                SelectDoubleClick(PointerPosition);
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
                    UpdateCursorVariable(PointerPosition);

                    selectionrenderer.SelectionEndPosition = new CursorPosition(CursorPosition.CharacterPosition, CursorPosition.LineNumber - 1);
                    selectionrenderer.HasSelection = true;
                    selectionrenderer.IsSelecting = false;
                    UpdateSelection();
                    return;
                }

                if (LeftButtonPressed)
                {
                    UpdateCursorVariable(PointerPosition);
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
        }
        private void Canvas_Selection_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var ctrl = Window.Current.CoreWindow.GetKeyState(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
            var shift = Window.Current.CoreWindow.GetKeyState(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);
            var delta = e.GetCurrentPoint(this).Properties.MouseWheelDelta;

            //Zoom using mousewheel
            if (ctrl)
            {
                _ZoomFactor += delta / 20;
                UpdateZoom();
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
            if (ScrollEventsLocked)
                return;

            Canvas_Text.Invalidate();
            Canvas_Selection.Invalidate();
            Canvas_Cursor.Invalidate();
        }
        private void HorizontalScrollbar_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (ScrollEventsLocked)
                return;
            
            Canvas_Text.Invalidate();
            Canvas_Selection.Invalidate();
            Canvas_Cursor.Invalidate();
        }

        private void Canvas_Text_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            //Clear the rendered lines, to fill them with new lines
            RenderedLines.Clear();
            //Create resources and layouts:
            if (NeedsTextFormatUpdate || TextFormat == null)
            {
                if (_ShowLineNumbers)
                    LineNumberTextFormat = TextRenderer.CreateLinenumberTextFormat(ZoomedFontSize);
                TextFormat = TextRenderer.CreateCanvasTextFormat(ZoomedFontSize);
                UpdateCharSize();
            }

            CreateColorResources(args.DrawingSession);

            //Calculate number of lines that needs to be rendered
            int NumberOfLinesToBeRendered = (int)(sender.ActualHeight / SingleLineHeight);
            NumberOfStartLine = (int)(VerticalScrollbar.Value / SingleLineHeight);

            NumberOfUnrenderedLinesToRenderStart = NumberOfStartLine;

            //Measure textposition and apply the value to the scrollbar
            VerticalScrollbar.Maximum = (TotalLines.Count + 1) * SingleLineHeight - Scroll.ActualHeight;
            VerticalScrollbar.ViewportSize = sender.ActualHeight;

            StringBuilder LineNumberContent = new StringBuilder();

            if (_ShowLineNumbers)
            {
                //Calculate the linenumbers             
                float LineNumberWidth = (float)Utils.MeasureTextSize(CanvasDevice.GetSharedDevice(), (TotalLines.Count).ToString(), LineNumberTextFormat).Width;
                Canvas_LineNumber.Width = LineNumberWidth + 10;
                Scroll.Margin = new Thickness(SpaceBetweenLineNumberAndText, 0, 0, 0);
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
                    if (_ShowLineNumbers)
                        LineNumberContent.AppendLine((i + 1).ToString());
                }
            }

            if (_ShowLineNumbers)
                LineNumberTextToRender = LineNumberContent.ToString();

            RenderedText = TextToRender.ToString();
            //Clear the StringBuilder:
            TextToRender.Clear();

            //Get text from longest line in whole text
            Size LineLength = Utils.MeasureLineLenght(CanvasDevice.GetSharedDevice(), TotalLines[Utils.GetLongestLineIndex(TotalLines)], TextFormat);

            //Measure horizontal Width of longest line and apply to scrollbar
            HorizontalScrollbar.Maximum = LineLength.Width <= sender.ActualWidth ? 0 : LineLength.Width - sender.ActualWidth + 50;
            HorizontalScrollbar.ViewportSize = sender.ActualWidth;

            //Create the textlayout --> apply the Syntaxhighlighting --> render it
            DrawnTextLayout = TextRenderer.CreateTextResource(sender, DrawnTextLayout, TextFormat, RenderedText, new Size { Height = sender.Size.Height, Width = this.ActualWidth });
            UpdateSyntaxHighlighting();
            args.DrawingSession.DrawTextLayout(DrawnTextLayout, (float)(-HorizontalScrollbar.Value), SingleLineHeight, TextColorBrush);

            //UpdateTextHighlights(args.DrawingSession);
            if (_ShowLineNumbers)
                Canvas_LineNumber.Invalidate();
        }
        private void Canvas_Selection_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            if (selectionrenderer.SelectionStartPosition != null && selectionrenderer.SelectionEndPosition != null)
            {
                selectionrenderer.HasSelection =
                    !(selectionrenderer.SelectionStartPosition.LineNumber == selectionrenderer.SelectionEndPosition.LineNumber &&
                    selectionrenderer.SelectionStartPosition.CharacterPosition == selectionrenderer.SelectionEndPosition.CharacterPosition);
            }
            else
                selectionrenderer.HasSelection = false;

            if (selectionrenderer.HasSelection)
            {
                TextSelection = selectionrenderer.DrawSelection(DrawnTextLayout, RenderedLines, args, (float)-HorizontalScrollbar.Value, SingleLineHeight / 4, NumberOfUnrenderedLinesToRenderStart, RenderedLines.Count, new ScrollBarPosition(HorizontalScrollbar.Value, VerticalScrollbar.Value));
            }

            if (TextSelection != null && !Selection.Equals(OldTextSelection, TextSelection))
            {
                OldTextSelection = new TextSelection(TextSelection);
                Internal_CursorChanged();
            }
        }
        private void Canvas_Cursor_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            CurrentLine = GetCurrentLine();
            if (CurrentLine == null || DrawnTextLayout == null)
                return;

            if(CursorPosition.LineNumber > TotalLines.Count)
            {
                CursorPosition.LineNumber = TotalLines.Count;
            }

            //Calculate the distance to the top for the cursorposition and render the cursor
            float RenderPosY = (float)((CursorPosition.LineNumber - NumberOfUnrenderedLinesToRenderStart - 1) * SingleLineHeight) + SingleLineHeight / 4;
            //Out of display-region:
            if (RenderPosY > RenderedLines.Count * SingleLineHeight || RenderPosY < 0)
                return;

            UpdateCurrentLineTextLayout();
            float OffsetX = (float)-HorizontalScrollbar.Value;
            CursorRenderer.RenderCursor(CurrentLineTextLayout, CursorPosition.CharacterPosition, OffsetX, RenderPosY, ZoomedFontSize, args, CursorColorBrush);
            if (_ShowLineHighlighter && SelectionIsNull())
            {
                LineHighlighter.Render((float)sender.ActualWidth, CurrentLineTextLayout, OffsetX, RenderPosY, ZoomedFontSize, args, LineHighlighterBrush);
            }

            if (!Cursor.Equals(CursorPosition, OldCursorPosition))
            {
                OldCursorPosition = new CursorPosition(CursorPosition);
                Internal_CursorChanged();
            }
        }
        private void Canvas_LineNumber_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            if (LineNumberTextToRender.Length == 0 || !_ShowLineNumbers)
                return;

            CanvasTextLayout LineNumberLayout = TextRenderer.CreateTextLayout(sender, LineNumberTextFormat, LineNumberTextToRender, (float)sender.Size.Width, (float)sender.Size.Height);
            args.DrawingSession.DrawTextLayout(LineNumberLayout, 0, SingleLineHeight, LineNumberColorBrush);
        }

        //Internal events:
        private void Internal_TextChanged(string text = null)
        {
            TextChanged?.Invoke(this, text == null ? GetText() : text);
        }
        private void Internal_CursorChanged()
        {
            SelectionChangedEventHandler args = new SelectionChangedEventHandler
            {
                CharacterPositionInLine = CursorPosition.CharacterPosition + 1,
                LineNumber = CursorPosition.LineNumber,
            };
            if (selectionrenderer.SelectionStartPosition != null && selectionrenderer.SelectionEndPosition != null)
            {
                var Sel = Selection.GetIndexOfSelection(TotalLines, new TextSelection(selectionrenderer.SelectionStartPosition, selectionrenderer.SelectionEndPosition));
                args.SelectionLength = Sel.Length;
                args.SelectionStartIndex = 1 + Sel.Index;
            }
            else
            {
                args.SelectionLength = 0;
                args.SelectionStartIndex = 1 + Cursor.CursorPositionToIndex(TotalLines, CursorPosition);
            }
            SelectionChanged?.Invoke(this, args);
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
            ChangeCursor(CoreCursorType.Arrow);
        }
        private void Canvas_LineNumber_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            ChangeCursor(CoreCursorType.IBeam);
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
            CursorPosition pos = selectionrenderer.SelectionEndPosition = new CursorPosition(ListHelper.GetLine(TotalLines, index - 1).Length, index - 1);
            CursorPosition.LineNumber = index;
            CursorPosition.CharacterPosition = CursorAtStart ? 0 : pos.CharacterPosition;

            UpdateSelection();
            UpdateCursor();
        }
        public void SetText(string text)
        {
            TotalLines.Clear();
            RenderedLines.Clear();
            UndoRedo.ClearStacks();
            selectionrenderer.ClearSelection();

            //Get the LineEnding
            LineEnding = LineEndings.FindLineEnding(text);

            //Split the lines by the current LineEnding
            var lines = text.Split(NewLineCharacter);
            for (int i = 0; i < lines.Length; i++)
            {
                TotalLines.Add(new Line(lines[i]));
            }

            Debug.WriteLine("Loaded " + lines.Length + " lines with the lineending " + LineEnding.ToString());
            Internal_TextChanged(text);
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
                ClearSelection();
                UpdateCursor();
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
            if (EndPosition == null || (StartPosition.LineNumber == EndPosition.LineNumber && StartPosition.CharacterPosition == EndPosition.CharacterPosition))
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
            CursorPosition = selectionrenderer.SelectionEndPosition = new CursorPosition(ListHelper.GetLine(TotalLines, -1).Length, TotalLines.Count);
            selectionrenderer.HasSelection = true;
            Canvas_Selection.Invalidate();
        }
        public void Undo()
        {
            var sel = UndoRedo.Undo(TotalLines, this, NewLineCharacter);
            Internal_TextChanged();
            if (sel == null)
                return;

            Debug.WriteLine(sel.ToString());
            
            selectionrenderer.SelectionStartPosition = sel.StartPosition;
            selectionrenderer.SelectionEndPosition = sel.EndPosition;
            selectionrenderer.HasSelection = true;
            selectionrenderer.IsSelecting = false;
            UpdateText();
            UpdateSelection();
        }
        public void Redo()
        {
            UndoRedo.Redo(TotalLines, this);
            Internal_TextChanged();
        }
        public void ScrollOneLineUp()
        {
            VerticalScrollbar.Value -= SingleLineHeight;
        }
        public void ScrollOneLineDown()
        {
            ScrollEventsLocked = true;
            VerticalScrollbar.Value += SingleLineHeight;
            ScrollEventsLocked = false;
        }
        public void ScrollLineIntoView(int Line)
        {
            VerticalScrollbar.Value = Line * SingleLineHeight;
        }
        public void ScrollTopIntoView()
        {
            VerticalScrollbar.Value = (CursorPosition.LineNumber - 1) * SingleLineHeight;
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
        public float SpaceBetweenLineNumberAndText = 20;
        public CursorPosition CursorPosition
        {
            get => _CursorPosition;
            set { _CursorPosition = new CursorPosition(value.CharacterPosition, value.LineNumber + (int)(VerticalScrollbar.Value / SingleLineHeight)); UpdateCursor(); }
        }
        public new int FontSize { get => _FontSize; set { _FontSize = value; UpdateZoom(); } }
        public string Text { get => GetText(); set { SetText(value);} }
        public Color TextColor = Color.FromArgb(255, 255, 255, 255);
        public Color SelectionColor = Color.FromArgb(100, 0, 100, 255);
        public Color CursorColor = Color.FromArgb(255, 255, 255, 255);
        public Color LineNumberColor = Color.FromArgb(255, 150, 150, 150);
        public Color LineHighlighterColor = Color.FromArgb(50, 0, 0, 0);
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
        public bool ShowLineHighlighter
        {
            get => _ShowLineHighlighter;
            set { _ShowLineHighlighter = value; UpdateCursor(); }
        }
        public int ZoomFactor { get => _ZoomFactor; set { _ZoomFactor = value; UpdateZoom(); } } //%

        //Events:
        public delegate void TextChangedEvent(TextControlBox sender, string Text);
        public event TextChangedEvent TextChanged;
        public delegate void SelectionChangedEvent(TextControlBox sender, SelectionChangedEventHandler args);
        public event SelectionChangedEvent SelectionChanged;
        public delegate void ZoomChangedEvent(TextControlBox sender, int ZoomFactor);
        public event ZoomChangedEvent ZoomChanged;
    }
     
    public class Line
    {
        private string _Content = "";
        public string Content { get => _Content; set { _Content = value; this.Length = value.Length; } }
        public int Length { get; private set; }

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
            if (Position < 0)
                Position = 0;

            if (Position >= Content.Length)
                Content += Value;
            else if (Length <= 0)
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
            if (Index >= Length || Index < 0)
                return Content;

            try
            {
                if (Count == -1)
                    Content = Content.Remove(Index);
                else
                    Content = Content.Remove(Index, Count);
            }
            catch (ArgumentOutOfRangeException)
            {
            }
            return Content;
        }
        public string Substring(int Index, int Count = -1)
        {
            if (Index >= Length)
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

            if (start == 0 && end >= Length)
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

    public class SelectionChangedEventHandler
    {
        public int CharacterPositionInLine;
        public int LineNumber;
        public int SelectionStartIndex;
        public int SelectionLength;
    }
}
public static class Extensions
{
    public static string RemoveFirstOccurence(this string value, string removeString)
    {
        int index = value.IndexOf(removeString, StringComparison.Ordinal);
        return index < 0 ? value : value.Remove(index, removeString.Length);
    }
    public static string ToDelimitedString<T>(this IEnumerable<T> source, string delimiter, Func<T, string> func)
    {
        return String.Join(delimiter, source.Select(func).ToArray());
    }
}