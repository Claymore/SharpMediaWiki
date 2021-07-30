using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Xml;

namespace Claymore.SharpMediaWiki
{
    public class Wiki
    {
        private CookieContainer _cookies;
        private readonly Uri _uri;
        private string _userAgent;
        private string _token;
        private DateTime _lastQueryTime;
        private bool _highLimits;
        private string _username;
        private bool _isBot;
        private int _sleepBetweenEdits;
        private int _sleepBetweenQueries;
        private Dictionary<string, int> _namespaces;
        private static readonly bool _isRunningOnMono = (Type.GetType("Mono.Runtime") != null);

        /// <summary>
        /// Initializes a new instance of the Wiki class with the specified URI.
        /// </summary>
        /// <param name="url">A URI string.</param>
        /// <example>Wiki wiki = new Wiki("http://en.wikipedia.org/w/");</example>
        /// <exception cref="System.ArgumentNullException" />
        /// <exception cref="System.UriFormatException" />
        public Wiki(string uri)
        {
            UriBuilder ub = new UriBuilder(uri);
            _uri = ub.Uri;
            _highLimits = false;
            _isBot = false;
            SleepBetweenEdits = 10;
            SleepBetweenQueries = 10;
            Version version = Assembly.GetExecutingAssembly().GetName().Version;
            _userAgent = string.Format("SharpMediaWiki/{0}.{1}",
                version.Major, version.Minor);
            _namespaces = new Dictionary<string, int>();
        }

        public int SleepBetweenEdits
        {
            set { _sleepBetweenEdits = Math.Max(2, value) * 1000; }
        }

        public int SleepBetweenQueries
        {
            set { _sleepBetweenQueries = Math.Max(2, value) * 1000; }
        }

        public Uri Uri
        {
            get { return _uri; }
        }

        public bool HighLimits
        {
            get { return _highLimits; }
        }

        public bool IsBot
        {
            get { return _isBot; }
        }

        public string Token
        {
            get { return _token; }
        }

        public string User
        {
            get { return _username; }
        }

        /// <summary>
        /// Logs into the MediaWiki as 'username' using 'password'.
        /// </summary>
        /// <param name="username">A username.</param>
        /// <param name="password">A password.</param>
        /// <exception cref="Claymore.SharpMediaWiki.WikiException">Thrown when an error occurs.</exception>
        /// <remarks><see cref="http://www.mediawiki.org/wiki/API:Login"/></remarks>
        public void Login(string username, string password)
        {
            if (string.IsNullOrEmpty(username))
            {
                throw new ArgumentException("Username shoudln't be empty.", "username");
            }
            if (string.IsNullOrEmpty(password))
            {
                throw new ArgumentException("Password shoudln't be empty.", "password");
            }

            ParameterCollection parameters = new ParameterCollection
            {
                { "lgname", username },
                { "lgpassword", password }
            };

            try
            {
                XmlDocument xml = MakeRequest(Action.Login, parameters);
                string result = xml.SelectSingleNode("//login").Attributes["result"].Value;
                if (result == "NeedToken")
                {
                    string loginToken = xml.SelectSingleNode("//login").Attributes["token"].Value;

                    parameters = new ParameterCollection
                    {
                        { "lgname", username },
                        { "lgpassword", password },
                        { "lgtoken", loginToken }
                    };
                    xml = MakeRequest(Action.Login, parameters);
                    result = xml.SelectSingleNode("//login").Attributes["result"].Value;
                }
                if (result != "Success")
                {
                    throw new WikiException("Login failed, server returned '" + result + "'.");
                }
                _username = username;

                parameters = new ParameterCollection
                {
                    { "meta", "userinfo|tokens" },
                    { "uiprop", "rights" },
                    { "type", "csrf" }
                };

                XmlDocument doc = Query(QueryBy.Titles, parameters, "Main Page");
                _highLimits = doc.SelectSingleNode("//rights[r=\"apihighlimits\"]/r") != null;
                _isBot = doc.SelectSingleNode("//rights[r=\"bot\"]/r") != null;
                XmlNode tokenNode = doc.SelectSingleNode("//tokens");
                _token = tokenNode != null ? pageNode.Attributes["csrftoken"].Value : "";
            }
            catch (WebException e)
            {
                throw new WikiException("Login failed", e);
            }
        }

        public void Login()
        {
            try
            {
                ParameterCollection parameters = new ParameterCollection
                {
                    { "prop", "info" },
                    { "meta", "userinfo|tokens" },
                    { "uiprop", "rights" },
                    { "type", "csrf" }
                };

                XmlDocument doc = Query(QueryBy.Titles, parameters, "Main Page");
                _highLimits = doc.SelectSingleNode("//rights[r=\"apihighlimits\"]/r") != null;
                _isBot = doc.SelectSingleNode("//rights[r=\"bot\"]/r") != null;
                XmlNode tokenNode = doc.SelectSingleNode("//tokens");
                XmlNode userNode = doc.SelectSingleNode("//userinfo");
                _username = userNode != null ? userNode.Attributes["name"].Value : "";
                _token = tokenNode != null ? pageNode.Attributes["csrftoken"].Value : "";
            }
            catch (WebException e)
            {
                throw new WikiException("Login failed", e);
            }
        }

