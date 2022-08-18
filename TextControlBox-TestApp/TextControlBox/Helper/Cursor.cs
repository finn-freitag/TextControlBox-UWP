﻿using System.Collections.Generic;
using System.Diagnostics;
using Windows.UI.Core;
using Windows.UI.Xaml;

namespace TextControlBox_TestApp.TextControlBox.Helper
{
    public class CursorPosition
    {
        public CursorPosition(int CharacterPosition = 0, int LineNumber = 0)
        {
            this.CharacterPosition = CharacterPosition;
            this.LineNumber = LineNumber;
        }
        public CursorPosition(CursorPosition CurrentCursorPosition)
        {
            if (CurrentCursorPosition == null)
                return;
            this.CharacterPosition = CurrentCursorPosition.CharacterPosition;
            this.LineNumber = CurrentCursorPosition.LineNumber;
        }
        public int CharacterPosition { get; set; } = 0;
        public int LineNumber { get; set; } = 0;
        public CursorPosition Change(int CharacterPosition, int LineNumber)
        {
            this.CharacterPosition = CharacterPosition;
            this.LineNumber = LineNumber;
            return this;
        }
        public void AddToCharacterPos(int Add)
        {
            CharacterPosition += Add;
        }
        public void SubtractFromCharacterPos(int Value)
        {
            CharacterPosition -= Value;
        }
        public new string ToString()
        {
            return LineNumber + ":" + CharacterPosition;
        }

        public CursorPosition ChangeLineNumber(int LineNumber)
        {
            this.LineNumber = LineNumber;
            return this;
        }

        public static CursorPosition ChangeCharacterPosition(CursorPosition CurrentCursorPosition, int CharacterPosition)
        {
            return new CursorPosition(CharacterPosition, CurrentCursorPosition.LineNumber);
        }
        public static CursorPosition ChangeLineNumber(CursorPosition CurrentCursorPosition, int LineNumber)
        {
            return new CursorPosition(CurrentCursorPosition.CharacterPosition, LineNumber);
        }
    }

    public class Cursor
    {
        private static bool ControlIsPressed { get => Window.Current.CoreWindow.GetKeyState(Windows.System.VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down); }

        public static int CursorPositionToIndex(List<Line> TotalLines, CursorPosition CursorPosition)
        {
            int CursorIndex = 0;
            for (int i = 0; i < CursorPosition.LineNumber - 1; i++)
            {
                CursorIndex += ListHelper.GetLine(TotalLines, i).Length + 1;
            }
            return CursorIndex + CursorPosition.CharacterPosition;
        }

        public static bool Equals(CursorPosition CurPos1, CursorPosition CurPos2)
        {
            if (CurPos1 == null || CurPos2 == null)
                return false;

            if(CurPos1.LineNumber == CurPos2.LineNumber)
                return CurPos1.CharacterPosition == CurPos2.CharacterPosition;
            return false;
        }

        //Convert the coordinates from Relative to Absolute and the other way around
        public static CursorPosition RelativeToAbsolute(CursorPosition curpos, int nmbOfUnrenderdLines)
        {
            return CursorPosition.ChangeLineNumber(curpos, curpos.LineNumber - nmbOfUnrenderdLines);
        }
        public static CursorPosition AbsoluteToRelative(CursorPosition curpos, int nmbOfUnrenderdLines)
        {
            return CursorPosition.ChangeLineNumber(curpos, curpos.LineNumber + nmbOfUnrenderdLines);
        }

