﻿using Collections.Pooled;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.ConstrainedExecution;
using System.Text;
using TextControlBox.Helper;

namespace TextControlBox.Text
{
    public class Selection
    {
        public static bool Equals(TextSelection Sel1, TextSelection Sel2)
        {
            if (Sel1 == null || Sel2 == null)
                return false;

            return Cursor.Equals(Sel1.StartPosition, Sel2.StartPosition) &&
                Cursor.Equals(Sel1.EndPosition, Sel2.EndPosition);
        }

        //Order the selection that StartPosition is always smaller than EndPosition
        public static TextSelection OrderTextSelection(TextSelection Selection)
        {
            if (Selection == null)
                return Selection;

            int StartLine = Math.Min(Selection.StartPosition.LineNumber, Selection.EndPosition.LineNumber);
            int EndLine = Math.Max(Selection.StartPosition.LineNumber, Selection.EndPosition.LineNumber);
            int StartPosition;
            int EndPosition;
            if (StartLine == EndLine)
            {
                StartPosition = Math.Min(Selection.StartPosition.CharacterPosition, Selection.EndPosition.CharacterPosition);
                EndPosition = Math.Max(Selection.StartPosition.CharacterPosition, Selection.EndPosition.CharacterPosition);
            }
            else
            {
                if (Selection.StartPosition.LineNumber < Selection.EndPosition.LineNumber)
                {
                    EndPosition = Selection.EndPosition.CharacterPosition;
                    StartPosition = Selection.StartPosition.CharacterPosition;
                }
                else
                {
                    EndPosition = Selection.StartPosition.CharacterPosition;
                    StartPosition = Selection.EndPosition.CharacterPosition;
                }
            }

            return new TextSelection(Selection.Index, Selection.Length, new CursorPosition(StartPosition, StartLine), new CursorPosition(EndPosition, EndLine));
        }

        public static bool WholeTextSelected(TextSelection Selection, PooledList<Line> TotalLines)
        {
            if (Selection == null)
                return false;

            return Utils.CursorPositionsAreEqual(Selection.StartPosition, new CursorPosition(0, 0)) &&
                Utils.CursorPositionsAreEqual(Selection.EndPosition, new CursorPosition(ListHelper.GetLine(TotalLines, -1).Length, TotalLines.Count));
        }

        public static CursorPosition GetMax(CursorPosition Pos1, CursorPosition Pos2)
        {
            if (Pos1.LineNumber == Pos2.LineNumber)
                return Pos1.CharacterPosition > Pos2.CharacterPosition ? Pos2 : Pos1;
            return Pos1.LineNumber > Pos2.LineNumber ? Pos1 : Pos2;
        }
        public static CursorPosition GetMin(CursorPosition Pos1, CursorPosition Pos2)
        {
            if (Pos1.LineNumber == Pos2.LineNumber)
                return Pos1.CharacterPosition > Pos2.CharacterPosition ? Pos1 : Pos2;
            return Pos1.LineNumber > Pos2.LineNumber ? Pos2 : Pos1;
        }
        public static CursorPosition GetMin(TextSelection Selection)
        {
            return GetMin(Selection.StartPosition, Selection.EndPosition);
        }
        public static CursorPosition GetMax(TextSelection Selection)
        {
            return GetMax(Selection.StartPosition, Selection.EndPosition);
        }