        /// <summary>
        /// Logs out from the Wiki.
        /// </summary>
        /// <exception cref="System.Net.ProtocolViolationException" />
        /// <exception cref="System.Net.WebException" />
        /// <remarks><see cref="http://www.mediawiki.org/wiki/API:Logout"/></remarks>
        public void Logout()
        {
            try
            {
                HttpWebRequest request = PrepareRequest();
                using (StreamWriter sw = new StreamWriter(request.GetRequestStream()))
                {
                    sw.Write("action=logout");
                }
                WebResponse response = request.GetResponse();
                response.Close();
                _username = "";
                _cookies = null;
                _token = "";
            }
            catch (WebException e)
            {
                throw new WikiException("Logout failed", e);
            }
        }

        public XmlDocument Enumerate(ParameterCollection parameters, bool getAll)
        {
            XmlDocument result = new XmlDocument();
            string query = PrepareQuery(Action.Query, parameters);
            Parameter parameter = null;

            while (true)
            {
                try
                {
                    parameter = FillDocumentWithQueryResults(query, result);
                }
                catch (WebException e)
                {
                    throw new WikiException("Enumerating failed", e);
                }
                if (parameter == null || !getAll)
                {
                    break;
                }
                string limitName = parameter.Name.Substring(0, 2) + "limit";
                ParameterCollection localParameters = new ParameterCollection(parameters);
                localParameters.Set(parameter.Name, parameter.Value);
                localParameters.Set(limitName, "max");
                query = PrepareQuery(Action.Query, localParameters);
            }
            return result;
        }

        private void Enumerate(ParameterCollection parameters, XmlDocument result, bool all)
        {
            string query = PrepareQuery(Action.Query, parameters);
            Parameter parameter = null;

            while (true)
            {
                try
                {
                    parameter = FillDocumentWithQueryResults(query, result);
                }
                catch (WebException e)
                {
                    throw new WikiException("Query failed.", e);
                }

                if (parameter == null || !all)
                {
                    break;
                }
                string limitName = parameter.Name.Substring(0, 2) + "limit";
                ParameterCollection localParameters = new ParameterCollection(parameters);
                localParameters.Set(parameter.Name, parameter.Value);
                localParameters.Set(limitName, "max");
                query = PrepareQuery(Action.Query, localParameters);
            }
        }

        public XmlDocument Query(QueryBy queryBy,
            ParameterCollection parameters,
            string id)
        {
            return Query(queryBy, parameters, new string[] { id }, _highLimits ? 500 : 50, true);
        }

        public XmlDocument Query(QueryBy queryBy,
            ParameterCollection parameters,
            IEnumerable<string> ids)
        {
            return Query(queryBy, parameters, ids, _highLimits ? 500 : 50, true);
        }

        public XmlDocument Query(QueryBy queryBy,
            ParameterCollection parameters,
            IEnumerable<string> ids,
            int limit,
            bool all)
        {
            string keyword = "";
            switch (queryBy)
            {
                case QueryBy.IDs:
                    keyword = "pageids";
                    break;
                case QueryBy.Revisions:
                    keyword = "revids";
                    break;
                case QueryBy.Titles:
                    keyword = "titles";
                    break;
            }
            XmlDocument document = new XmlDocument();
            StringBuilder idsString = new StringBuilder();
            int index = 0;
            foreach (string id in ids)
            {
                if (index < limit)
                {
                    idsString.Append("|" + id);
                    ++index;
                }
                else
                {
                    idsString.Remove(0, 1);
                    ParameterCollection localParameters = new ParameterCollection(parameters);
                    localParameters.Add(keyword, idsString.ToString());
                    string query = PrepareQuery(Action.Query, localParameters);
                    Enumerate(localParameters, document, all);

                    index = 1;
                    idsString = new StringBuilder("|" + id);
                }
            }
            if (index > 0)
            {
                idsString.Remove(0, 1);
                ParameterCollection localParameters = new ParameterCollection(parameters);
                localParameters.Add(keyword, idsString.ToString());
                string query = PrepareQuery(Action.Query, localParameters);
                Enumerate(localParameters, document, all);
            }
            return document;
        }