        //Move selectionend:
        public static CursorPosition MoveSelectionLeft(CursorPosition SelectionEndPosition, List<Line> TotalLines)
        {
            CursorPosition ReturnValue = new CursorPosition(SelectionEndPosition);
            if (SelectionEndPosition == null)
                return ReturnValue;


            //If a selection has been started, continue the selection
            int Line = ReturnValue.LineNumber;
            int StepsToMoveLeft = CalculateStepsToMoveLeft(ListHelper.GetLine(TotalLines, Line), SelectionEndPosition.CharacterPosition);

            if (SelectionEndPosition.CharacterPosition == 0)
            {
                ReturnValue.LineNumber = Line > 0 ? Line-- : Line;
                if (Line < ReturnValue.LineNumber)
                {
                    ReturnValue.LineNumber--;
                    ReturnValue.CharacterPosition = ListHelper.GetLine(TotalLines, ReturnValue.LineNumber).Length;
                }
                else
                    ReturnValue.CharacterPosition -= StepsToMoveLeft;
            }
            else
            {
                ReturnValue.CharacterPosition -= StepsToMoveLeft;
            }
            if (ReturnValue.CharacterPosition < 0)
                ReturnValue.CharacterPosition = 0;
            return ReturnValue;
        }
        public static CursorPosition MoveSelectionRight(CursorPosition SelectionEndPosition, List<Line> TotalLines, Line CurrentLine)
        {
            CursorPosition ReturnValue = new CursorPosition(SelectionEndPosition);

            if (SelectionEndPosition == null || CurrentLine == null)
                return ReturnValue;

            int StepsToMoveRight = CalculateStepsToMoveRight(ListHelper.GetLine(TotalLines, SelectionEndPosition.LineNumber), SelectionEndPosition.CharacterPosition);

            if (SelectionEndPosition.CharacterPosition == CurrentLine.Length)
            {
                ReturnValue.LineNumber += SelectionEndPosition.LineNumber < TotalLines.Count ? 1 : 0;
                if (SelectionEndPosition.LineNumber < ReturnValue.LineNumber)
                    ReturnValue.CharacterPosition = 0;
                else
                    ReturnValue.CharacterPosition += StepsToMoveRight;
            }
            else
            {
                ReturnValue.LineNumber = SelectionEndPosition.LineNumber;
                ReturnValue.CharacterPosition += StepsToMoveRight;
            }
            return ReturnValue;
        }
        public static CursorPosition MoveSelectionUp(CursorPosition SelectionEndPosition, List<Line> TotalLines)
        {
            CursorPosition ReturnValue = new CursorPosition(SelectionEndPosition);

            if (SelectionEndPosition == null)
                return ReturnValue;

            if (1 < SelectionEndPosition.LineNumber + 1)
            {
                Line PreviousLine = ListHelper.GetLine(TotalLines, SelectionEndPosition.LineNumber + 1);
                if (PreviousLine == null)
                    return ReturnValue;

                int PreviousLineLenght = PreviousLine.Length;
                ReturnValue.LineNumber--;
                if (PreviousLineLenght > SelectionEndPosition.CharacterPosition)
                    ReturnValue.CharacterPosition = SelectionEndPosition.CharacterPosition;
                else
                    ReturnValue.CharacterPosition = PreviousLineLenght;
            }
            if (ReturnValue.LineNumber < 0)
                ReturnValue.LineNumber = 0;

            return ReturnValue;
        }
        public static CursorPosition MoveSelectionDown(CursorPosition SelectionEndPosition, List<Line> TotalLines)
        {

            CursorPosition ReturnValue = new CursorPosition(SelectionEndPosition);

            if (SelectionEndPosition == null)
                return ReturnValue;

            if (TotalLines.Count > SelectionEndPosition.LineNumber)
            {
                int NextLineLenght = ListHelper.GetLine(TotalLines, SelectionEndPosition.LineNumber).Length;
                ReturnValue.LineNumber++;
                if (NextLineLenght > SelectionEndPosition.CharacterPosition)
                    ReturnValue.CharacterPosition = SelectionEndPosition.CharacterPosition;
                else
                    ReturnValue.CharacterPosition = NextLineLenght;
            }

            return ReturnValue;
        }

        //Calculate the number of characters from the cursorposition to the next character or digit to the left and to the right
        public static int CalculateStepsToMoveLeft2(Line CurrentLine, int CursorCharPosition)
        {
            int Count = 0;
            for (int i = CursorCharPosition - 1; i >= 0; i--)
            {
                char CurrentCharacter = CurrentLine.Content[i < CurrentLine.Length ? i : CurrentLine.Length - 1];
                if (char.IsLetterOrDigit(CurrentCharacter))
                    Count++;
                else if (i == CursorCharPosition - 1 && char.IsWhiteSpace(CurrentCharacter))
                    return 0;
                else
                    break;
            }
            return Count;
        }
        public static int CalculateStepsToMoveRight2(Line CurrentLine, int CursorCharPosition)
        {
            int Count = 0;
            for (int i = CursorCharPosition; i < CurrentLine.Length; i++)
            {
                if (char.IsLetterOrDigit(CurrentLine.Content[i]))
                    Count++;
                else if (i == CursorCharPosition && char.IsWhiteSpace(CurrentLine.Content[i]))
                    return 0;
                else
                    break;
            }
            //If it ignores the ControlKey return the real value of Count otherwise
            //return 1 if Count is 0
            return Count;
        }

