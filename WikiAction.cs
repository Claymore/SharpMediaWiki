using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Claymore.SharpMediaWiki
{
    public class WikiAction
    {
        private readonly Wiki _wiki;
        protected readonly ParameterCollection _parameters;
        private readonly Action _action;

        public WikiAction(Wiki wiki, Action action)
        {
            _wiki = wiki;
            _action = action;
            _parameters = new ParameterCollection();
        }

        public override string ToString()
        {
            string query = "action=" + _action.ToString().ToLower();
            StringBuilder attributes = new StringBuilder();
            foreach (KeyValuePair<string, string> pair in _parameters)
            {
                attributes.Append(string.Format("&{0}={1}",
                    pair.Key,
                    Wiki.EscapeString(pair.Value)));
            }
            query += attributes.ToString();
            return query;
        }

        public string ToString(IEnumerable<KeyValuePair<string, string>> parameters)
        {
            string query = "action=" + _action.ToString().ToLower();
            StringBuilder attributes = new StringBuilder();
            foreach (KeyValuePair<string, string> pair in parameters)
            {
                attributes.Append(string.Format("&{0}={1}",
                    pair.Key,
                    Wiki.EscapeString(pair.Value)));
            }
            query += attributes.ToString();
            return query;
        }

        public Action Action
        {
            get { return _action; }
        }
    }

    public class WikiSiteMatrixAction : WikiAction
    {
        public WikiSiteMatrixAction(Wiki wiki) :
            base(wiki, Action.SiteMatrix)
        {
        }
    }

    public class WikiReviewAction : WikiAction
    {
        public WikiReviewAction(Wiki wiki, string revisionId, string comment, int flagAccuracy) :
            base(wiki, Action.Review)
        {
            _parameters.Add("revid", revisionId);
            _parameters.Add("comment", comment);
            _parameters.Add("flag_accuracy", flagAccuracy.ToString());
        }

        public WikiReviewAction(Wiki wiki, string revisionId, string comment) :
            base(wiki, Action.Review)
        {
            _parameters.Add("revid", revisionId);
            _parameters.Add("comment", comment);
        }

        public WikiReviewAction(Wiki wiki, string revisionId) :
            base(wiki, Action.Review)
        {
            _parameters.Add("revid", revisionId);
        }
    }

    public class WikiLoginAction : WikiAction
    {
        public WikiLoginAction(Wiki wiki, string username, string password, string domain) :
            base(wiki, Action.Login)
        {
            _parameters.Add("lgname", username);
            _parameters.Add("lgpassword", password);
            _parameters.Add("lgdomain", domain);
        }

        public WikiLoginAction(Wiki wiki, string username, string password) :
            base(wiki, Action.Login)
        {
            _parameters.Add("lgname", username);
            _parameters.Add("lgpassword", password);
        }
    }

    public class WikiLogoutAction : WikiAction
    {
        public WikiLogoutAction(Wiki wiki) :
            base(wiki, Action.Logout)
        {
        }
    }

    public class WikiQueryAction : WikiAction
    {
        private List<string> _queries;

        public WikiQueryAction(Wiki wiki,
            QueryBy queryBy,
            IEnumerable<string> ids,
            HashSet<WikiQueryProperty> properties,
            bool redirects,
            bool indexPageIds,
            bool export,
            bool exportNoWrap) :
            base(wiki, Action.Query)
        {
            _queries = new List<string>();
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

            if (redirects)
            {
                _parameters.Add("redirects");
            }
            if (indexPageIds)
            {
                _parameters.Add("indexpageids");
            }
            if (export)
            {
                _parameters.Add("export");
            }
            if (exportNoWrap)
            {
                _parameters.Add("exportnowrap");
            }

            StringBuilder propertiesString = new StringBuilder();
            foreach (WikiQueryProperty property in properties)
            {
                propertiesString.Append("|" + property.Name);
                foreach (KeyValuePair<string, string> parameter in property.Parameters)
                {
                    _parameters.Add(parameter.Key, parameter.Value);
                }
            }
            propertiesString.Remove(0, 1);
            _parameters.Add("prop", propertiesString.ToString());

            int limit = 3;
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
                    ParameterCollection localParameters = new ParameterCollection(_parameters);
                    localParameters.Add(keyword, idsString.ToString());
                    _queries.Add(ToString(localParameters));

                    index = 1;
                    idsString = new StringBuilder("|" + id);
                }
            }
            if (index > 0)
            {
                idsString.Remove(0, 1);
                ParameterCollection localParameters = new ParameterCollection(_parameters);
                localParameters.Add(keyword, idsString.ToString());
                _queries.Add(ToString(localParameters));
            }
        }

        public WikiQueryAction(Wiki wiki,
            QueryBy queryBy,
            IEnumerable<string> ids,
            WikiGenerator generator,
            HashSet<WikiQueryProperty> properties,
            HashSet<WikiQueryMeta> metas,
            bool redirects,
            bool indexPageIds,
            bool export,
            bool exportNoWrap) :
            base(wiki, Action.Query)
        {
            _queries = new List<string>();

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

            if (redirects)
            {
                _parameters.Add("redirects");
            }
            if (indexPageIds)
            {
                _parameters.Add("indexpageids");
            }
            if (export)
            {
                _parameters.Add("export");
            }
            if (exportNoWrap)
            {
                _parameters.Add("exportnowrap");
            }

            StringBuilder propertiesString = new StringBuilder();
            foreach (WikiQueryProperty property in properties)
            {
                propertiesString.Append("|" + property.Name);
                foreach (KeyValuePair<string, string> parameter in property.Parameters)
                {
                    _parameters.Add(parameter.Key, parameter.Value);
                }
            }
            propertiesString.Remove(0, 1);
            _parameters.Add("prop", propertiesString.ToString());
            
            foreach (KeyValuePair<string, string> parameter in generator.Parameters)
            {
                _parameters.Add(parameter.Key, parameter.Value);
            }

            propertiesString = new StringBuilder();
            foreach (WikiQueryMeta property in metas)
            {
                propertiesString.Append("|" + property.Name);
                foreach (KeyValuePair<string, string> parameter in property.Parameters)
                {
                    _parameters.Add(parameter.Key, parameter.Value);
                }
            }
            propertiesString.Remove(0, 1);
            _parameters.Add("meta", propertiesString.ToString());

            int limit = 3;
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
                    ParameterCollection localParameters = new ParameterCollection(_parameters);
                    localParameters.Add(keyword, idsString.ToString());
                    _queries.Add(ToString(localParameters));

                    index = 1;
                    idsString = new StringBuilder("|" + id);
                }
            }
            if (index > 0)
            {
                idsString.Remove(0, 1);
                ParameterCollection localParameters = new ParameterCollection(_parameters);
                localParameters.Add(keyword, idsString.ToString());
                _queries.Add(ToString(localParameters));
            }
        }
    }

    public class WikiQueryProperty
    {
        protected readonly Dictionary<string, string> _parameters;
        private readonly string _name;

        public WikiQueryProperty(string name)
        {
            _parameters = new Dictionary<string, string>();
            _name = name;
        }

        public override string ToString()
        {
            StringBuilder attributes = new StringBuilder();
            foreach (KeyValuePair<string, string> pair in _parameters)
            {
                attributes.Append(string.Format("&{0}={1}",
                    pair.Key,
                    Wiki.EscapeString(pair.Value)));
            }
            attributes.Remove(0, 1);
            return attributes.ToString();
        }

        public string Name
        {
            get { return _name; }
        }

        public IEnumerable<KeyValuePair<string, string>> Parameters
        {
            get { return _parameters; }
        }
    }

    public class WikiQueryMeta
    {
        protected readonly Dictionary<string, string> _parameters;
        private readonly string _name;

        public WikiQueryMeta(string name)
        {
            _parameters = new Dictionary<string, string>();
            _name = name;
        }

        public override string ToString()
        {
            StringBuilder attributes = new StringBuilder();
            foreach (KeyValuePair<string, string> pair in _parameters)
            {
                attributes.Append(string.Format("&{0}={1}",
                    pair.Key,
                    Wiki.EscapeString(pair.Value)));
            }
            attributes.Remove(0, 1);
            return attributes.ToString();
        }

        public string Name
        {
            get { return _name; }
        }

        public IEnumerable<KeyValuePair<string, string>> Parameters
        {
            get { return _parameters; }
        }
    }

    public class WikiQueryInfoProperty : WikiQueryProperty
    {
        public WikiQueryInfoProperty(Property attribute, Token token)
            : base("info")
        {
            string attributes = attribute.ToString().Replace(" ", "").ToLower();
            string tokens = token.ToString().Replace(" ", "").ToLower();
            _parameters.Add("inprop", attributes);
            _parameters.Add("intoken", tokens);
        }

        public WikiQueryInfoProperty(Property attribute)
            : base("info")
        {
            string attributes = attribute.ToString().Replace(" ", "").ToLower();
            _parameters.Add("inprop", attributes);
        }

        public WikiQueryInfoProperty(Token token)
            : base("info")
        {
            string tokens = token.ToString().Replace(" ", "").ToLower();
            _parameters.Add("intoken", tokens);
        }

        public WikiQueryInfoProperty()
            : base("info")
        {
        }

        [Flags]
        public enum Property
        {
            None = 0,
            Protection = 1,
            TalkId = 1 << 1,
            SubjectId = 1 << 2,
            Url = 1 << 3,
            Readable = 1 << 4
        }
        
        [Flags]
        public enum Token
        {
            None = 0,
            Edit = 1,
            Delete = 1 << 1,
            Protect = 1 << 2,
            Move = 1 << 3,
            Block = 1 << 4,
            Unblock = 1 << 5,
            Email = 1 << 6,
            Import = 1 << 7
        }
    }

    public class WikiQueryRevisionsProperty : WikiQueryProperty
    {
        public WikiQueryRevisionsProperty(Property properties)
            : base("revisions")
        {
            _parameters.Add("rvprop", properties.ToString().Replace(" ", "").ToLower());
        }

        [Flags]
        public enum Property
        {
            None = 0,
            IDs = 1,
            Flags = 1 << 1,
            Timestamp = 1 << 2,
            User = 1 << 3,
            Size = 1 << 4,
            Comment = 1 << 5,
            Content = 1 << 6,
            Flagged = 1 << 7
        }

        [Flags]
        public enum Token
        {
            None = 0,
            Rollback = 1
        }
    }

    public class WikiQueryLangLinksProperty : WikiQueryProperty
    {
        public WikiQueryLangLinksProperty(string limit)
            : base("langlinks")
        {
            _parameters.Add("lllimit", limit);
        }
    }

    public interface IWikiGenerator
    {
        WikiGenerator GetGenerator();
    }

    public class WikiQueryImagesProperty : WikiQueryProperty, IWikiGenerator
    {
        public WikiQueryImagesProperty(string limit)
            : base("images")
        {
            _parameters.Add("imlimit", limit);
        }

        public WikiGenerator GetGenerator()
        {
            return new WikiGenerator(this);
        }
    }

    public class WikiGenerator
    {
        private readonly Dictionary<string, string> _parameters;

        public WikiGenerator(WikiQueryProperty property)
        {
            _parameters = new Dictionary<string, string>();
            _parameters.Add("generator", property.Name);
            foreach (KeyValuePair<string, string> parameter in property.Parameters)
            {
                _parameters.Add("g" + parameter.Key, parameter.Value);
            }
        }

        public IEnumerable<KeyValuePair<string, string>> Parameters
        {
            get { return _parameters; }
        }
    }

    public class WikiQuerySiteInfoMeta : WikiQueryMeta
    {
        public WikiQuerySiteInfoMeta(Property properties, bool local, bool showalldb)
            : base("siteinfo")
        {
            if (local)
            {
                _parameters.Add("sifilteriw", "local");
            }
            else
            {
                _parameters.Add("sifilteriw", "!local");
            }

            if (showalldb)
            {
                _parameters.Add("sishowalldb", "1");
            }

            _parameters.Add("siprop", properties.ToString().Replace(" ", "").ToLower());
        }

        [Flags]
        public enum Property
        {
            None = 0,
            General = 1,
            Namespaces = 1 << 1,
            Namespacealiases = 1 << 2,
            SpecialPageAliases = 1 << 3,
            MagicWords = 1 << 4,
            Statistics = 1 << 5,
            InterwikiMap = 1 << 6,
            DbReplLag = 1 << 7,
            UserGroups = 1 << 8,
            Extensions = 1 << 9,
            FileExtensions = 1 << 10,
            RightsInfo = 1 << 11
        }
    }

    public class WikiQueryUserInfoMeta : WikiQueryMeta
    {
        public WikiQueryUserInfoMeta(Property properties)
            : base("userinfo")
        {
            _parameters.Add("uiprop", properties.ToString().Replace(" ", "").ToLower());
        }

        [Flags]
        public enum Property
        {
            None = 0,
            BlockInfo = 1,
            HasMsg = 1 << 1,
            Groups = 1 << 2,
            Rights = 1 << 3,
            Options = 1 << 4,
            PreferencesToken = 1 << 5,
            EditCount = 1 << 6,
            RateLimits = 1 << 7,
            Email = 1 << 9
        }
    }

    public class WikiQueryAllMessagesMeta : WikiQueryMeta
    {
        public WikiQueryAllMessagesMeta(string messages, string filter, string lang, string from)
            : base("allmessages")
        {
            _parameters.Add("ammessages", messages);
            _parameters.Add("amfilter", filter);
            _parameters.Add("amlang", lang);
            _parameters.Add("amfrom", from);
        }
    }
}
