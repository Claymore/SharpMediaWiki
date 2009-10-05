using System.Collections.Generic;

namespace Claymore.SharpMediaWiki
{
    public class WikiPageSection
    {
        private string _text;
        private readonly List<WikiPageSection> _subSections;
        private string _rawTitle;
        private string _title;

        public string Title
        {
            get
            {
                return _title;
            }
            set
            {
                string filler = "";
                for (int i = 0; i < Level; ++i)
                {
                    filler += "=";
                }
                _title = value;
                _rawTitle = filler + Title + filler;
            }
        }

        public int Level { get; private set; }

        public WikiPageSection(string title, int level, string text)
        {
            Level = level;
            _text = text;
            Title = title;
            _subSections = new List<WikiPageSection>();
        }

        public WikiPageSection(string title, string rawTitle, int level, string text)
        {
            _rawTitle = rawTitle;
            _title = title;
            Level = level;
            _text = text;
            _subSections = new List<WikiPageSection>();
        }

        public string SectionText
        {
            get { return _text; }
            set { _text = value; }
        }

        public IList<WikiPageSection> Subsections
        {
            get { return _subSections; }
        }

        public string Text
        {
            get
            {
                return string.Format("{0}\n{1}{2}",
                    _rawTitle,
                    _text,
                    string.Concat(_subSections.ConvertAll(s => s.Text).ToArray()));
            }
        }

        public override string ToString()
        {
            return Title.Trim();
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