        //Calculates how many characters the cursor needs to move if control is pressed
        //Returns 1 if control is not pressed
        public static int CalculateStepsToMoveLeft(Line CurrentLine, int CursorCharPosition)
        {
            if (!ControlIsPressed)
                return 1;
            int Count = 0;
            for (int i = CursorCharPosition - 1; i >= 0; i--)
            {
                char CurrentCharacter = CurrentLine.Content[i < CurrentLine.Length ? i : CurrentLine.Length - 1];
                if (char.IsLetterOrDigit(CurrentCharacter))
                    Count++;
                else if (i == CursorCharPosition - 1 && char.IsWhiteSpace(CurrentCharacter))
                    Count++;
                else
                    break;
            }
            //If it ignores the ControlKey return the real value of Count otherwise
            //return 1 if Count is 0
            return Count == 0 ? 1 : Count;
        }
        public static int CalculateStepsToMoveRight(Line CurrentLine, int CursorCharPosition)
        {
            if (!ControlIsPressed)
                return 1;
            int Count = 0;
            for (int i = CursorCharPosition; i < CurrentLine.Length; i++)
            {
                if (char.IsLetterOrDigit(CurrentLine.Content[i]))
                    Count++;
                else if (i == CursorCharPosition && char.IsWhiteSpace(CurrentLine.Content[i]))
                    Count++;
                else
                    break;
            }
            //If it ignores the ControlKey return the real value of Count otherwise
            //return 1 if Count is 0
            return Count == 0 ? 1 : Count;
        }

        //Move cursor:
        public static CursorPosition MoveLeft(CursorPosition CurrentCursorPosition, List<Line> TotalLines)
        {
            CursorPosition ReturnValue = new CursorPosition(CurrentCursorPosition);
            int Line = CurrentCursorPosition.LineNumber;
            int StepsToMoveLeft = CalculateStepsToMoveLeft(ListHelper.GetLine(TotalLines, Line - 1), CurrentCursorPosition.CharacterPosition);

            if (CurrentCursorPosition.CharacterPosition == 0)
            {
                ReturnValue.LineNumber = Line > 1 ? Line-- : Line;
                if (Line < ReturnValue.LineNumber)
                {
                    ReturnValue.LineNumber -= StepsToMoveLeft;
                    ReturnValue.CharacterPosition = ListHelper.GetLine(TotalLines, ReturnValue.LineNumber - 1).Length;
                }
                else
                    ReturnValue.CharacterPosition -= StepsToMoveLeft;
            }
            else
            {
                ReturnValue.LineNumber = Line;
                ReturnValue.CharacterPosition -= StepsToMoveLeft;
            }
            if (ReturnValue.CharacterPosition < 0)
                ReturnValue.CharacterPosition = 0;

            return ReturnValue;
        }
        public static CursorPosition MoveRight(CursorPosition CurrentCursorPosition, List<Line> TotalLines, Line CurrentLine)
        {
            CursorPosition ReturnValue = new CursorPosition(CurrentCursorPosition);
            int StepsToMoveRight = CalculateStepsToMoveRight(CurrentLine, CurrentCursorPosition.CharacterPosition);
            if (CurrentCursorPosition.CharacterPosition == CurrentLine.Length)
            {
                ReturnValue.LineNumber += CurrentCursorPosition.LineNumber < TotalLines.Count ? 1 : 0;
                if (CurrentCursorPosition.LineNumber < ReturnValue.LineNumber)
                    ReturnValue.CharacterPosition = 0;
                else
                    ReturnValue.CharacterPosition += StepsToMoveRight;
            }
            else
            {
                ReturnValue.LineNumber = CurrentCursorPosition.LineNumber;
                ReturnValue.CharacterPosition += StepsToMoveRight;
            }
            var LineLength = ListHelper.GetLine(TotalLines, ReturnValue.LineNumber - 1).Length;
            if (ReturnValue.CharacterPosition >= LineLength)
                ReturnValue.CharacterPosition = LineLength;
            return ReturnValue;
        }
        public static CursorPosition MoveDown(CursorPosition CurrentCursorPosition, int TotalLinesLength)
        {
            CursorPosition ReturnValue = new CursorPosition(CurrentCursorPosition);
            if (CurrentCursorPosition.LineNumber < TotalLinesLength - 1)
                ReturnValue = CursorPosition.ChangeLineNumber(CurrentCursorPosition, CurrentCursorPosition.LineNumber + 1);
            return ReturnValue;
        }
        public static CursorPosition MoveUp(CursorPosition CurrentCursorPosition)
        {
            CursorPosition ReturnValue = new CursorPosition(CurrentCursorPosition);
            if (CurrentCursorPosition.LineNumber > 0)
                ReturnValue = CursorPosition.ChangeLineNumber(CurrentCursorPosition, CurrentCursorPosition.LineNumber - 1);
            return ReturnValue;
        }
    }
}