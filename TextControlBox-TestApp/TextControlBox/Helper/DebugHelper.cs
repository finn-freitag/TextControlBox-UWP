﻿using System.Collections.Generic;
using System.Diagnostics;

namespace TextControlBox_TestApp.TextControlBox.Helper
{
    public class DebugHelper
    {
        public static void DebugList(List<Line> List, string DebugTitle = "--DebugList--")
        {
            Debug.WriteLine(DebugTitle);
            if (List == null)
            {
                Debug.WriteLine("\tCan't debug List because it is null");
                return;
            }
            for (int i = 0; i < List.Count; i++)
            {
                Debug.WriteLine("\t" + List[i].Content);
            }
        }
    }
}
