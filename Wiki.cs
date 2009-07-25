using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Xml;
using System.IO.Compression;

namespace Claymore.SharpMediaWiki
{
    public class Wiki
    {
        private CookieCollection _cookies;
        private readonly Uri _uri;
        private int _maxLag;
        private string _userAgent;
        private DateTime _lastQueryTime;
        private DateTime _lastEditTime;
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
        /// <example>Wiki wiki = new Wiki("http://en.wikipedia.org/");</example>
        /// <exception cref="System.ArgumentNullException" />
        /// <exception cref="System.UriFormatException" />
        public Wiki(string uri)
        {
            UriBuilder ub = new UriBuilder(uri);
            _uri = ub.Uri;
            _highLimits = false;
            _isBot = true;
            _maxLag = 5;
            SleepBetweenEdits = 10;
            SleepBetweenQueries = 10;
            Version version = Assembly.GetExecutingAssembly().GetName().Version;
            _userAgent = string.Format("SharpMediaWiki/{0}.{1}",
                version.Major, version.Minor);
            _namespaces = new Dictionary<string, int>();
        }

        public int MaxLag
        {
            get { return _maxLag; }
            set { _maxLag = Math.Max(5, value); }
        }

        public int SleepBetweenEdits
        {
            set { _sleepBetweenEdits = value * 1000; }
        }

        public int SleepBetweenQueries
        {
            set { _sleepBetweenQueries = value * 1000; }
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
            if (_cookies != null && _cookies.Count > 0 && username == _username)
            {
                return;
            }
            ParameterCollection parameters = new ParameterCollection();
            parameters.Add("lgname", username);
            parameters.Add("lgpassword", password);

            try
            {
                XmlDocument xml = MakeRequest(Action.Login, parameters);
                string result = xml.SelectSingleNode("//login").Attributes["result"].Value;
                if (result != "Success")
                {
                    throw new WikiException("Login failed, server returned '" + result + "'.");
                }
                
                _username = username;

                parameters.Clear();
                parameters.Add("meta", "userinfo");
                parameters.Add("uiprop", "rights");

                XmlDocument doc = MakeRequest(Action.Query, parameters);
                _highLimits = doc.SelectSingleNode("//rights[r=\"apihighlimits\"]/r") != null;
                _isBot = doc.SelectSingleNode("//rights[r=\"bot\"]/r") != null;
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
                ParameterCollection parameters = new ParameterCollection();
                parameters.Add("meta", "userinfo");
                parameters.Add("uiprop", "rights");

                XmlDocument doc = MakeRequest(Action.Query, parameters);
                _highLimits = doc.SelectSingleNode("//rights[r=\"apihighlimits\"]/r") != null;
                _isBot = doc.SelectSingleNode("//rights[r=\"bot\"]/r") != null;
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
            }
            catch (WebException e)
            {
                throw new WikiException("Logout failed", e);
            }
        }

