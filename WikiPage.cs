using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Net;

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

        public void LoadPage(Wiki wiki)
        {
            UriBuilder ub = new UriBuilder(wiki.Uri);
            ub.Path = "/w/index.php";
            ub.Query = string.Format("title={0}&redirect=no&action=raw&ctype=text/plain&dontcountme=1",
                    Uri.EscapeDataString(Title));
            try
            {
                wiki.MakeRequest(ub.Uri, "GET");
            }
            catch (WebException e)
            {
                if (e.Status == WebExceptionStatus.ProtocolError &&
                    e.Message.Contains("(404)"))
                {
                    throw new WikiPageNotFound(Title + " not found", e);
                }
                else
                {
                    throw new WikiException("Failed to load page " + Title, e);
                }
            }
        }

        public void LoadEx(Wiki wiki)
        {
            ParameterCollection parameters = new ParameterCollection();
            parameters.Add("prop", "info|revisions");
            parameters.Add("intoken", "edit");
            parameters.Add("rvprop", "timestamp|content|ids");
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
        }

        public void Create(Wiki wiki, string summary)
        {
            Save(wiki,
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

        public void Save(Wiki wiki, string summary)
        {
            Save(wiki,
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

        public void Append(Wiki wiki, string text, string summary)
        {
            Save(wiki,
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

        public void Prepend(Wiki wiki, string text, string summary)
        {
            Save(wiki,
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

        public void Save(Wiki wiki,
                         string section,
                         string text,
                         string summary,
                         MinorFlags minor,
                         CreateFlags create,
                         WatchFlags watch,
                         SaveFlags mode,
                         bool bot,
                         string basetimestamp,
                         string starttimestamp,
                         string token)
        {
            ParameterCollection parameters = new ParameterCollection();
            parameters.Add("title", Title);
            if (mode == SaveFlags.Replace && !string.IsNullOrEmpty(section))
            {
                parameters.Add("section", section);
            }
            parameters.Add("token", token);
            if (minor != MinorFlags.None)
            {
                parameters.Add(minor.ToString().ToLower());
            }
            if (create != CreateFlags.None)
            {
                parameters.Add(create.ToString().ToLower());
            }
            if (watch != WatchFlags.None)
            {
                parameters.Add(watch.ToString().ToLower());
            }
            if (mode == SaveFlags.Append)
            {
                parameters.Add("appendtext", text);
            }
            else if (mode == SaveFlags.Prepend)
            {
                parameters.Add("prependtext", text);
            }
            else
            {
                parameters.Add("text", text);
            }
            if (bot)
            {
                parameters.Add("bot");
            }
            if (!string.IsNullOrEmpty(basetimestamp))
            {
                parameters.Add("basetimestamp", basetimestamp);
            }
            if (!string.IsNullOrEmpty(starttimestamp))
            {
                parameters.Add("starttimestamp", starttimestamp);
            }
            parameters.Add("summary", summary);

            XmlDocument xml = wiki.MakeRequest(Action.Edit, parameters);
            XmlNode result = xml.SelectSingleNode("//edit[@newrevid]");
            if (result != null)
            {
                LastRevisionId = result.Attributes["newrevid"].Value;
            }
        }

        public void Move(Wiki wiki,
                         string newTitle,
                         string reason,
                         bool moveTalk,
                         bool noRedirect)
        {
            if (string.IsNullOrEmpty(Token))
            {
                Token = wiki.Token;
            }
            Move(wiki, newTitle, reason, Token, moveTalk, noRedirect);
        }

        public void Move(Wiki wiki,
                         string newTitle,
                         string reason,
                         string token,
                         bool moveTalk,
                         bool noRedirect)
        {
            ParameterCollection parameters = new ParameterCollection();
            parameters.Add("from", Title);
            parameters.Add("to", newTitle);
            parameters.Add("token", token);
            parameters.Add("reason", reason);
            if (moveTalk)
            {
                parameters.Add("movetalk");
            }
            if (noRedirect)
            {
                parameters.Add("noredirect");
            }

            wiki.MakeRequest(Action.Move, parameters);
        }

        public void Delete(Wiki wiki, string reason)
        {
            if (string.IsNullOrEmpty(Token))
            {
                Token = wiki.Token;
            }

            Delete(wiki, reason, Token);
        }

        public void Delete(Wiki wiki, string reason, string token)
        {
            ParameterCollection parameters = new ParameterCollection();
            parameters.Add("title", Title);
            parameters.Add("token", token);
            parameters.Add("reason", reason);
            wiki.MakeRequest(Action.Delete, parameters);
        }

        public void Protect(Wiki wiki, string protection, string expiry, string reason)
        {
            if (string.IsNullOrEmpty(Token))
            {
                Token = wiki.Token;
            }
            Protect(wiki, protection, expiry, reason, Token);
        }

        public void Protect(Wiki wiki, string protection, string expiry, string reason, string token)
        {
            ParameterCollection parameters = new ParameterCollection();
            parameters.Add("title", Title);
            parameters.Add("token", token);
            parameters.Add("protections", protection);
            parameters.Add("expiry", expiry);
            parameters.Add("reason", reason);
            wiki.MakeRequest(Action.Protect, parameters);
        }

        public static void Review(Wiki wiki,
                                  string revisionId,
                                  string accuracy,
                                  string comment,
                                  string editToken)
        {
            ParameterCollection parameters = new ParameterCollection();
            parameters.Add("revid", revisionId);
            parameters.Add("token", editToken);
            parameters.Add("flag_accuracy", accuracy);
            if (!string.IsNullOrEmpty(comment))
            {
                parameters.Add("comment", comment);
            }

            wiki.MakeRequest(Action.Review, parameters);
        }

        public void Stabilize(Wiki wiki, string reason)
        {
            Stabilize(wiki, reason, wiki.Token);
        }

        public void Stabilize(Wiki wiki, string reason, string editToken)
        {
            UriBuilder ub = new UriBuilder(wiki.Uri);
            ub.Path = "/wiki/Special:Stabilization";

            ParameterCollection parameters = new ParameterCollection();
            parameters.Add("wpEditToken", editToken);
            parameters.Add("page", Title);
            parameters.Add("title", "Special:Stabilization");
            parameters.Add("wpWatchthis", "0");
            parameters.Add("wpReviewthis", "0");
            parameters.Add("wpReason", reason);
            parameters.Add("wpReasonSelection", "other");
            parameters.Add("mwStabilize-expiry", "infinite");
            parameters.Add("wpExpirySelection", "infinite");
            parameters.Add("wpStableconfig-select", "1");
            parameters.Add("wpStableconfig-override", "1");

            StringBuilder attributes = new StringBuilder();
            foreach (KeyValuePair<string, string> pair in parameters)
            {
                if (pair.Key == "format")
                {
                    continue;
                }
                attributes.Append(string.Format("&{0}={1}",
                    pair.Key,
                    Uri.EscapeDataString(pair.Value)));
            }
            ub.Query = attributes.ToString().Substring(1);
            string result = wiki.MakeRequest(ub.Uri, "POST");
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
