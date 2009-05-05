using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Xml;
using System.Security.Cryptography;

namespace Claymore.SharpMediaWiki
{
    public class Wiki
    {
        private CookieCollection _cookies;
        private readonly Uri _uri;
        private bool _isBot;

        /// <summary>
        /// Initializes a new instance of the Wiki class with the specified URI.
        /// </summary>
        /// <param name="url">A URI string.</param>
        /// <example>Wiki wiki = new Wiki("http://en.wikipedia.org/")</example>
        /// <exception cref="System.ArgumentNullException" />
        /// <exception cref="System.UriFormatException" />
        public Wiki(string uri)
        {
            UriBuilder ub = new UriBuilder(uri);
            _uri = ub.Uri;
            _isBot = false;
        }

        /// <summary>
        /// Indicates if logged in user has bot rights.
        /// </summary>
        public bool Boot
        {
            get { return _isBot; }
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
            ParameterCollection parameters = new ParameterCollection();
            parameters.Add("lgname", user);
            parameters.Add("lgpassword", password);
            
            XmlDocument doc = MakeRequest(Action.Login, parameters);
            XmlNode resultNode = doc.SelectSingleNode("/api/login");
            if (resultNode.Attributes["result"] != null)
            {
                string result = resultNode.Attributes["result"].Value;
                if (result != "Success")
                {
                    throw new LoginException(result);
                }
            }

            parameters.Clear();
            parameters.Add("list", "users");
            parameters.Add("usprop", "groups");
            parameters.Add("ususers", user);

            doc = MakeRequest(Action.Query, parameters);
            XmlNodeList nodes = doc.SelectNodes("/api/query/users/user/groups/g");
            foreach (XmlNode node in nodes)
            {
                if (node.Value == "bot")
                {
                    _isBot = true;
                    break;
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
        }

        /// <summary>
        /// Loads raw text of the given page and returns it as System.String.
        /// </summary>
        /// <param name="title">Title of the page to load.</param>
        /// <returns>A System.String that contains raw text of the page.</returns>
        public string LoadPage(string title)
        {
            UriBuilder ub = new UriBuilder(_uri);
            ub.Path = "/w/index.php";
            ub.Query = string.Format("title={0}&redirect=no&action=raw&ctype=text/plain&dontcountme=1",
                    Uri.EscapeDataString(title));
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(ub.Uri);
            request.AllowAutoRedirect = false;
            request.Method = "GET";
            request.AutomaticDecompression = DecompressionMethods.GZip;
            request.ProtocolVersion = HttpVersion.Version10;
            request.CookieContainer = new CookieContainer();
            if (_cookies != null && _cookies.Count > 0)
            {
                request.CookieContainer.Add(_cookies);
            }

            HttpWebResponse response = (HttpWebResponse)request.GetResponse();
            using (StreamReader sr = new StreamReader(response.GetResponseStream()))
            {
                return sr.ReadToEnd();
            }
        }

        public void SavePage(string title, string text, string comment)
        {
            SavePage(title, text, comment,
                MinorFlags.Minor, CreateFlags.NoCreate, WatchFlags.None);
        }

        public void SavePage(string title, string text, string comment,
            MinorFlags minor, CreateFlags create, WatchFlags watch)
        {
            ParameterCollection parameters = new ParameterCollection();
            parameters.Add("prop", "info|revisions");
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
            parameters.Add("token", editToken);
            parameters.Add("text", text);
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
            parameters.Add("basetimestamp", baseTimeStamp);
            parameters.Add("starttimestamp", starttimestamp);
            parameters.Add("summary", comment);
            
            byte[] input = Encoding.UTF8.GetBytes(text);
            byte[] output = MD5.Create().ComputeHash(input);
            StringBuilder hash = new StringBuilder();
            for (int i = 0; i < output.Length; i++)
            {
                hash.Append(output[i].ToString("x2"));
            }
            parameters.Add("md5", hash.ToString());
            
            doc = MakeRequest(Action.Edit, parameters);
            node = doc.SelectSingleNode("/api/edit");
            if (node != null && node.Attributes["result"] != null)
            {
                string result = node.Attributes["result"].Value;
                if (result != "Success")
                {
                    throw new EditException(result);
                }
            }
            else
            {
                throw new EditException("Unknown error.");
            }
        }

        public XmlDocument QueryTitles(ParameterCollection parameters, IEnumerable<string> titles, int limit)
        {
            XmlDocument document = new XmlDocument();
            StringBuilder titlesString = new StringBuilder();
            int index = 0;
            foreach (string title in titles)
            {
                if (index < limit)
                {
                    titlesString.Append("|" + Uri.EscapeDataString(title));
                    ++index;
                }
                else
                {
                    titlesString.Remove(0, 1);
                    ParameterCollection localParameters = new ParameterCollection(parameters);
                    localParameters.Add("titles", titlesString.ToString());
                    string query = PrepareQuery(Action.Query, parameters);
                    FillDocumentWithQueryResults(query, document);

                    index = 1;
                    titlesString = new StringBuilder("|" + Uri.EscapeDataString(title));
                }
            }
            if (index > 0)
            {
                titlesString.Remove(0, 1);
                ParameterCollection localParameters = new ParameterCollection(parameters);
                localParameters.Add("titles", titlesString.ToString());
                string query = PrepareQuery(Action.Query, parameters);
                FillDocumentWithQueryResults(query, document);
            }
            return document;
        }

        private string PrepareQuery(Action action, IEnumerable<KeyValuePair<string, string>> parameters)
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
            }
            StringBuilder attributes = new StringBuilder();
            foreach (KeyValuePair<string, string> pair in parameters)
            {
                attributes.Append(string.Format("&{0}={1}",
                    pair.Key,
                    string.IsNullOrEmpty(pair.Value)
                        ? "1"
                        : EscapeString(pair.Value)));
            }
            attributes.Append("&format=xml");
            query += attributes.ToString();
            return query;
        }