        public string LoadText(string title)
        {
            if (string.IsNullOrEmpty(title))
            {
                throw new ArgumentException("Title shouldn't be empty.", "title");
            }
            UriBuilder ub = new UriBuilder(Uri);
            ub.Path = "/w/index.php";
            ub.Query = string.Format("title={0}&redirect=no&action=raw&ctype=text/plain&dontcountme=1",
                    Uri.EscapeDataString(title));
            try
            {
                return MakeRequest(ub.Uri, RequestMethod.Get);
            }
            catch (WebException e)
            {
                if (e.Status == WebExceptionStatus.ProtocolError &&
                    e.Message.Contains("(404)"))
                {
                    throw new WikiPageNotFound(title + " not found", e);
                }
                else
                {
                    throw new WikiException("Failed to load page " + title, e);
                }
            }
        }

        public string Append(string title, string text, string summary)
        {
            return Save(title,
                        "",
                        text,
                        summary,
                        MinorFlags.None,
                        CreateFlags.NoCreate,
                        WatchFlags.None,
                        SaveFlags.Append,
                        true);
        }

        public string Append(string title, string text, string summary, MinorFlags minor, bool botEdit)
        {
            return Save(title,
                        "",
                        text,
                        summary,
                        minor,
                        CreateFlags.None,
                        WatchFlags.None,
                        SaveFlags.Append,
                        botEdit);
        }

        public string Prepend(string title, string text, string summary)
        {
            return Save(title,
                        "",
                        text,
                        summary,
                        MinorFlags.None,
                        CreateFlags.NoCreate,
                        WatchFlags.None,
                        SaveFlags.Prepend,
                        true);
        }

        public string Create(string title, string text, string summary)
        {
            return Save(title,
                        "",
                        text,
                        summary,
                        MinorFlags.None,
                        CreateFlags.CreateOnly,
                        WatchFlags.None,
                        SaveFlags.Replace,
                        true);
        }

        public string SaveSection(string title, string section, string text, string summary)
        {
            if (string.IsNullOrEmpty(section))
            {
                throw new ArgumentException("Section shouldn't be empty.", "section");
            }
            return Save(title,
                        section,
                        text,
                        summary,
                        MinorFlags.None,
                        CreateFlags.None,
                        WatchFlags.None,
                        SaveFlags.Replace,
                        true);
        }

        public string Save(string title, string text, string summary, MinorFlags minor, bool botEdit)
        {
            return Save(title,
                        "",
                        text,
                        summary,
                        minor,
                        CreateFlags.None,
                        WatchFlags.None,
                        SaveFlags.Replace,
                        botEdit);
        }

        public string Save(string title, string text, string summary)
        {
            return Save(title,
                        "",
                        text,
                        summary,
                        MinorFlags.None,
                        CreateFlags.None,
                        WatchFlags.None,
                        SaveFlags.Replace,
                        true);
        }

        public string Save(string title,
                           string section,
                           string text,
                           string summary,
                           MinorFlags minor,
                           CreateFlags create,
                           WatchFlags watch,
                           SaveFlags mode,
                           bool bot)
        {
            ParameterCollection parameters = new ParameterCollection
            {
                { "meta", "tokens" },
                { "type", "csrf" }
            };
            XmlDocument doc = Query(QueryBy.Titles, parameters, title);
            XmlNode tokenNode = doc.SelectSingleNode("//tokens");
            string token = tokenNode != null ? pageNode.Attributes["csrftoken"].Value : "";
            return Save(title,
                        section,
                        text,
                        summary,
                        minor,
                        create,
                        watch,
                        mode,
                        bot,
                        "",
                        "",
                        token);
        }

        public string Save(string title,
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
            if (string.IsNullOrEmpty(title))
            {
                throw new ArgumentException("Title shouldn't be empty.", "title");
            }
            if (string.IsNullOrEmpty(token))
            {
                throw new ArgumentException("Token shouldn't be empty.", "token");
            }

            ParameterCollection parameters = new ParameterCollection
            {
                { "title", title },
                { "token", token }
            };
            if (mode == SaveFlags.Replace && !string.IsNullOrEmpty(section))
            {
                parameters.Add("section", section);
            }
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
                parameters.Add("watchlist", watch.ToString().ToLower());
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
            if (!string.IsNullOrEmpty(summary))
            {
                parameters.Add("summary", summary);
            }

            try
            {
                XmlDocument xml = MakeRequest(Action.Edit, parameters);
                XmlNode result = xml.SelectSingleNode("//edit[@newrevid]");
                if (result != null)
                {
                    return result.Attributes["newrevid"].Value;
                }
            }
            catch (WebException e)
            {
                throw new WikiException("Saving failed", e);
            }
            return null;
        }

        public void Review(string revisionId,
                           string accuracy,
                           string comment,
                           string token)
        {
            if (string.IsNullOrEmpty(revisionId))
            {
                throw new ArgumentException("Revision ID shouldn't be empty.", "revisionId");
            }
            if (string.IsNullOrEmpty(token))
            {
                throw new ArgumentException("Token shouldn't be empty.", "token");
            }
            ParameterCollection parameters = new ParameterCollection
            {
                { "revid", revisionId} ,
                { "token", token },
                { "flag_accuracy", accuracy },
                //{ "unapprove", "1" }
            };
            if (!string.IsNullOrEmpty(comment))
            {
                parameters.Add("comment", comment);
            }
            try
            {
                MakeRequest(Action.Review, parameters);
            }
            catch (WebException e)
            {
                throw new WikiException("Review failed", e);
            }
        }

