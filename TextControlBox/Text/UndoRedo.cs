﻿using Collections.Pooled;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using TextControlBox.Helper;
using static System.Collections.Specialized.BitVector32;

namespace TextControlBox.Text
{
    public class UndoRedo
    {
        private Stack<UndoRedoItem> UndoStack = new Stack<UndoRedoItem>();
        private Stack<UndoRedoItem> RedoStack = new Stack<UndoRedoItem>();

        private bool HasRedone = false;

        private void RecordRedo(UndoRedoItem item)
        {
            RedoStack.Push(item);
        }
        private void RecordUndo(UndoRedoItem item)
        {
            UndoStack.Push(item);
        }

        private void AddUndoItem(TextSelection selection, int startLine, string undoText, string redoText, int undoCount, int redoCount)
        {
            UndoStack.Push(new UndoRedoItem
            {
                RedoText = redoText,
                UndoText = undoText,
                Selection = selection,
                StartLine = startLine,
                UndoCount = undoCount,
                RedoCount = redoCount,
            });
        }

        private void RecordSingleLine(Action action, PooledList<Line> TotalLines, int startline)
        {
            var lineBefore = ListHelper.GetLine(TotalLines, startline).Content;
            action.Invoke();
            var lineAfter = ListHelper.GetLine(TotalLines, startline).Content;
            AddUndoItem(null, startline, lineBefore, lineAfter, 1, 1);
        }

        public void RecordUndoAction(Action action, PooledList<Line> TotalLines, int startline, int undocount, int redoCount, string NewLineCharacter)
        {
            if (undocount == redoCount && redoCount == 1)
            {
                RecordSingleLine(action, TotalLines, startline);
                return;
            }

            var linesBefore = ListHelper.GetLinesAsString(TotalLines, startline, undocount, NewLineCharacter);
            action.Invoke();
            var linesAfter = ListHelper.GetLinesAsString(TotalLines, startline, redoCount, NewLineCharacter);

            AddUndoItem(null, startline, linesBefore, linesAfter, undocount, redoCount);
        }
        public void RecordUndoAction(Action action, PooledList<Line> TotalLines, TextSelection selection, int NumberOfAddedLines, string NewLineCharacter)
        {
            var orderedSel = Selection.OrderTextSelection(selection);
            if (orderedSel.StartPosition.LineNumber == orderedSel.EndPosition.LineNumber && orderedSel.StartPosition.LineNumber == 1)
            {
                RecordSingleLine(action, TotalLines, orderedSel.StartPosition.LineNumber);
                return;
            }

            int NumberOfRemovedLines = orderedSel.EndPosition.LineNumber - orderedSel.StartPosition.LineNumber + 1;
            if (NumberOfAddedLines == 0 && !Selection.WholeLinesAreSelected(selection, TotalLines))
            {
                NumberOfAddedLines += 1;
            }

            var linesBefore = ListHelper.GetLinesAsString(TotalLines, orderedSel.StartPosition.LineNumber, NumberOfRemovedLines, NewLineCharacter);
            action.Invoke();
            var linesAfter = ListHelper.GetLinesAsString(TotalLines, orderedSel.StartPosition.LineNumber, NumberOfAddedLines, NewLineCharacter);

            AddUndoItem(
                selection,
                orderedSel.StartPosition.LineNumber,
                linesBefore,
                linesAfter,
                NumberOfRemovedLines,
                NumberOfAddedLines
                );
        }

        /// <summary>
        /// Excecutes the undo and applys the changes to the text
        /// </summary>
        /// <param name="TotalLines">A list containing all the lines of the textbox</param>
        /// <param name="NewLineCharacter">The current line-ending character either CR, LF or CRLF</param>
        /// <returns>A class containing the start and end-position of the selection</returns>
        public TextSelection Undo(PooledList<Line> TotalLines, StringManager StringManager, string NewLineCharacter)
        {
            if (UndoStack.Count < 1)
                return null;

            if (HasRedone)
            {
                HasRedone = false;
                RedoStack.Clear();
            }

            UndoRedoItem item = UndoStack.Pop();
            RecordRedo(item);

            //Faster for singleline
            if (item.UndoCount == 1 && item.RedoCount == 1)
            {
                ListHelper.GetLine(TotalLines, item.StartLine).Content = StringManager.CleanUpString(item.UndoText);
            }
            else
            {
                ListHelper.RemoveRange(TotalLines, item.StartLine, item.RedoCount);
                if (item.UndoCount > 0)
                    ListHelper.InsertRange(TotalLines, ListHelper.GetLinesFromString(StringManager.CleanUpString(item.UndoText), NewLineCharacter), item.StartLine);

                //Selection.ReplaceLines(TotalLines, item.StartLine, item.RedoCount, StringManager.CleanUpString(Decompress(item.UndoText)).Split(NewLineCharacter));
            }

            return item.Selection;
        }

        /// <summary>
        /// Excecutes the redo and apply the changes to the text
        /// </summary>
        /// <param name="TotalLines">A list containing all the lines of the textbox</param>
        /// <param name="NewLineCharacter">The current line-ending character either CR, LF or CRLF</param>
        /// <returns>A class containing the start and end-position of the selection</returns>
        public TextSelection Redo(PooledList<Line> TotalLines, StringManager StringManager, string NewLineCharacter)
        {
            if (RedoStack.Count < 1)
                return null;

            UndoRedoItem item = RedoStack.Pop();
            RecordUndo(item);
            HasRedone = true;

            //Faster for singleline
            if (item.UndoCount == 1 && item.RedoCount == 1)
            {
                ListHelper.GetLine(TotalLines, item.StartLine).Content = StringManager.CleanUpString(item.RedoText);
            }
            else
            {
                ListHelper.RemoveRange(TotalLines, item.StartLine, item.UndoCount);
                if (item.RedoCount > 0)
                    ListHelper.InsertRange(TotalLines, ListHelper.GetLinesFromString(StringManager.CleanUpString(item.RedoText), NewLineCharacter), item.StartLine);

                //Selection.ReplaceLines(TotalLines, item.StartLine, item.UndoCount, StringManager.CleanUpString(Decompress(item.RedoText)).Split(NewLineCharacter));
            }
            return null;
        }

        /// <summary>
        /// Clears all the items in the undo and redo stack
        /// </summary>
        public void ClearAll()
        {
            UndoStack.Clear();
            RedoStack.Clear();
            UndoStack.TrimExcess();
            RedoStack.TrimExcess();
        }

        public void NullAll()
        {
            UndoStack = null;
            RedoStack = null;
        }

        /// <summary>
        /// Gets if the undo stack contains actions
        /// </summary>
        public bool CanUndo { get => UndoStack.Count > 0; }

        /// <summary>
        /// Gets if the redo stack contains actions
        /// </summary>
        public bool CanRedo { get => RedoStack.Count > 0; }
    }
    public struct UndoRedoItem
    {
        public int StartLine { get; set; }
        public string UndoText { get; set; }
        public string RedoText { get; set; }
        public int UndoCount { get; set; }
        public int RedoCount { get; set; }
        public TextSelection Selection { get; set; }
    }
}
