﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.UI;

namespace TextControlBox_TestApp.TextControlBox.Languages
{
    public class Html : CodeLanguage
    {
        Color orange = Color.FromArgb(255, 255, 214, 140);
        Color aqua = Color.FromArgb(255, 0, 255, 255);
        Color pink = Color.FromArgb(255, 255, 0, 220);

        public Html()
        {
            Name = "Html";
            //Highlights.Add(new SyntaxHighlights("<.*?>", aqua));
            Highlights.Add(new SyntaxHighlights(@"<+[a-zA-Z0-9]+>", aqua)); //Opening tag 1
            Highlights.Add(new SyntaxHighlights(@"[<]+[a-zA-Z0-9]+\s", aqua)); //Opening tag 2
            Highlights.Add(new SyntaxHighlights(@"<+[/]+[a-zA-Z0-9]+>", aqua)); //Closing tag
            Highlights.Add(new SyntaxHighlights(@"\s+class=", pink)); //class tag

            //Strings
            Highlights.Add(new SyntaxHighlights(@"""[^\n]*?""", orange));
            Highlights.Add(new SyntaxHighlights(@"'[^\n]*?'", orange));
            Highlights.Add(new SyntaxHighlights(@"(?s)(\""\""\"")(.*?)(\""\""\"")", orange));
        }
    }
}
