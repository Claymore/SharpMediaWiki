using System.Collections.Generic;

namespace Claymore.SharpMediaWiki
{
    public class WikiPageSection
    {
        private int _level;
        private string _text;
        private readonly List<WikiPageSection> _subSections;

        public string Title { get; set; }

        public WikiPageSection(string title, int level, string text)
        {
            Title = title;
            _level = level;
            _text = text;
            _subSections = new List<WikiPageSection>();
        }

        public int Level
        {
            get { return _level; }
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
                    Title,
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
