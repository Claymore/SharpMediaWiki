using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

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
            if (string.IsNullOrEmpty(title))
            {
                throw new ArgumentException("Title shouldn't be empty.", "title");
            }
            _title = title;
            _sections = new List<WikiPageSection>();
        }

        public WikiPage(string title, string text)
            : this(title)
        {
            Text = text;
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
            set
            {
                Parse(value);
            }
        }

        public string Title
        {
            get { return _title; }
        }

        public void Load(Wiki wiki)
        {
            Parse(wiki.LoadText(Title));
        }

        public void LoadEx(Wiki wiki)
        {
            ParameterCollection parameters = new ParameterCollection
            {
                { "prop", "info|revisions" },
                { "intoken", "edit" },
                { "rvprop", "timestamp|content|ids" },
            };
            XmlDocument xml = wiki.Query(QueryBy.Titles, parameters, Title);
            XmlNode node = xml.SelectSingleNode("//rev");
            string baseTimeStamp = "";
            string text = "";
            string revid = "";
            if (node != null && node.Attributes["timestamp"] != null)
            {
                baseTimeStamp = node.Attributes["timestamp"].Value;
            }
            if (node != null && node.Attributes["content"] != null)
            {
                text = node.Attributes["content"].Value;
            }
            if (node != null && node.Attributes["id"] != null)
            {
                revid = node.Attributes["id"].Value;
            }
            node = xml.SelectSingleNode("//page");
            string editToken = node.Attributes["edittoken"].Value;

            BaseTimestamp = baseTimeStamp;
            Token = editToken;
            LastRevisionId = revid;
            Parse(text);
        }

        public string Create(Wiki wiki, string summary)
        {
            return wiki.Save(Title,
                 "",
                 Text,
                 summary,
                 MinorFlags.None,
                 CreateFlags.CreateOnly,
                 WatchFlags.None,
                 SaveFlags.Replace,
                 true,
                 BaseTimestamp,
                 "",
                 wiki.Token);
        }

        public string Save(Wiki wiki, string summary)
        {
            return wiki.Save(Title,
                 "",
                 Text,
                 summary,
                 MinorFlags.None,
                 CreateFlags.None,
                 WatchFlags.None,
                 SaveFlags.Replace,
                 true,
                 BaseTimestamp,
                 "",
                 wiki.Token);
        }

        public string Append(Wiki wiki, string text, string summary)
        {
            return wiki.Save(Title,
                 "",
                 text,
                 summary,
                 MinorFlags.None,
                 CreateFlags.NoCreate,
                 WatchFlags.None,
                 SaveFlags.Append,
                 true,
                 BaseTimestamp,
                 "",
                 wiki.Token);
        }

        public string Prepend(Wiki wiki, string text, string summary)
        {
            return wiki.Save(Title,
                 "",
                 text,
                 summary,
                 MinorFlags.None,
                 CreateFlags.NoCreate,
                 WatchFlags.None,
                 SaveFlags.Prepend,
                 true,
                 BaseTimestamp,
                 "",
                 wiki.Token);
        }

        public void Move(Wiki wiki, string newTitle, string reason)
        {
            wiki.Move(Title, newTitle, reason, wiki.Token, true, false);
        }

        public void Move(Wiki wiki,
                         string newTitle,
                         string reason,
                         bool moveTalk,
                         bool noRedirect)
        {
            wiki.Move(Title, newTitle, reason, wiki.Token, moveTalk, noRedirect);
        }

        public void Delete(Wiki wiki, string reason)
        {
            wiki.Delete(Title, reason, wiki.Token);
        }

        public void Protect(Wiki wiki,
                            List<Wiki.Protection> protections,
                            string reason)
        {
            wiki.Protect(Title, protections, reason, wiki.Token, false);
        }

        public void UpProtect(Wiki wiki, string reason)
        {
            wiki.UnProtect(Title, reason, wiki.Token);
        }

        public void Stabilize(Wiki wiki, string reason)
        {
            wiki.Stabilize(Title, reason, wiki.Token);
        }

        public void Parse(string text)
        {
            if (_sections.Count != 0)
            {
                _sections.Clear();
            }
            if (!string.IsNullOrEmpty(_text))
            {
                _text = "";
            }
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

                        int index = _sections.Count - 1;
                        if (index >= 0 &&
                            index < _sections.Count &&
                            _sections[index].Level < level)
                        {
                            _sections[index].AddSubsection(section);
                        }
                        else
                        {
                            _sections.Add(section);
                        }
                        sectionText = new StringBuilder();
                        found = false;
                    }
                    else
                    {
                        _text = sectionText.ToString();
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

                int index = _sections.Count - 1;
                if (index >= 0 &&
                    index < _sections.Count &&
                    _sections[index].Level < level)
                {
                    _sections[index].AddSubsection(section);
                }
                else
                {
                    _sections.Add(section);
                }
            }
            else
            {
                _text = sectionText.ToString();
            }
        }

        public static WikiPage Parse(string title, string text)
        {
            WikiPage page = new WikiPage(title);
            page.Parse(text);
            return page;
        }
    }
}
