using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Claymore.SharpMediaWiki
{
    public class WikiTemplate : WikiElement
    {
        public string Name { get; private set; }
        private readonly List<WikiElement> _parameters;

        public WikiTemplate(string name)
        {
            Name = name;
            _parameters = new List<WikiElement>();
        }

        #region WikiElement Members

        public string Text
        {
            get
            {
                return "{{" + Name + (_parameters.Count != 0 ? "|" : "") +
                    string.Join("|", _parameters.Select(p => p.Text).ToArray()) + "}}";
            }
            set
            {
                throw new NotImplementedException();
            }
        }

        #endregion
    }

    public class WikiString : WikiElement
    {
        private string _text;

        public WikiString(string text)
        {
            _text = text;
        }

        #region WikiElement Members

        public string Text
        {
            get
            {
                return _text;
            }
            set
            {
                _text = value;
            }
        }

        #endregion
    }

    public interface WikiElement
    {
        string Text { get; set; }
    }
}
