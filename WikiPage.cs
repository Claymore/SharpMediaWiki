using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Claymore.SharpMediaWiki
{
    public class WikiPage
    {
        private string _title;
        private readonly List<WikiPageSection> _sections;
        private string _text;
        private static Regex _sectionRE;

        public string BaseTimestamp { get; set; }
        public string Token { get; set; }
        public string LastRevisionId { get; set; }

        static WikiPage()
        {
            _sectionRE = new Regex(@"^(={2,6})([^=].*?)(={2,6})\s*$");
        }

        public WikiPage(string title)
        {
            _title = title;
            _sections = new List<WikiPageSection>();
        }

        public List<WikiPageSection> Sections
        {
            get { return _sections; }
        }

        public string Text
        {
            get
            {
                return _text + string.Concat(_sections.ConvertAll(s => s.Text).ToArray());
            }
        }

        public string Title
        {
            get { return _title; }
        }

        public static WikiPage Parse(string title, string text)
        {
            WikiPage page = new WikiPage(title);

            StringReader reader = new StringReader(text);
            StringBuilder sectionText = new StringBuilder();
            int level = 0;
            string sectionTitle = "";
            string rawSectionTitle = "";
            bool comment = false;
            bool found = false;
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                Match m = _sectionRE.Match(line);
                if (line.Contains("<!--"))
                {
                    comment = true;
                }
                if (comment && line.Contains("-->"))
                {
                    comment = false;
                }
                if (!comment && m.Success)
                {
                    if (found)
                    {
                        var section = new WikiPageSection(sectionTitle,
                            rawSectionTitle,
                            level,
                            sectionText.ToString());

                        int index = page._sections.Count - 1;
                        if (index >= 0 &&
                            index < page._sections.Count &&
                            page._sections[index].Level < level)
                        {
                            page._sections[index].AddSubsection(section);
                        }
                        else
                        {
                            page._sections.Add(section);
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
                    rawSectionTitle = line;
                }
                else
                {
                    sectionText.Append(line + "\n");
                }
            }
            
            if (found)
            {
                var section = new WikiPageSection(sectionTitle,
                            rawSectionTitle,
                            level,
                            sectionText.ToString());

                int index = page._sections.Count - 1;
                if (index >= 0 &&
                    index < page._sections.Count &&
                    page._sections[index].Level < level)
                {
                    page._sections[index].AddSubsection(section);
                }
                else
                {
                    page._sections.Add(section);
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