        public void Move(string fromTitle, string toTitle, string reason)
        {
            Move(fromTitle, toTitle, reason, Token, true, false);
        }

        public void Move(string fromTitle,
                         string toTitle,
                         string reason,
                         bool moveTalk,
                         bool noRedirect)
        {
            Move(fromTitle, toTitle, reason, Token, moveTalk, noRedirect);
        }

        public void Move(string fromTitle,
                         string toTitle,
                         string reason,
                         string token,
                         bool moveTalk,
                         bool noRedirect)
        {
            if (string.IsNullOrEmpty(fromTitle))
            {
                throw new ArgumentException("Title shouldn't be empty.", "fromTitle");
            }
            if (string.IsNullOrEmpty(toTitle))
            {
                throw new ArgumentException("Title shouldn't be empty.", "toTitle");
            }
            if (string.IsNullOrEmpty(token))
            {
                throw new ArgumentException("Token shouldn't be empty.", "token");
            }

            ParameterCollection parameters = new ParameterCollection
            {
                { "from", fromTitle },
                { "to", toTitle },
                { "token", token },
            };
            if (!string.IsNullOrEmpty(reason))
            {
                parameters.Add("reason", reason);
            }
            if (moveTalk)
            {
                parameters.Add("movetalk");
            }
            if (noRedirect)
            {
                parameters.Add("noredirect");
            }

            try
            {
                MakeRequest(Action.Move, parameters);
            }
            catch (WebException e)
            {
                throw new WikiException("Move failed", e);
            }
        }

        public void Delete(string title, string reason, string token)
        {
            if (string.IsNullOrEmpty(title))
            {
                throw new ArgumentException("Title shouldn't be empty.", "title");
            }
            if (string.IsNullOrEmpty(token))
            {
                throw new ArgumentException("Token shouldn't be empty.", "token");
            }

            ParameterCollection parameters = new ParameterCollection
            {
                { "title", title },
                { "token", token }
            };
            if (!string.IsNullOrEmpty(reason))
            {
                parameters.Add("reason", reason);
            }
            try
            {
                MakeRequest(Action.Delete, parameters);
            }
            catch (WebException e)
            {
                throw new WikiException("Delete failed", e);
            }
        }

        public void UnProtect(string title, string reason, string token)
        {
            Protect(title,
                new List<Protection>
                {
                    new Protection(Action.Edit, UserGroup.None),
                    new Protection(Action.Move, UserGroup.None),
                },
                reason,
                token,
                false);
        }

        public void Protect(string title,
                            List<Protection> protections,
                            string reason,
                            string token,
                            bool cascade)
        {
            if (string.IsNullOrEmpty(title))
            {
                throw new ArgumentException("Title shouldn't be empty.", "title");
            }
            if (string.IsNullOrEmpty(token))
            {
                throw new ArgumentException("Token shouldn't be empty.", "token");
            }

            ParameterCollection parameters = new ParameterCollection
            {
                { "title", title },
                { "token", token },
                { "protections", string.Join("|",
                    protections.ConvertAll(p => p.ToString()).ToArray()) },
                { "expiry", string.Join("|",
                    protections.ConvertAll(p => p.Expiry).ToArray()) },
            };
            if (!string.IsNullOrEmpty(reason))
            {
                parameters.Add("reason", reason);
            }
            if (cascade)
            {
                parameters.Add("cascade");
            }
            try
            {
                MakeRequest(Action.Protect, parameters);
            }
            catch (WebException e)
            {
                throw new WikiException("Protect failed", e);
            }
        }

        public void Upload(string filename, string comment, string text, string token, WatchFlags watch, string url)
        {
            if (string.IsNullOrEmpty(filename))
            {
                throw new ArgumentException("File name shouldn't be empty.", "filename");
            }
            if (string.IsNullOrEmpty(token))
            {
                throw new ArgumentException("Token shouldn't be empty.", "token");
            }
            if (string.IsNullOrEmpty(url))
            {
                throw new ArgumentException("URL shouldn't be empty.", "url");
            }
        }

        public void Upload(string filename, string comment, string text, WatchFlags watch, byte[] data, string contentType, bool ignoreWarnings)
        {
            ParameterCollection parameters = new ParameterCollection
            {
                { "prop", "info" },
                { "meta", "tokens" },
                { "type": "csrf" }
            };
            XmlDocument doc = Query(QueryBy.Titles, parameters, "Main Page");
            XmlNode tokenNode = doc.SelectSingleNode("//tokens");
            string token = tokenNode != null ? pageNode.Attributes["csrftoken"].Value : "";
            Upload(filename, comment, text, token, watch, data, contentType, ignoreWarnings);
        }

