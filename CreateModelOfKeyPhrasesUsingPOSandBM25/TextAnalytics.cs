using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpNLPTestPOSTagger
{
    public class TextAnalytics
    {
        public static List<string> RetrieveSummaries(string content, List<BM25Phrase> phrases, int SentencesToSummarize)
        {
            String[] sentences = content.Split('!', '.', '?');

            List<Match> matchList = new List<Match>();
            int counter = 0;
            // Take the 10 best words
            var topPhrases = phrases.Take(10);
            foreach (var sentence in sentences)
            {
                double count = 0;

                Match match = new Match();
                foreach (var phrase in topPhrases)
                {
                    if ((sentence.ToLower().IndexOf(phrase.phrase) > -1) &&
                        (sentence.Length > 20) && (WordCount(sentence) >= 3))
                        count += phrase.bm25;
                }

                if (count > 0)
                    matchList.Add(new Match { sentence = counter, total = count });
                counter++;
            }

            var MatchList = matchList.OrderByDescending(y => y.total).Take(SentencesToSummarize).OrderBy(x => x.sentence).ToList();
            List<string> SentenceList = new List<string>();
            string summary = string.Empty;
            for (int i = 0; i < MatchList.Count; i++)
            {
                SentenceList.Add(sentences[MatchList[i].sentence]);
            }
            // If there are no sentences found, just take the first three
            if (SentenceList.Count == 0)
            {
                for (int i = 0; i < Math.Min(SentencesToSummarize, sentences.Count()); i++)
                {
                    SentenceList.Add(sentences[0]);
                }
            }

            return SentenceList;
        }

        public static int WordCount(string text)
        {
            // Calculate total word count in text
            int wordCount = 0, index = 0;

            while (index < text.Length)
            {
                // check if current char is part of a word
                while (index < text.Length && !char.IsWhiteSpace(text[index]))
                    index++;

                wordCount++;

                // skip whitespace until next word
                while (index < text.Length && char.IsWhiteSpace(text[index]))
                    index++;
            }

            return wordCount;
        }

    }
}
