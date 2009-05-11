using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Xml;
using System.Web;

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

        /// <summary>
        /// Logs into the Wiki as 'user' with 'password'.
        /// </summary>
        /// <param name="user">A username on the Wiki.</param>
        /// <param name="password">A password.</param>
        /// <exception cref="Claymore.SharpMediaWiki.LoginException">Thrown when login fails.</exception>
        /// <remarks><see cref="http://www.mediawiki.org/wiki/API:Login"/></remarks>
        public void Login(string user, string password)
        {
            if (_cookies != null && _cookies.Count > 0 && user == _username)
            {
                return;
            }
            ParameterCollection parameters = new ParameterCollection();
            parameters.Add("lgname", user);
            parameters.Add("lgpassword", password);

            MakeRequest(Action.Login, parameters);
            _username = user;

            parameters.Clear();
            parameters.Add("meta", "userinfo");
            parameters.Add("uiprop", "rights");

            XmlDocument doc = MakeRequest(Action.Query, parameters);
            XmlNodeList nodes = doc.SelectNodes("/api/query/userinfo/rights/r");
            foreach (XmlNode node in nodes)
            {
                if (node.FirstChild.Value == "apihighlimits")
                {
                    _highLimits = true;
                }
                else if (node.FirstChild.Value == "bot")
                {
                    _isBot = true;
                }
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

        /// <summary>
        /// Loads raw text of the given page and returns it as System.String.
        /// </summary>
        /// <param name="title">Title of the page to load.</param>
        /// <returns>A System.String that contains raw text of the page.</returns>
        public string LoadPage(string title)
        {
            TimeSpan diff = DateTime.Now - _lastQueryTime;
            if (diff.Milliseconds < _sleepBetweenQueries)
            {
                Thread.Sleep(_sleepBetweenQueries - diff.Milliseconds);
            }

            UriBuilder ub = new UriBuilder(_uri);
            ub.Path = "/w/index.php";
            ub.Query = string.Format("title={0}&redirect=no&action=raw&ctype=text/plain&dontcountme=1",
                    Uri.EscapeDataString(title));
            for (int tries = 0; tries < 3; ++tries)
            {
                try
                {
                    HttpWebRequest request = PrepareRequest(ub.Uri, "GET");
                    WebResponse response = request.GetResponse();
                    using (StreamReader sr =
                        new StreamReader(response.GetResponseStream()))
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
                }
            }
            return null;
        }

        public void SavePage(string title, string text, string comment)
        {
            SavePage(title, "", text, comment,
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
        public void SavePage(string title, string section, string text, string summary,
            MinorFlags minor, CreateFlags create, WatchFlags watch, SaveFlags mode)
        {
            for (int tries = 0; tries < 3; ++tries)
            {
                try
                {
                    ParameterCollection parameters = new ParameterCollection();
                    parameters.Add("prop", "info|revisions");
                    parameters.Add("rvprop", "timestamp");
                    parameters.Add("intoken", "edit");
                    parameters.Add("titles", title);

                    XmlDocument doc = MakeRequest(Action.Query, parameters);
                    XmlNode node = doc.SelectSingleNode("/api/query/pages/page");
                    string editToken = "";
                    if (node.Attributes["edittoken"] != null)
                    {
                        editToken = node.Attributes["edittoken"].Value;
                    }
                    string realTitle = node.Attributes["title"].Value;
                    node = doc.SelectSingleNode("/api/query/pages/page/revisions/rev");
                    string baseTimeStamp = node.Attributes["timestamp"].Value;
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
                    parameters.Add("basetimestamp", baseTimeStamp);
                    parameters.Add("starttimestamp", starttimestamp);
                    parameters.Add("summary", summary);
                    parameters.Add("md5", ComputeHashString(text));
                    parameters.Add("maxlag", MaxLag.ToString());

                    MakeRequest(Action.Edit, parameters);
                    break;
                }
                catch (WebException)
                {
                    continue;
                }
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
            XmlNode node = doc.SelectSingleNode("/api/query/pages/page");
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
                for (int tries = 0; tries < 3; ++tries)
                {
                    try
                    {
                        parameter = FillDocumentWithQueryResults(query, result);
                        break;
                    }
                    catch (WebException)
                    {
                        continue;
                    }
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
                    FillDocumentWithQueryResults(query, document);

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
                FillDocumentWithQueryResults(query, document);
            }
            return document;
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
                        new StreamReader(response.GetResponseStream()))
                    {
                        doc.LoadXml(sr.ReadToEnd());
                    }
                    break;
                }
            }

            XmlNode node = doc.SelectSingleNode("/api/error");
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
                new StreamReader(response.GetResponseStream()))
            {
                xml = sr.ReadToEnd();
                _lastQueryTime = DateTime.Now;
            }
            if (!document.HasChildNodes)
            {
                document.LoadXml(xml);
                XmlNode n = document.SelectSingleNode("/api/query-continue");
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
                    foreach (XmlNode childNode in node.ChildNodes)
                    {
                        XmlNode importedNode = document.ImportNode(childNode, true);
                        root.AppendChild(importedNode);
                    }
                }
                
                n = doc.SelectSingleNode("/api/query-continue");
                if (n != null)
                {
                    string name = n.FirstChild.Attributes[0].Name;
                    string value = n.FirstChild.Attributes[0].Value;
                    return new Parameter(value, name);
                }
            }
            return null;
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
            request.AutomaticDecompression = DecompressionMethods.GZip |
                DecompressionMethods.Deflate;
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