        public void Upload(string filename, string comment, string text, string token, WatchFlags watch, byte[] data, string contentType, bool ignoreWarnings)
        {
            if (string.IsNullOrEmpty(filename))
            {
                throw new ArgumentException("File name shouldn't be empty.", "filename");
            }
            if (string.IsNullOrEmpty(token))
            {
                throw new ArgumentException("Token shouldn't be empty.", "token");
            }
            if (data == null || data.Length == 0)
            {
                throw new ArgumentException("Data shouldn't be empty.", "data");
            }
            ParameterCollection parameters = new ParameterCollection
            {
                { "filename", filename },
                { "token", token },
            };
            if (!string.IsNullOrEmpty(text))
            {
                parameters.Add("text", text);
            }
            if (!string.IsNullOrEmpty(comment))
            {
                parameters.Add("comment", comment);
            }
            if (watch != WatchFlags.None)
            {
                parameters.Add("watch", watch.ToString().ToLower());
            }
            if (ignoreWarnings)
            {
                parameters.Add("ignorewarnings");
            }
            try
            {
                MakeMultipartFormRequest(parameters, filename, contentType, data);
            }
            catch (WebException e)
            {
                throw new WikiException("Upload failed", e);
            }
        }

        public void Stabilize(string title, string reason, string editToken)
        {
            UriBuilder ub = new UriBuilder(Uri);
            ub.Path = "/wiki/Special:Stabilization";

            ParameterCollection parameters = new ParameterCollection();
            parameters.Add("wpEditToken", editToken);
            parameters.Add("page", title);
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
            string result = MakeRequest(ub.Uri, RequestMethod.Post);
        }

        public int PageNamespace(string title)
        {
            foreach (var pair in _namespaces)
            {
                if (title.StartsWith(pair.Key + ":"))
                {
                    return pair.Value;
                }
            }
            return 0;
        }

        public void GetNamespaces()
        {
            _namespaces.Clear();
            ParameterCollection parameters = new ParameterCollection
            {
                { "meta", "siteinfo" },
                { "siprop", "namespaces" }
            };
            XmlDocument xml = Enumerate(parameters, true);
            XmlNodeList nodes = xml.SelectNodes("//ns[@id > 0]");
            foreach (XmlNode node in nodes)
            {
                _namespaces.Add(node.FirstChild.Value, int.Parse(node.Attributes["id"].Value));
            }
        }

        public void LoadNamespaces(IEnumerable<byte> data)
        {
            _namespaces.Clear();
            Deserializer deserializer = new Deserializer(data);
            int count = deserializer.GetInt();
            for (int i = 0; i < count; ++i)
            {
                string ns = deserializer.GetString();
                int number = deserializer.GetInt();
                _namespaces.Add(ns, number);
            }
        }

        public byte[] NamespacesToArray()
        {
            Serializer serializer = new Serializer();
            serializer.Put(_namespaces.Count);
            foreach (var pair in _namespaces)
            {
                serializer.Put(pair.Key);
                serializer.Put(pair.Value);
            }
            return serializer.ToArray();
        }

        public byte[] CookiesToArray()
        {
            Serializer serializer = new Serializer();
            serializer.Put(_cookies.Count);
            foreach (Cookie cookie in _cookies.GetCookies(_uri))
            {
                serializer.Put(cookie.Name);
                serializer.Put(cookie.Value);
                serializer.Put(cookie.Path);
                serializer.Put(cookie.Domain);
            }
            return serializer.ToArray();
        }

        public void LoadCookies(byte[] data)
        {
            _cookies = new CookieContainer();
            Deserializer deserializer = new Deserializer(data);
            int count = deserializer.GetInt();
            for (int i = 0; i < count; ++i)
            {
                string name = deserializer.GetString();
                string value = deserializer.GetString();
                string path = deserializer.GetString();
                string domain = deserializer.GetString();
                Cookie cookie = new Cookie(name, value, path, domain);
                _cookies.Add(cookie);
            }
        }

        public string GetNamespace(int number)
        {
            foreach (var item in _namespaces)
            {
                if (item.Value == number)
                {
                    return item.Key;
                }
            }
            return null;
        }

        public string MakeRequest(Uri uri, RequestMethod method)
        {
            TimeSpan diff = DateTime.Now - _lastQueryTime;
            if (diff.Milliseconds < _sleepBetweenQueries)
            {
                Thread.Sleep(_sleepBetweenQueries - diff.Milliseconds);
            }

            HttpWebRequest request = PrepareRequest(uri, method);
            WebResponse response = request.GetResponse();
            using (StreamReader sr =
                new StreamReader(GetResponseStream((HttpWebResponse)response)))
            {
                _lastQueryTime = DateTime.Now;
                return sr.ReadToEnd();
            }
        }