        public static CursorPosition InsertText(TextSelection Selection, CursorPosition CursorPosition, PooledList<Line> TotalLines, string Text, string NewLineCharacter)
        {
            if (Selection != null)
                return Replace(Selection, TotalLines, Text, NewLineCharacter);

            string[] lines = Text.Split(NewLineCharacter);
            Line CurrentLine = ListHelper.GetLine(TotalLines, CursorPosition.LineNumber);

            //Singleline
            if (lines.Length == 1 && Text != string.Empty)
            {
                Text = Text.Replace("\r", string.Empty).Replace("\n", string.Empty);
                ListHelper.GetLine(TotalLines, -1).AddText(Text, CursorPosition.CharacterPosition);
                CursorPosition.AddToCharacterPos(Text.Length);
                return CursorPosition;
            }

            //Multiline:

            //Get the text in front of the cursor
            int CurPos = CursorPosition.CharacterPosition;

            if (CurPos > CurrentLine.Length)
                CurPos = CurrentLine.Length;
            string TextInFrontOfCursor = CurrentLine.Content.Substring(0, CurPos < 0 ? 0 : CurPos);

            //Get the text behind the cursor
            if (CurPos > CurrentLine.Length)
                CurPos = CurrentLine.Length;
            string TextBehindCursor = CurrentLine.Content.Remove(0, CurPos < 0 ? 0 : CurPos);

            ListHelper.DeleteAt(TotalLines, CursorPosition.LineNumber);

            using (PooledList<Line> LinesToInsert = new PooledList<Line>(lines.Length))
            {
                //Paste the text
                for (int i = 0; i < lines.Length; i++)
                {
                    if (i == 0)
                        LinesToInsert.Add(new Line(TextInFrontOfCursor + lines[i]));
                    else if (i == lines.Length - 1)
                        LinesToInsert.Add(new Line(lines[i] + TextBehindCursor));
                    else
                        LinesToInsert.Add(new Line(lines[i]));
                }
                ListHelper.InsertRange(TotalLines, LinesToInsert, CursorPosition.LineNumber);
            }
            return new CursorPosition(CursorPosition.CharacterPosition + lines.Length > 0 ? lines[lines.Length - 1].Length : 0, CursorPosition.LineNumber + lines.Length - 1);
        }

        public static CursorPosition Replace(TextSelection Selection, PooledList<Line> TotalLines, string Text, string NewLineCharacter)
        {
            //Just delete the text if the string is emty
            if (Text == "")
            {
                return Remove(Selection, TotalLines);
            }

            Selection = OrderTextSelection(Selection);
            int StartLine = Selection.StartPosition.LineNumber;
            int EndLine = Selection.EndPosition.LineNumber;
            int StartPosition = Selection.StartPosition.CharacterPosition;
            int EndPosition = Selection.EndPosition.CharacterPosition;

            string[] SplittedText = Text.Split(NewLineCharacter);
            Line Start_Line = ListHelper.GetLine(TotalLines, StartLine);

            //Selection is singleline and text to paste is also singleline
            if (StartLine == EndLine && SplittedText.Length == 1)
            {
                if (StartPosition == 0 && EndPosition == ListHelper.GetLine(TotalLines, EndLine).Length)
                    Start_Line.Content = "";
                else
                    Start_Line.Remove(StartPosition, EndPosition - StartPosition);

                Start_Line.AddText(Text, StartPosition);
                return new CursorPosition(StartPosition + Text.Length, Selection.StartPosition.LineNumber);
            }
            else if (StartLine == EndLine && SplittedText.Length > 1)
            {
                if (StartPosition == 0 && EndPosition == ListHelper.GetLine(TotalLines, EndLine).Length)
                    Start_Line.Content = "";

                string TextTo = Start_Line.Content == "" ? "" : Start_Line.Content.Substring(0, StartPosition);
                string TextFrom = Start_Line.Content == "" ? "" : Start_Line.Content.Substring(EndPosition);

                using (PooledList<Line> Lines = new PooledList<Line>(SplittedText.Length))
                {
                    for (int i = 0; i < SplittedText.Length; i++)
                    {
                        if (i == 0)
                            Start_Line.SetText(TextTo + SplittedText[i]);
                        else if (i == SplittedText.Length - 1)
                            Lines.Add(new Line(SplittedText[i] + TextFrom));
                        else
                            Lines.Add(new Line(SplittedText[i]));
                    }
                    ListHelper.InsertRange(TotalLines, Lines, StartLine + 1);
                    return new CursorPosition(EndPosition + Text.Length, StartLine + SplittedText.Length - 1);
                }
            }
            else if (WholeTextSelected(Selection, TotalLines))
            {
                ListHelper.Clear(TotalLines);
                using (PooledList<Line> LinesToAdd = new PooledList<Line>(SplittedText.Length))
                {
                    for (int i = 0; i < SplittedText.Length; i++)
                        LinesToAdd.Add(new Line(SplittedText[i]));

                    TotalLines.AddRange(LinesToAdd);

                    if (TotalLines.Count == 0)
                        TotalLines.Add(new Line());

                    return new CursorPosition(ListHelper.GetLine(TotalLines, -1).Length, TotalLines.Count - 1);
                }
            }
            else
            {
                using (PooledList<Line> LinesToInsert = new PooledList<Line>(SplittedText.Length))
                {
                    Line End_Line = ListHelper.GetLine(TotalLines, EndLine);
                    int InsertPosition = StartLine;
                    //all lines are selected from start to finish
                    if (StartPosition == 0 && EndPosition == End_Line.Length)
                    {
                        ListHelper.RemoveRange(TotalLines, StartLine, EndLine - StartLine + 1);

                        for (int i = 0; i < SplittedText.Length; i++)
                        {
                            LinesToInsert.Add(new Line(SplittedText[i]));
                        }
                        StartLine -= 1;
                    }
                    //Only the startline is completely selected
                    else if (StartPosition == 0 && EndPosition != End_Line.Length)
                    {
                        End_Line.Substring(EndPosition);
                        ListHelper.RemoveRange(TotalLines, StartLine, EndLine - StartLine);

                        for (int i = 0; i < SplittedText.Length; i++)
                        {
                            if (i == SplittedText.Length - 1)
                                End_Line.AddToStart(SplittedText[i]);
                            else
                                LinesToInsert.Add(new Line(SplittedText[i]));
                        }
                        StartLine -= 1;
                    }
                    //Only the endline is completely selected
                    else if (StartPosition != 0 && EndPosition == End_Line.Length)
                    {
                        Start_Line.Remove(StartPosition);
                        ListHelper.RemoveRange(TotalLines, StartLine + 1, EndLine - StartLine);

                        for (int i = 0; i < SplittedText.Length; i++)
                        {
                            if (i == 0)
                                Start_Line.AddToEnd(SplittedText[i]);
                            else
                                LinesToInsert.Add(new Line(SplittedText[i]));
                        }
                    }
                    else
                    {
                        Start_Line.Remove(StartPosition);
                        End_Line.Substring(EndPosition);

                        int Remove = EndLine - StartLine - 1;
                        ListHelper.RemoveRange(TotalLines, StartLine + 1, Remove < 0 ? 0 : Remove);

                        if (SplittedText.Length == 1)
                        {
                            Start_Line.AddToEnd(SplittedText[0] + End_Line.Content);
                            TotalLines.Remove(End_Line);
                        }
                        else
                        {
                            for (int i = 0; i < SplittedText.Length; i++)
                            {
                                if (i == 0)
                                    Start_Line.AddToEnd(SplittedText[i]);
                                if (i != 0 && i != SplittedText.Length - 1)
                                    LinesToInsert.Add(new Line(SplittedText[i]));
                                if (i == SplittedText.Length - 1)
                                {
                                    End_Line.AddToStart(SplittedText[i]);
                                }
                            }
                        }
                    }
                    if (LinesToInsert.Count > 0)
                    {
                        ListHelper.InsertRange(TotalLines, LinesToInsert, StartLine + 1);
                    }

                    return new CursorPosition(Start_Line.Length + End_Line.Length - 1, InsertPosition + SplittedText.Length - 1);
                }
            }
        }