        private XmlDocument MakeRequest(Action action, IEnumerable<KeyValuePair<string, string>> parameters)
        {
            string query = PrepareQuery(action, parameters);
            HttpWebRequest request = PrepareRequest();
            using (StreamWriter sw = new StreamWriter(request.GetRequestStream()))
            {
                sw.Write(query);
            }
            XmlDocument doc = new XmlDocument();
            HttpWebResponse response = (HttpWebResponse)request.GetResponse();
            using (StreamReader sr = new StreamReader(response.GetResponseStream()))
            {
                doc.LoadXml(sr.ReadToEnd());
            }

            if (action == Action.Login &&
                response.Cookies != null &&
                response.Cookies.Count > 0)
            {
                _cookies = response.Cookies;
            }
            return doc;
        }

        private void FillDocumentWithQueryResults(string query, XmlDocument document)
        {
            HttpWebRequest request = PrepareRequest();
            using (StreamWriter sw = new StreamWriter(request.GetRequestStream()))
            {
                sw.Write(query);
            }
            WebResponse response = request.GetResponse();
            string xml;
            using (StreamReader sr = new StreamReader(response.GetResponseStream()))
            {
                xml = sr.ReadToEnd();
            }
            if (!document.HasChildNodes)
            {
                document.LoadXml(xml);
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
            }
        }

        private HttpWebRequest PrepareRequest()
        {
            UriBuilder ub = new UriBuilder(_uri);
            ub.Path = "/w/api.php";
            return PrepareRequest(ub.Uri);
        }

        private HttpWebRequest PrepareRequest(Uri uri)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
            request.AllowAutoRedirect = false;
            request.Method = "POST";
            request.ContentType = "application/x-www-form-urlencoded";
            request.AutomaticDecompression = DecompressionMethods.GZip;
            request.ProtocolVersion = HttpVersion.Version10;
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
    }

    public enum Action
    {
        Login,
        Logout,
        Edit,
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
}
