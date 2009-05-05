using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Xml;

namespace Claymore.SharpMediaWiki
{
    public class Wiki
    {
        private CookieCollection _cookies;
        private readonly Uri _uri;

        public Wiki(string url)
        {
            UriBuilder ub = new UriBuilder(url);
            _uri = ub.Uri;
        }

        public void Login(string login, string password)
        {
            HttpWebRequest request = PrepareRequest();
            using (StreamWriter sw = new StreamWriter(request.GetRequestStream()))
            {
                sw.Write(string.Format("action=login&format=xml&lgname={0}&lgpassword={1}",
                    Uri.EscapeDataString(login), Uri.EscapeDataString(password)));
            }
            HttpWebResponse response = (HttpWebResponse)request.GetResponse();
            using (StreamReader sr = new StreamReader(response.GetResponseStream()))
            {
                XmlDocument xml = new XmlDocument();
                xml.LoadXml(sr.ReadToEnd());
                XmlNode node = xml.SelectSingleNode("/api/login");
                if (node.Attributes["result"] != null)
                {
                    string result = node.Attributes["result"].Value;
                    if (result != "Success")
                    {
                        throw new LoginException(result);
                    }
                }
            }
            _cookies = response.Cookies;
        }

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

        public static string EscapeString(string text)
        {
            const int max = 32766;
            StringBuilder result = new StringBuilder();
            int index = 0;
            while (index < text.Length)
            {
                int diff = text.Length - index;
                int count = Math.Min(diff, max);
                string substring = text.Substring(index, count);
                result.Append(Uri.EscapeDataString(substring));
                index += count;
            }
            return result.ToString();
        }

        public void SavePage(string title, string text, string comment)
        {
            string query = "action=query&format=xml&prop=info|revisions&intoken=edit&titles=" +
                Uri.EscapeDataString(title);
            XmlDocument doc = new XmlDocument();
            FillDocumentWithQueryResults(query, doc);
            XmlNode node = doc.SelectSingleNode("/api/query/pages/page");
            string editToken = null;
            if (node.Attributes["edittoken"] != null)
            {
                editToken = node.Attributes["edittoken"].Value;
            }
            string realTitle = node.Attributes["title"].Value;
            node = doc.SelectSingleNode("/api/query/pages/page/revisions/rev");
            string baseTimeStamp = node.Attributes["timestamp"].Value;
            string starttimestamp = DateTime.Now.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
            HttpWebRequest request = PrepareRequest();
            using (StreamWriter sw = new StreamWriter(request.GetRequestStream()))
            {
                sw.Write(string.Format("action=edit&format=xml&title={0}&token={1}&text={2}&minor=1&basetimestamp={3}&starttimestamp={4}&nocreate=1&summary={5}",
                    Uri.EscapeDataString(realTitle),
                    Uri.EscapeDataString(editToken),
                    EscapeString(text),
                    Uri.EscapeDataString(baseTimeStamp),
                    Uri.EscapeDataString(starttimestamp),
                    Uri.EscapeDataString(comment)));
            }
            HttpWebResponse response = (HttpWebResponse)request.GetResponse();
            using (StreamReader sr = new StreamReader(response.GetResponseStream()))
            {
                XmlDocument xml = new XmlDocument();
                xml.LoadXml(sr.ReadToEnd());
                node = xml.SelectSingleNode("/api/edit");
                if (node.Attributes["result"] != null)
                {
                    string result = node.Attributes["result"].Value;
                    if (result != "Success")
                    {
                        throw new EditException(result);
                    }
                }
            }
        }

        public XmlDocument Query(string parameters, IEnumerable<string> titles, int limit)
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
                    string query = "action=query&format=xml&" + parameters + "&titles=" + titlesString.ToString();
                    FillDocumentWithQueryResults(query, document);

                    index = 1;
                    titlesString = new StringBuilder("|" + Uri.EscapeDataString(title));
                }
            }
            if (index > 0)
            {
                titlesString.Remove(0, 1);
                string query = "action=query&format=xml&" + parameters + "&titles=" + titlesString.ToString();
                FillDocumentWithQueryResults(query, document);
            }
            return document;
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
    }
}