        public static CursorPosition Remove(TextSelection Selection, PooledList<Line> TotalLines)
        {
            Selection = OrderTextSelection(Selection);
            int StartLine = Selection.StartPosition.LineNumber;
            int EndLine = Selection.EndPosition.LineNumber;
            int StartPosition = Selection.StartPosition.CharacterPosition;
            int EndPosition = Selection.EndPosition.CharacterPosition;

            Line Start_Line = ListHelper.GetLine(TotalLines, StartLine);
            Line End_Line = ListHelper.GetLine(TotalLines, EndLine);

            if (StartLine == EndLine)
            {
                if (StartPosition == 0 && EndPosition == End_Line.Length)
                    End_Line.Content = "";
                else
                    Start_Line.Remove(StartPosition, EndPosition - StartPosition);
            }
            else if (WholeTextSelected(Selection, TotalLines))
            {
                ListHelper.Clear(TotalLines, true);
                return new CursorPosition(0, TotalLines.Count - 1);
            }
            else
            {
                //Whole lines are selected from start to finish
                if (StartPosition == 0 && EndPosition == End_Line.Length)
                {
                    ListHelper.RemoveRange(TotalLines, StartLine, EndLine - StartLine + 1);
                }
                //Only the startline is completely selected
                else if (StartPosition == 0 && EndPosition != End_Line.Length)
                {
                    End_Line.Substring(EndPosition);
                    ListHelper.RemoveRange(TotalLines, StartLine, EndLine - StartLine);
                }
                //Only the endline is completely selected
                else if (StartPosition != 0 && EndPosition == End_Line.Length)
                {
                    Start_Line.Remove(StartPosition);
                    ListHelper.RemoveRange(TotalLines, StartLine + 1, EndLine - StartLine);
                }
                //Both startline and endline are not completely selected
                else
                {
                    Start_Line.Remove(StartPosition);
                    Start_Line.Content += End_Line.Content.Substring(EndPosition);
                    ListHelper.RemoveRange(TotalLines, StartLine + 1, EndLine - StartLine);
                }
            }

            if (TotalLines.Count == 0)
                TotalLines.Add(new Line());

            return new CursorPosition(StartPosition, StartLine);
        }