        /// <summary>
        /// Loads raw text of the given page and returns it as System.String.
        /// </summary>
        /// <param name="title">Title of the page to load.</param>
        /// <returns>A System.String that contains raw text of the page.</returns>
        public string LoadPage(string title)
        {
            if (string.IsNullOrEmpty(title))
            {
                throw new ArgumentException("Title shouldn't be empty.", "title");
            }
            TimeSpan diff = DateTime.Now - _lastQueryTime;
            if (diff.Milliseconds < _sleepBetweenQueries)
            {
                Thread.Sleep(_sleepBetweenQueries - diff.Milliseconds);
            }

            UriBuilder ub = new UriBuilder(_uri);
            ub.Path = "/w/index.php";
            ub.Query = string.Format("title={0}&redirect=no&action=raw&ctype=text/plain&dontcountme=1",
                    Uri.EscapeDataString(title));
            try
            {
                HttpWebRequest request = PrepareRequest(ub.Uri, "GET");
                WebResponse response = request.GetResponse();
                using (StreamReader sr =
                    new StreamReader(GetResponseStream((HttpWebResponse)response)))
                {
                    _lastQueryTime = DateTime.Now;
                    return sr.ReadToEnd();
                }
            }
            catch (WebException e)
            {
                _lastQueryTime = DateTime.Now;
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

        public string SavePage(string title, string text, string comment)
        {
            return SavePage(title, "", text, comment,
                MinorFlags.Minor, CreateFlags.NoCreate, WatchFlags.None, SaveFlags.Replace);
        }

        public void AppendTextToPage(string title, string text, string comment,
            MinorFlags minor, WatchFlags watch)
        {
            SavePage(title, null, text, comment, minor, CreateFlags.NoCreate,
                watch, SaveFlags.Append);
        }

        public void PrependTextToPage(string title, string text, string comment,
            MinorFlags minor, WatchFlags watch)
        {
            SavePage(title, null, text, comment, minor, CreateFlags.NoCreate,
                watch, SaveFlags.Prepend);
        }

        public string SavePage(string title, string section, string text, string summary,
            MinorFlags minor, CreateFlags create, WatchFlags watch, SaveFlags mode,
            string basetimestamp, string starttimestamp, string editToken)
        {
            try
            {
                ParameterCollection parameters = new ParameterCollection();
                parameters.Add("title", title);
                if (mode == SaveFlags.Replace && !string.IsNullOrEmpty(section))
                {
                    parameters.Add("section", section);
                }
                parameters.Add("token", editToken);
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
                if (_isBot)
                {
                    parameters.Add("bot");
                }
                if (!string.IsNullOrEmpty(basetimestamp))
                {
                    parameters.Add("basetimestamp", basetimestamp);
                }
                parameters.Add("starttimestamp", starttimestamp);
                parameters.Add("summary", summary);
                //parameters.Add("md5", ComputeHashString(text));
                parameters.Add("maxlag", MaxLag.ToString());

                XmlDocument xml = MakeRequest(Action.Edit, parameters);
                XmlNode result = xml.SelectSingleNode("//edit[@newrevid]");
                if (result != null)
                {
                    return result.Attributes["newrevid"].Value;
                }
                else
                {
                    return null;
                }
            }
            catch (WebException e)
            {
                throw new WikiException("Saving failed", e);
            }
        }

        /// <summary>
        /// Saves or creates a page or a page section.
        /// </summary>
        /// <param name="title">A page title.</param>
        /// <param name="section">A section. Use "0" for the top section and "new" for a new one.</param>
        /// <param name="text">Text of the page or section.</param>
        /// <param name="summary">Edit summary. If section is "new" it will be used as section title.</param>
        /// <param name="minor">Minor flag.</param>
        /// <param name="create">Create flag.</param>
        /// <param name="watch">Watch flag.</param>
        /// <remarks><see cref="http://www.mediawiki.org/wiki/API:Edit_-_Create%26Edit_pages"/></remarks>
        public string SavePage(string title, string section, string text, string summary,
            MinorFlags minor, CreateFlags create, WatchFlags watch, SaveFlags mode)
        {
            if (string.IsNullOrEmpty(title))
            {
                throw new ArgumentException("Title shouldn't be empty.", "title");
            }
            if (string.IsNullOrEmpty(text))
            {
                throw new ArgumentException("Text shouldn't be empty.", "text");
            }
            try
            {
                ParameterCollection parameters = new ParameterCollection();
                parameters.Add("prop", "info|revisions");
                parameters.Add("rvprop", "timestamp");
                parameters.Add("intoken", "edit");
                parameters.Add("titles", title);

                XmlDocument doc = MakeRequest(Action.Query, parameters);
                XmlNode node = doc.SelectSingleNode("//page");
                if (node.Attributes["edittoken"] == null)
                {
                    throw new WikiException("Saving failed: couldn't get edit token");
                }
                string editToken = node.Attributes["edittoken"].Value;
                string realTitle = node.Attributes["title"].Value;
                node = doc.SelectSingleNode("//rev");
                string baseTimeStamp = null;
                if (node != null && node.Attributes["timestamp"] != null)
                {
                    baseTimeStamp = node.Attributes["timestamp"].Value;
                }
                string starttimestamp = DateTime.Now.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");

                parameters.Clear();
                parameters.Add("title", realTitle);
                if (mode == SaveFlags.Replace && !string.IsNullOrEmpty(section))
                {
                    parameters.Add("section", section);
                }
                parameters.Add("token", editToken);
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
                if (_isBot)
                {
                    parameters.Add("bot");
                }
                if (!string.IsNullOrEmpty(baseTimeStamp))
                {
                    parameters.Add("basetimestamp", baseTimeStamp);
                }
                parameters.Add("starttimestamp", starttimestamp);
                parameters.Add("summary", summary);
                //parameters.Add("md5", ComputeHashString(text));
                parameters.Add("maxlag", MaxLag.ToString());

                XmlDocument xml = MakeRequest(Action.Edit, parameters);
                XmlNode result = xml.SelectSingleNode("//edit[@newrevid]");
                if (result != null)
                {
                    return result.Attributes["newrevid"].Value;
                }
                else
                {
                    return null;
                }
            }
            catch (WebException e)
            {
                throw new WikiException("Saving failed", e);
            }
        }

        public void MovePage(string fromTitle, string toTitle, string reason,
            bool moveTalk, bool noRedirect)
        {
            ParameterCollection parameters = new ParameterCollection();
            parameters.Add("prop", "info");
            parameters.Add("intoken", "move");
            parameters.Add("titles", fromTitle);
            parameters.Add("maxlag", MaxLag.ToString());

            XmlDocument doc = MakeRequest(Action.Query, parameters);
            XmlNode node = doc.SelectSingleNode("//page");
            string token = "";
            if (node.Attributes["movetoken"] != null)
            {
                token = node.Attributes["movetoken"].Value;
            }
            string realTitle = node.Attributes["title"].Value;

            parameters.Clear();
            parameters.Add("from", fromTitle);
            parameters.Add("to", toTitle);
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

            MakeRequest(Action.Move, parameters);
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

        private void Enumerate(ParameterCollection parameters, XmlDocument result)
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
                
                if (parameter == null)
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
            IEnumerable<string> ids)
        {
            return Query(queryBy, parameters, ids, _highLimits ? 500 : 50);
        }

        public XmlDocument Query(QueryBy queryBy,
            ParameterCollection parameters,
            IEnumerable<string> ids,
            int limit)
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
                    Enumerate(localParameters, document);

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
                Enumerate(localParameters, document);
            }
            return document;
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
            ParameterCollection parameters = new ParameterCollection();
            parameters.Add("meta", "siteinfo");
            parameters.Add("siprop", "namespaces");
            XmlDocument xml = Enumerate(parameters, true);
            XmlNodeList nodes = xml.SelectNodes("//ns[@id > 0]");
            foreach (XmlNode node in nodes)
            {
                _namespaces.Add(node.FirstChild.Value, int.Parse(node.Attributes["id"].Value));
            }
        }