        public XmlDocument MakeRequest(Action action, ParameterCollection parameters)
        {
            TimeSpan diff = DateTime.Now - _lastQueryTime;
            if (action == Action.Edit && diff.Milliseconds < _sleepBetweenEdits)
            {
                Thread.Sleep(_sleepBetweenEdits - diff.Milliseconds);
            }

            XmlDocument doc = new XmlDocument();
            HttpWebResponse response = null;
            string query = PrepareQuery(action, parameters);
            for (int tries = 0; tries < 3; ++tries)
            {
                HttpWebRequest request = PrepareRequest();
                using (StreamWriter sw =
                    new StreamWriter(request.GetRequestStream()))
                {
                    sw.Write(query);
                }
                response = (HttpWebResponse)request.GetResponse();
                string[] retryAfter = response.Headers.GetValues("Retry-After");
                if (retryAfter != null)
                {
                    int lagInSeconds = int.Parse(retryAfter[0]);
                    Thread.Sleep(lagInSeconds * 1000);
                }
                else
                {
                    using (StreamReader sr =
                        new StreamReader(GetResponseStream((HttpWebResponse)response)))
                    {
                        doc.LoadXml(sr.ReadToEnd());
                        _lastQueryTime = DateTime.Now;
                        XmlNode errorNode = doc.SelectSingleNode("//error");
                        if (errorNode != null &&
                            errorNode.Attributes["code"].Value == "maxlag")
                        {
                            Thread.Sleep(5 * 1000 * (tries + 1));
                            continue;
                        }
                    }
                    break;
                }
            }

            XmlNode node = doc.SelectSingleNode("//error");
            if (node != null)
            {
                string code = node.Attributes["code"].Value;
                throw MakeActionException(action, code);
            }
            node = doc.SelectSingleNode("//" + action.ToString().ToLower());
            string result = "";
            if (node != null && node.Attributes["result"] != null)
            {
                result = node.Attributes["result"].Value;
                if (result != "Success" && result != "NeedToken")
                {
                    throw MakeActionException(action, result);
                }
            }

            if (action == Action.Login &&
                response.Cookies != null &&
                response.Cookies.Count > 0)
            {
                _cookies = new CookieContainer();
                _cookies.Add(response.Cookies);
            }
            return doc;
        }

        private XmlDocument MakeMultipartFormRequest(ParameterCollection parameters, string filename, string contentType, byte[] data)
        {
            Action action = Action.Upload;
            TimeSpan diff = DateTime.Now - _lastQueryTime;
            if (diff.TotalMilliseconds < _sleepBetweenEdits)
            {
                Thread.Sleep(_sleepBetweenEdits - diff.Milliseconds);
            }

            XmlDocument doc = new XmlDocument();
            HttpWebResponse response = null;
            byte[] query = PrepareMultipartFormQuery(parameters, filename, contentType, data);
            for (int tries = 0; tries < 3; ++tries)
            {
                string ct = "multipart/form-data; boundary=----------ThIs_Is_tHe_bouNdaRY_$";
                UriBuilder ub = new UriBuilder(_uri);
                ub.Path += "api.php";
                HttpWebRequest request = PrepareRequest(ub.Uri, RequestMethod.Post, ct);
                using (BinaryWriter sw =
                    new BinaryWriter(request.GetRequestStream()))
                {
                    sw.Write(query);
                }
                response = (HttpWebResponse)request.GetResponse();
                string[] retryAfter = response.Headers.GetValues("Retry-After");
                if (retryAfter != null)
                {
                    int lagInSeconds = int.Parse(retryAfter[0]);
                    Thread.Sleep(lagInSeconds * 1000);
                }
                else
                {
                    using (StreamReader sr =
                        new StreamReader(GetResponseStream((HttpWebResponse)response)))
                    {
                        doc.LoadXml(sr.ReadToEnd());
                        _lastQueryTime = DateTime.Now;
                        XmlNode errorNode = doc.SelectSingleNode("//error");
                        if (errorNode != null &&
                            errorNode.Attributes["code"].Value == "maxlag")
                        {
                            Thread.Sleep(5 * 1000 * (tries + 1));
                            continue;
                        }
                    }
                    break;
                }
            }

            XmlNode node = doc.SelectSingleNode("//error");
            if (node != null)
            {
                string code = node.Attributes["code"].Value;
                throw MakeActionException(action, code);
            }
            node = doc.SelectSingleNode("//" + action.ToString().ToLower());
            string result = "";
            if (node != null && node.Attributes["result"] != null)
            {
                result = node.Attributes["result"].Value;
                if (result != "Success" && result != "NeedToken")
                {
                    throw MakeActionException(action, result);
                }
            }
            return doc;
        }