        public static TextSelectionPosition GetIndexOfSelection(PooledList<Line> TotalLines, TextSelection Selection)
        {
            var Sel = OrderTextSelection(Selection);
            int StartIndex = Cursor.CursorPositionToIndex(TotalLines, Sel.StartPosition);
            int EndIndex = Cursor.CursorPositionToIndex(TotalLines, Sel.EndPosition);

            int SelectionLength;
            if (EndIndex > StartIndex)
                SelectionLength = EndIndex - StartIndex;
            else
                SelectionLength = StartIndex - EndIndex;

            return new TextSelectionPosition(Math.Min(StartIndex, EndIndex), SelectionLength);
        }

        public static TextSelection GetSelectionFromPosition(PooledList<Line> TotalLines, int StartPosition, int Length, int NumberOfCharacters)
        {
            TextSelection returnValue = new TextSelection();

            if (StartPosition + Length > NumberOfCharacters)
            {
                if (StartPosition > NumberOfCharacters)
                {
                    StartPosition = NumberOfCharacters;
                    Length = 0;
                }
                else
                {
                    Length = NumberOfCharacters - StartPosition;
                }
            }

            void GetIndexInLine(int CurrentIndex, int CurrentTotalLength)
            {
                int Position = Math.Abs(CurrentTotalLength - StartPosition);

                returnValue.StartPosition =
                    new CursorPosition(Position, CurrentIndex);

                if (Length == 0)
                    returnValue.EndPosition = new CursorPosition(returnValue.StartPosition);
                else
                {
                    int LengthCount = 0;
                    for (int i = CurrentIndex; i < TotalLines.Count; i++)
                    {
                        int lineLength = TotalLines[i].Length + 1;
                        if (LengthCount + lineLength > Length)
                        {
                            returnValue.EndPosition = new CursorPosition(Math.Abs(LengthCount - Length) + Position, i);
                            break;
                        }
                        LengthCount += lineLength;
                    }
                }
            }

            //Get the Length
            int TotalLength = 0;
            for (int i = 0; i < TotalLines.Count; i++)
            {
                int lineLength = TotalLines[i].Length + 1;
                if (TotalLength + lineLength > StartPosition)
                {
                    GetIndexInLine(i, TotalLength);
                    break;
                }

                TotalLength += lineLength;
            }
            return returnValue;
        }

        //Returns the whole lines, without respecting the characterposition of the selection
        public static PooledList<Line> GetPointerToSelectedLines(PooledList<Line> TotalLines, TextSelection Selection)
        {
            if (Selection == null)
                return null;

            int StartLine = Math.Min(Selection.StartPosition.LineNumber, Selection.EndPosition.LineNumber);
            int EndLine = Math.Max(Selection.StartPosition.LineNumber, Selection.EndPosition.LineNumber);

            if (EndLine == StartLine)
            {
                return ListHelper.GetLines(TotalLines, StartLine, 1);
            }
            else
            {
                int Count = EndLine - StartLine + (EndLine - StartLine + 1 < TotalLines.Count ? 1 : 0);
                return ListHelper.GetLines(TotalLines, StartLine, Count);
            }
        }
        //Get the selected lines as a new Line without respecting the characterposition
        public static PooledList<Line> GetCopyOfSelectedLines(PooledList<Line> TotalLines, TextSelection Selection)
        {
            if (Selection == null)
                return null;

            int StartLine = Math.Min(Selection.StartPosition.LineNumber, Selection.EndPosition.LineNumber);
            int EndLine = Math.Max(Selection.StartPosition.LineNumber, Selection.EndPosition.LineNumber);

            //Get the items into the list CurrentItems
            PooledList<Line> CurrentItems;
            if (EndLine == StartLine)
            {
                CurrentItems = ListHelper.GetLines(TotalLines, StartLine, 1);
            }
            else
            {
                int Count = EndLine - StartLine + 1;
                if (StartLine + Count >= TotalLines.Count)
                    Count = TotalLines.Count - StartLine;

                CurrentItems = ListHelper.GetLines(TotalLines, StartLine, Count);
            }

            //Create new items with same content from CurrentItems into NewItems
            using (PooledList<Line> NewItems = new PooledList<Line>())
            {
                for (int i = 0; i < CurrentItems.Count; i++)
                {
                    NewItems.Add(new Line(CurrentItems[i].Content));
                }
                CurrentItems = null;
                return NewItems;
            }
        }