        public bool LoadNamespaces()
        {
            return LoadNamespaces(@"Cache\namespaces.dat");
        }

        public void SaveNamespaces()
        {
            SaveNamespaces(@"Cache\namespaces.dat");
        }

        public bool LoadNamespaces(string filename)
        {
            if (!File.Exists(filename))
            {
                return false;
            }
            _namespaces.Clear();
            Deserializer deserializer = new Deserializer(File.ReadAllBytes(filename));
            int count = deserializer.GetInt();
            for (int i = 0; i < count; ++i)
            {
                string ns = deserializer.GetString();
                int number = deserializer.GetInt();
                _namespaces.Add(ns, number);
            }
            return true;
        }

        public void SaveNamespaces(string filename)
        {
            Serializer serializer = new Serializer();
            serializer.Put(_namespaces.Count);
            foreach (var pair in _namespaces)
            {
                serializer.Put(pair.Key);
                serializer.Put(pair.Value);
            }
            using (FileStream fs = new FileStream(filename, FileMode.CreateNew))
            using (BinaryWriter streamWriter = new BinaryWriter(fs))
            {
                streamWriter.Write(serializer.ToArray());
            }
        }

        private string PrepareQuery(Action action, ParameterCollection parameters)
        {
            string query = "";
            switch (action)
            {
                case Action.Login:
                    query = "action=login";
                    break;
                case Action.Logout:
                    query = "action=logout";
                    break;
                case Action.Query:
                    query = "action=query";
                    break;
                case Action.Edit:
                    query = "action=edit";
                    break;
                case Action.Move:
                    query = "action=move";
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

        public XmlDocument MakeRequest(Action action, ParameterCollection parameters)
        {
            TimeSpan diff = DateTime.Now - _lastEditTime;
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
            node = doc.SelectSingleNode(action.ToString().ToLower());
            if (node != null && node.Attributes["result"] != null)
            {
                string result = node.Attributes["result"].Value;
                if (result != "Success")
                {
                    throw MakeActionException(action, result);
                }
            }

            if (action == Action.Login &&
                response.Cookies != null &&
                response.Cookies.Count > 0)
            {
                _cookies = response.Cookies;
            }

            if (action == Action.Edit)
            {
                _lastEditTime = DateTime.Now;
            }
            return doc;
        }

        private Parameter FillDocumentWithQueryResults(string query, XmlDocument document)
        {
            TimeSpan diff = DateTime.Now - _lastQueryTime;
            if (diff.Milliseconds < _sleepBetweenQueries)
            {
                Thread.Sleep(_sleepBetweenQueries - diff.Milliseconds);
            }

            string xml = "";
            HttpWebRequest request = PrepareRequest();
            using (StreamWriter sw =
                new StreamWriter(request.GetRequestStream()))
            {
                sw.Write(query);
            }
            WebResponse response = request.GetResponse();
            using (StreamReader sr =
                new StreamReader(GetResponseStream((HttpWebResponse)response)))
            {
                xml = sr.ReadToEnd();
                _lastQueryTime = DateTime.Now;
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

        public byte[] CookiesToArray()
        {
            Serializer serializer = new Serializer();
            serializer.Put(_cookies.Count);
            for (int i = 0; i < _cookies.Count; ++i)
            {
                serializer.Put(_cookies[i].Name);
                serializer.Put(_cookies[i].Value);
                serializer.Put(_cookies[i].Path);
                serializer.Put(_cookies[i].Domain);
            }
            return serializer.ToArray();
        }

        public void CacheCookies(string fileName)
        {
            using (FileStream fs = new FileStream(fileName, FileMode.Create))
            using (GZipStream gs = new GZipStream(fs, CompressionMode.Compress))
            {
                byte[] data = CookiesToArray();
                gs.Write(data, 0, data.Length);
            }
        }

        public void CacheCookies()
        {
            Directory.CreateDirectory("Cache");
            string filename = @"Cache\cookie.jar";
            CacheCookies(filename);
        }

        public void LoadCookies(byte[] data)
        {
            _cookies = new CookieCollection();
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

        public bool LoadCookies()
        {
            return LoadCookies(@"Cache\cookie.jar");
        }

        public bool LoadCookies(string fileName)
        {
            if (!File.Exists(fileName))
            {
                return false;
            }
            using (FileStream fs = new FileStream(fileName, FileMode.Open))
            using (GZipStream gs = new GZipStream(fs, CompressionMode.Decompress))
            using (BinaryReader sr = new BinaryReader(gs))
            {
                List<byte> data = new List<byte>();
                int b;
                while ((b = sr.BaseStream.ReadByte()) != -1)
                {
                    data.Add((byte)b);
                }
                LoadCookies(data.ToArray());
            }
            return true;
        }

        private HttpWebRequest PrepareRequest()
        {
            UriBuilder ub = new UriBuilder(_uri);
            ub.Path = "/w/api.php";
            return PrepareRequest(ub.Uri, "POST");
        }

        private HttpWebRequest PrepareRequest(Uri uri, string method)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
            request.AllowAutoRedirect = false;
            request.Method = method;
            if (request.Method == "POST")
            {
                request.ContentType = "application/x-www-form-urlencoded";
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
            request.ProtocolVersion = HttpVersion.Version10;
            request.UserAgent = _userAgent;
            request.CookieContainer = new CookieContainer();
            if (_cookies != null && _cookies.Count > 0)
            {
                request.CookieContainer.Add(_cookies);
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
    }

    public enum Action
    {
        Login,
        Logout,
        Edit,
        Move,
        Query
    }

    public enum CreateFlags
    {
        None,
        NoCreate,
        CreateOnly,
        Recreate
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
        Unwatch
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
}
