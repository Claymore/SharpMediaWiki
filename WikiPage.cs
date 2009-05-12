using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Claymore.SharpMediaWiki
{
    public class WikiPage
    {
        private string _title;
        private List<WikiPageSection> _sections;
        private string _text;

        public WikiPage(string title)
        {
            _title = title;
            _sections = new List<WikiPageSection>();
        }

        public IList<WikiPageSection> Sections
        {
            get { return _sections; }
        }

        public string Text
        {
            get
            {
                return _text + string.Join("", _sections.ConvertAll(s => s.Text).ToArray());
            }
        }

        public static WikiPage Parse(string title, string text)
        {
            WikiPage page = new WikiPage(title);

            string[] lines = text.Split(new char[] { '\n' });
            StringBuilder sectionText = new StringBuilder();
            Regex sectionRE = new Regex(@"^(={2,6})([^=].*?)(={2,6})\s*$");
            int level = 0;
            string sectionTitle = "";
            bool found = false;
            foreach (string line in lines)
            {
                Match m = sectionRE.Match(line);
                if (m.Success)
                {
                    if (found)
                    {
                        int index = page._sections.Count - 1;
                        if (index >= 0 &&
                            index < page._sections.Count &&
                            page._sections[index].Level < level)
                        {
                            page._sections[index].AddSubsection(new WikiPageSection(sectionTitle, level, sectionText.ToString()));
                        }
                        else
                        {
                            page._sections.Add(new WikiPageSection(sectionTitle, level, sectionText.ToString()));
                        }
                        sectionText = new StringBuilder();
                        found = false;
                    }
                    else
                    {
                        page._text = sectionText.ToString();
                        sectionText = new StringBuilder();
                    }
                    found = true;
                    level = Math.Min(m.Groups[1].Length, m.Groups[3].Length);
                    sectionTitle = m.Groups[2].Value;
                }
                else
                {
                    sectionText.Append(line + "\n");
                }
            }
            if (found)
            {
                int index = page._sections.Count - 1;
                if (index >= 0 &&
                    index < page._sections.Count &&
                    page._sections[index].Level < level)
                {
                    page._sections[index].AddSubsection(new WikiPageSection(sectionTitle, level, sectionText.ToString()));
                }
                else
                {
                    page._sections.Add(new WikiPageSection(sectionTitle, level, sectionText.ToString()));
                }
            }
            else
            {
                page._text = sectionText.ToString();
            }
            return page;
        }
    }
}