        public static string GetSelectedTextWithoutCharacterPos(PooledList<Line> TotalLines, TextSelection TextSelection, string NewLineCharacter)
        {
            if (TextSelection == null)
                return null;

            int StartLine = Math.Min(TextSelection.StartPosition.LineNumber, TextSelection.EndPosition.LineNumber);
            int EndLine = Math.Max(TextSelection.StartPosition.LineNumber, TextSelection.EndPosition.LineNumber);

            //Get the items into the list CurrentItems
            if (EndLine == StartLine)
            {
                return ListHelper.GetLine(TotalLines, StartLine).Content;
            }
            else
            {
                int Count = EndLine - StartLine + 1;
                if (StartLine + Count >= TotalLines.Count)
                    Count = TotalLines.Count - StartLine;

                return string.Join(NewLineCharacter, ListHelper.GetLines(TotalLines, StartLine, Count).Select(x => x.Content));
            }
        }

        public static string GetSelectedText(PooledList<Line> TotalLines, TextSelection TextSelection, int CurrentLineIndex, string NewLineCharacter)
        {
            //return the current line, if no text is selected:
            if (TextSelection == null)
            {
                return ListHelper.GetLine(TotalLines, CurrentLineIndex).Content;
            }

            int StartLine = Math.Min(TextSelection.StartPosition.LineNumber, TextSelection.EndPosition.LineNumber);
            int EndLine = Math.Max(TextSelection.StartPosition.LineNumber, TextSelection.EndPosition.LineNumber);
            int EndIndex = Math.Max(TextSelection.StartPosition.CharacterPosition, TextSelection.EndPosition.CharacterPosition);
            int StartIndex = Math.Min(TextSelection.StartPosition.CharacterPosition, TextSelection.EndPosition.CharacterPosition);

            StringBuilder StringBuilder = new StringBuilder();

            if (StartLine == EndLine) //Singleline
            {
                Line Line = ListHelper.GetLine(TotalLines, StartLine < TotalLines.Count ? StartLine : TotalLines.Count - 1);
                if (StartIndex == 0 && EndIndex != Line.Length)
                    StringBuilder.Append(Line.Content.Remove(EndIndex));
                else if (EndIndex == Line.Length && StartIndex != 0)
                    StringBuilder.Append(Line.Content.Substring(StartIndex));
                else if (StartIndex == 0 && EndIndex == Line.Length)
                    StringBuilder.Append(Line.Content);
                else StringBuilder.Append(Line.Content.Remove(EndIndex).Substring(StartIndex));
            }
            else if (WholeTextSelected(TextSelection, TotalLines))
            {
                return String.Join(NewLineCharacter, TotalLines.Select(x => x.Content));
            }
            else //Multiline
            {
                for (int i = StartLine; i < EndLine + 1; i++)
                {
                    if (i == StartLine)
                        StringBuilder.Append(ListHelper.GetLine(TotalLines, StartLine).Content.Substring(StartIndex) + NewLineCharacter);
                    else if (i == EndLine)
                    {
                        Line CurrentLine = ListHelper.GetLine(TotalLines, EndLine);
                        StringBuilder.Append(EndIndex == CurrentLine.Length ? CurrentLine.Content : CurrentLine.Content.Remove(EndIndex));
                    }
                    else
                        StringBuilder.Append(ListHelper.GetLine(TotalLines, i).Content + NewLineCharacter);
                }
            }
            string text = StringBuilder.ToString();
            StringBuilder.Clear();
            return text;
        }
    }
}