        private Parameter FillDocumentWithQueryResults(string query, XmlDocument document)
        {
            TimeSpan diff = DateTime.Now - _lastQueryTime;
            if (diff.TotalMilliseconds < _sleepBetweenQueries)
            {
                Thread.Sleep(_sleepBetweenQueries - diff.Milliseconds);
            }

            string xml = "";
            for (int tries = 0; tries < 3; ++tries)
            {
                HttpWebRequest request = PrepareRequest();
                using (StreamWriter sw =
                    new StreamWriter(request.GetRequestStream()))
                {
                    sw.Write(query);
                }
                WebResponse response = (HttpWebResponse)request.GetResponse();
                string[] retryAfter = response.Headers.GetValues("Retry-After");
                if (retryAfter != null)
                {
                    int lagInSeconds = int.Parse(retryAfter[0]);
                    Thread.Sleep(lagInSeconds * 1000);
                }
                using (StreamReader sr =
                    new StreamReader(GetResponseStream((HttpWebResponse)response)))
                {
                    xml = sr.ReadToEnd();
                    _lastQueryTime = DateTime.Now;
                    XmlDocument doc = new XmlDocument();
                    doc.LoadXml(xml);
                    XmlNode errorNode = doc.SelectSingleNode("//error");
                    if (errorNode != null &&
                        errorNode.Attributes["code"].Value == "maxlag")
                    {
                        Thread.Sleep(5 * 1000 * (tries + 1));
                        continue;
                    }
                    break;
                }
            }
            if (!document.HasChildNodes)
            {
                document.LoadXml(xml);
                XmlNode n = document.SelectSingleNode("//query-continue");
                if (n != null)
                {
                    string name = n.FirstChild.Attributes[0].Name;
                    string value = n.FirstChild.Attributes[0].Value;
                    return new Parameter(value, name);
                }
            }
            else
            {
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(xml);
                XmlNode errorNode = doc.SelectSingleNode("//error");
                if (errorNode != null &&
                    errorNode.Attributes["code"].Value == "maxlag")
                {
                    throw new WikiException("Query failed: " + errorNode.Attributes["info"].Value);
                }
                XmlNode n = doc.SelectSingleNode("/api/query");

                foreach (XmlNode node in n.ChildNodes)
                {
                    XmlNode root = document.SelectSingleNode("/api/query/" + node.Name);
                    if (root == null)
                    {
                        continue;
                    }
                    foreach (XmlNode childNode in node.ChildNodes)
                    {
                        XmlNode importedNode = document.ImportNode(childNode, true);
                        root.AppendChild(importedNode);
                    }
                }

                n = doc.SelectSingleNode("//query-continue");
                if (n != null)
                {
                    string name = n.FirstChild.Attributes[0].Name;
                    string value = n.FirstChild.Attributes[0].Value;
                    return new Parameter(value, name);
                }
            }
            return null;
        }

        public string PrepareQuery(Action action, ParameterCollection parameters)
        {
            string query = "";
            switch (action)
            {
                default:
                    query = "action=" + action.ToString().ToLower();
                    break;
            }
            StringBuilder attributes = new StringBuilder();
            foreach (KeyValuePair<string, string> pair in parameters)
            {
                attributes.Append(string.Format("&{0}={1}",
                    pair.Key,
                    EscapeString(pair.Value)));
            }
            query += attributes.ToString();
            return query;
        }

        private byte[] PrepareMultipartFormQuery(ParameterCollection parameters, string filename, string contentType, byte[] data)
        {
            string boundary = "----------ThIs_Is_tHe_bouNdaRY_$";
            List<string> lines = new List<string>();
            lines.Add("--" + boundary);
            lines.Add("Content-Disposition: form-data; name=\"action\"");
            lines.Add("");
            lines.Add("upload");

            foreach (KeyValuePair<string, string> pair in parameters)
            {
                lines.Add("--" + boundary);
                lines.Add(string.Format("Content-Disposition: form-data; name=\"{0}\"", pair.Key));
                lines.Add("");
                lines.Add(pair.Value);
            }

            lines.Add("--" + boundary);
            lines.Add(string.Format("Content-Disposition: form-data; name=\"file\"; filename=\"{0}\"", filename));
            lines.Add(string.Format("Content-Type: {0}", contentType));
            lines.Add("");         

            string header = string.Join("\r\n", lines.ToArray()) + "\r\n";
            List<byte> bytes = new List<byte>();
            bytes.AddRange(Encoding.UTF8.GetBytes(header));
            bytes.AddRange(data);

            lines.Clear();
            lines.Add("--" + boundary + "--");
            lines.Add("");
            string footer = "\r\n" + string.Join("\r\n", lines.ToArray());
            bytes.AddRange(Encoding.UTF8.GetBytes(footer));
            return bytes.ToArray();
        }

