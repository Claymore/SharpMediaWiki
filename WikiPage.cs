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
            bool found = false;
            Tokens token = Tokens.WikiText;
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                for (int i = 0; i < line.Length - 1; ++i)
                {
                    char ch = line[i];
                    char nextCh = line[i + 1];
                    switch (token)
                    {
                        case Tokens.WikiText:
                            if (ch == '<' && nextCh == '!')
                            {
                                token = Tokens.HtmlCommentStart;
                                ++i;
                            }
                            else if (ch == '<' && nextCh == 'n')
                            {
                                token = Tokens.NoWikiStartPart1;
                                ++i;
                            }
                            break;
                        case Tokens.HtmlCommentStart:
                            if (ch == '-' && nextCh == '-')
                            {
                                token = Tokens.HtmlComment;
                                ++i;
                            }
                            else
                            {
                                token = Tokens.WikiText;
                            }
                            break;
                        case Tokens.HtmlComment:
                            if (ch == '-' && nextCh == '-')
                            {
                                token = Tokens.HtmlCommentEnd;
                            }
                            break;
                        case Tokens.HtmlCommentEnd:
                            if (ch == '-' && nextCh == '>')
                            {
                                token = Tokens.WikiText;
                            }
                            else
                            {
                                token = Tokens.HtmlComment;
                            }
                            break;
                        case Tokens.NoWikiStartPart1:
                            if (ch == 'o' && nextCh == 'w')
                            {
                                token = Tokens.NoWikiStartPart2;
                                ++i;
                            }
                            else
                            {
                                token = Tokens.WikiText;
                            }
                            break;
                        case Tokens.NoWikiStartPart2:
                            if (ch == 'i' && nextCh == 'k')
                            {
                                token = Tokens.NoWikiStartPart3;
                                ++i;
                            }
                            else
                            {
                                token = Tokens.WikiText;
                            }
                            break;
                        case Tokens.NoWikiStartPart3:
                            if (ch == 'i' && nextCh == '>')
                            {
                                token = Tokens.NoWiki;
                                ++i;
                            }
                            else
                            {
                                token = Tokens.WikiText;
                            }
                            break;
                        case Tokens.NoWiki:
                            if (ch == '<' && nextCh == '/')
                            {
                                token = Tokens.NoWikiEndPart1;
                                ++i;
                            }
                            break;
                        case Tokens.NoWikiEndPart1:
                            if (ch == 'n' && nextCh == 'o')
                            {
                                token = Tokens.NoWikiEndPart2;
                                ++i;
                            }
                            else
                            {
                                token = Tokens.NoWiki;
                            }
                            break;
                        case Tokens.NoWikiEndPart2:
                            if (ch == 'w' && nextCh == 'i')
                            {
                                token = Tokens.NoWikiEndPart3;
                                ++i;
                            }
                            else
                            {
                                token = Tokens.NoWiki;
                            }
                            break;
                        case Tokens.NoWikiEndPart3:
                            if (ch == 'k' && nextCh == 'i')
                            {
                                token = Tokens.NoWikiEndPart4;
                            }
                            else
                            {
                                token = Tokens.NoWiki;
                            }
                            break;
                        case Tokens.NoWikiEndPart4:
                            if (ch == 'i' && nextCh == '>')
                            {
                                token = Tokens.WikiText;
                            }
                            else
                            {
                                token = Tokens.NoWiki;
                            }
                            break;
                        default:
                            break;
                    }
                }
                if (token == Tokens.HtmlCommentStart)
                {
                    token = Tokens.WikiText;
                }
                else if (token == Tokens.HtmlCommentEnd)
                {
                    token = Tokens.HtmlComment;
                }
                else if (token == Tokens.NoWikiStartPart1 ||
                         token == Tokens.NoWikiStartPart2 ||
                         token == Tokens.NoWikiStartPart3)
                {
                    token = Tokens.WikiText;
                }
                else if (token == Tokens.NoWikiEndPart1 ||
                         token == Tokens.NoWikiEndPart2 ||
                         token == Tokens.NoWikiEndPart3 ||
                         token == Tokens.NoWikiEndPart4)
                {
                    token = Tokens.NoWiki;
                }

                Match m = _sectionRE.Match(line);

                if (token == Tokens.WikiText && m.Success)
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

        private enum Tokens
        {
            WikiText,
            HtmlCommentStart, // <!
            HtmlComment,
            NoWikiStartPart1, // <n
            NoWikiStartPart2, // ow
            NoWikiStartPart3, // ik
            NoWiki,
            HtmlCommentEnd,   // --
            NoWikiEndPart1,   // </
            NoWikiEndPart2,   // no
            NoWikiEndPart3,   // wi
            NoWikiEndPart4    // ki
        }
    }
}
