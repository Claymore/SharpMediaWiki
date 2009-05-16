using System.Collections.Generic;

namespace Claymore.SharpMediaWiki
{
    public class WikiPageSection
    {
        private string _title;
        private int _level;
        private string _text;
        private readonly List<WikiPageSection> _subSections;

        public WikiPageSection(string title, int level, string text)
        {
            _title = title;
            _level = level;
            _text = text;
            _subSections = new List<WikiPageSection>();
        }

        public int Level
        {
            get { return _level; }
        }

        public override string ToString()
        {
            return _title.Trim();
        }

        public string Title
        {
            get { return _title; }
            set { _title = value; }
        }

        public string SectionText
        {
            get { return _text; }
        }

        public IList<WikiPageSection> Subsections
        {
            get { return _subSections; }
        }

        public string Text
        {
            get
            {
                string filler = "";
                for (int i = 0; i < _level; ++i)
                {
                    filler += "=";
                }
                return string.Format("{0}{1}{0}\n{2}{3}",
                    filler,
                    _title,
                    _text,
                    string.Join("", _subSections.ConvertAll(s => s.Text).ToArray()));
            }
        }

        public void AddSubsection(WikiPageSection section)
        {
            int index = _subSections.Count - 1;
            if (index >= 0 && index < _subSections.Count &&
                _subSections[index].Level < section.Level)
            {
                _subSections[index].AddSubsection(section);
            }
            else
            {
                _subSections.Add(section);
            }
        }

        public void ForEach(Action action)
        {
            foreach (WikiPageSection section in Subsections)
            {
                action(section);
            }
        }

        public T Reduce<T>(T aggregator, ReduceAction<T> action)
        {
            T result = aggregator;
            foreach (WikiPageSection section in Subsections)
            {
                result = action(section, result);
            }
            return result;
        }

        public delegate T ReduceAction<T>(WikiPageSection section, T aggregator);
        public delegate void Action(WikiPageSection section);
    }
}