        private static Stream GetResponseStream(HttpWebResponse response)
        {
            if (!_isRunningOnMono)
            {
                return response.GetResponseStream();
            }
            if (response.ContentEncoding.ToLower().Contains("gzip"))
            {
                return new GZipStream(response.GetResponseStream(), CompressionMode.Decompress);
            }
            else if (response.ContentEncoding.ToLower().Contains("deflate"))
            {
                return new DeflateStream(response.GetResponseStream(), CompressionMode.Decompress);
            }
            return response.GetResponseStream();
        }

        private HttpWebRequest PrepareRequest()
        {
            UriBuilder ub = new UriBuilder(_uri);
            ub.Path += "api.php";
            return PrepareRequest(ub.Uri, RequestMethod.Post);
        }

        private HttpWebRequest PrepareRequest(Uri uri, RequestMethod method)
        {
            return PrepareRequest(uri, method, "application/x-www-form-urlencoded");
        }

        private HttpWebRequest PrepareRequest(Uri uri, RequestMethod method, string contentType)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
            request.AllowAutoRedirect = false;
            request.Method = method.ToString().ToUpper();
            if (method == RequestMethod.Post)
            {
                request.ContentType = contentType;
            }
            if (!_isRunningOnMono)
            {
                request.AutomaticDecompression = DecompressionMethods.GZip |
                    DecompressionMethods.Deflate;
            }
            else
            {
                request.Headers.Add(HttpRequestHeader.AcceptEncoding, "gzip,deflate");
            }
            request.ServicePoint.Expect100Continue = false;
            request.Expect = "";
            request.UserAgent = _userAgent;
            request.CookieContainer = new CookieContainer();
            if (_cookies != null && _cookies.Count > 0)
            {
                request.CookieContainer = _cookies;
            }
            return request;
        }

        /// <summary>
        /// Converts a string to its escaped representation.
        /// </summary>
        /// <param name="text">The string to escape.</param>
        /// <returns>A System.String that contains the escaped representation of stringToEscape.</returns>
        /// <exception cref="System.ArgumentNullException">stringToEscape is null.</exception>
        private static string EscapeString(string stringToEscape)
        {
            const int max = 32766;
            StringBuilder result = new StringBuilder();
            int index = 0;
            while (index < stringToEscape.Length)
            {
                int diff = stringToEscape.Length - index;
                int count = Math.Min(diff, max);
                string substring = stringToEscape.Substring(index, count);
                result.Append(Uri.EscapeDataString(substring));
                index += count;
            }
            return result.ToString();
        }

        private static string ComputeHashString(string stringToHash)
        {
            byte[] input = Encoding.UTF8.GetBytes(stringToHash);
            byte[] output = MD5.Create().ComputeHash(input);
            StringBuilder hash = new StringBuilder();
            for (int i = 0; i < output.Length; ++i)
            {
                hash.Append(output[i].ToString("x2"));
            }
            return hash.ToString();
        }

        private static WikiException MakeActionException(Action action, string error)
        {
            string message = action.ToString() + " failed with error '" + error + "'.";
            switch (action)
            {
                case Action.Edit:
                    return new EditException(message);
                case Action.Login:
                    return new LoginException(message);
                case Action.Move:
                    return new MoveException(message);
                default:
                    return new WikiException(message);
            }
        }

        private class Parameter
        {
            private readonly string _value;
            private readonly string _name;

            public Parameter(string value, string name)
            {
                _value = value;
                _name = name;
            }

            public string Value
            {
                get { return _value; }
            }

            public string Name
            {
                get { return _name; }
            }
        }

        public class Protection
        {
            public Action Action { get; private set; }
            public UserGroup Group { get; private set; }
            public string Expiry { get; private set; }

            public Protection(Action action, UserGroup group)
                : this(action, group, "")
            {
            }

            public Protection(Action action, UserGroup group, string expiry)
            {
                Action = action;
                Group = group;
                Expiry = expiry;
            }

            public override string ToString()
            {
                return string.Format("{0}={1}", Action, Group).ToLower();
            }
        }
    }

    public enum RequestMethod
    {
        Get,
        Post
    }

    public enum Action
    {
        Login,
        Logout,
        Edit,
        Move,
        Query,
        Review,
        Delete,
        Protect,
        SiteMatrix,
        OpenSearch,
        ClickTracking,
        ExpandTemplates,
        Parse,
        FeedWatchList,
        ParamInfo,
        Purge,
        Rollback,
        Undelete,
        Block,
        Unblock,
        Upload,
        EmailUser,
        Watch,
        UserRights
    }

    public enum CreateFlags
    {
        None,
        NoCreate,
        CreateOnly,
        Recreate,
    }

    public enum MinorFlags
    {
        None,
        NotMinor,
        Minor
    }

    public enum WatchFlags
    {
        None,
        Watch,
        Unwatch,
        NoChange
    }

    public enum QueryBy
    {
        Titles,
        Revisions,
        IDs
    }

    public enum SaveFlags
    {
        Replace,
        Append,
        Prepend
    }

    public enum UserGroup
    {
        None,
        Autoconfirmed,
        Sysop
    }
}
