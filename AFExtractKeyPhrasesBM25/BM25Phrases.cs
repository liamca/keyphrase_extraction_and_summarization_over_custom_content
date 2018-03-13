using edu.stanford.nlp.ling;
using edu.stanford.nlp.tagger.maxent;
using java.util;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data.Entity.Design.PluralizationServices;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace AFExtractKeyPhrasesAndSummariesBM25
{
    // This function will use a sqlite db (model) created by CreateModelOfKeyPhrasesUsingPOSandBM25 to 
    // identify key phrases from a set of user provided text

    // ****************************************************************************************************************************
    // IMPORTANT: Update KeyPhraseDB to point to SQLite db created using CreateModelOfKeyPhrasesUsingPOSandBM25 
    // ****************************************************************************************************************************

    public static class BM25Phrases
    {
        // Limit the # of key phrases
        static int MaxKeyPhrases = 30;
        static int SentencesToSummarize = 3;

#if DEBUG
        private static string RootDir = Directory.GetCurrentDirectory();
#else
        private static string RootDir = @"D:\home\site\wwwroot";
#endif

        private static string KeyPhraseDB = System.IO.Path.Combine(RootDir, @"data\news-2017.sqlite");

        //System.IO.Directory.GetCurrentDirectory() 

        // Loading POS Tagger
        static string taggerDirectory = System.IO.Path.Combine(RootDir, @"models\english-left3words-distsim.tagger");
        static MaxentTagger tagger = new MaxentTagger(taggerDirectory); 

        private static CultureInfo ci = new CultureInfo("en-us");
        private static PluralizationService ps = PluralizationService.CreateService(ci);

        [FunctionName("BM25Phrases")]
        public static async Task<HttpResponseMessage> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "analyze")]HttpRequestMessage req, TraceWriter log)
        {
            log.Info("BM25 HTTP trigger function processed a request...");

            var jsonRequest = await req.Content.ReadAsStringAsync();
            var docs = JsonConvert.DeserializeObject<WebApiSkillRequest>(jsonRequest);

            WebApiSkillResponse response = new WebApiSkillResponse();

            using (var conn = new SQLiteConnection("Data Source=" + KeyPhraseDB + ";Version=3;"))
            {
                conn.Open();

                foreach (var inRecord in docs.values)
                {
                    var outRecord = new WebApiResponseRecord() { recordId = inRecord.recordId };

                    string name = inRecord.data["name"] as string;
                    log.Info($"Processing Search Document:{name}");

                    try
                    {
                        log.Info($"Processing Document...");

                        // Get all the potential phrases using POS Tagger
                        log.Info("Extracting Potential Phrases...");
                        log.Info("===============================");
                        var potentialPhrases = RetrieveKeyPhrases((string)inRecord.data["content"]);

                        //// Log potential phrases
                        //foreach (var phrase in potentialPhrases)
                        //    log.Info(phrase.Key + ": " + phrase.Value);

                        log.Info("Extracting BM25 Phrases...");
                        log.Info("==========================");

                        string commaList = "'" + potentialPhrases.Select(x => x.Key).Aggregate((x, y) => x + "','" + y) + "'";
                        var phraseList = new List<BM25Phrase>();
                        string sql = "select phrase, bm25 from bm25 where phrase in (" + commaList + ") order by bm25 desc";
                        SQLiteCommand cmd = new SQLiteCommand(sql, conn);
                        cmd.Parameters.AddWithValue("phrase", commaList);
                        var rdr = cmd.ExecuteReader();
                        while (rdr.Read())
                        {
                            //log.Info(rdr["phrase"].ToString() + ": " + rdr["bm25"].ToString());
                            phraseList.Add(new BM25Phrase { phrase = rdr["phrase"].ToString(), bm25 = Convert.ToDouble(rdr["bm25"]) });
                        }
                        rdr.Close();

                        outRecord.data["keyphrases"] = JsonConvert.SerializeObject(phraseList.Select(x=>x.phrase).Take(MaxKeyPhrases));

                        // Extract summary of content
                        var summaries = RetrieveSummaries((string)inRecord.data["content"], phraseList.Take(20).ToList());
                        outRecord.data["summaries"] = JsonConvert.SerializeObject(summaries);

                    }
                    catch (Exception e)
                    {
                        log.Error(e.ToString());
                        outRecord.errors.Add("Error processing the Document: " + e.ToString());
                    }
                    response.values.Add(outRecord);
                }
            }

            return req.CreateResponse(HttpStatusCode.OK, response);
        }

        static IEnumerable<KeyValuePair<string, int>> RetrieveKeyPhrases(string text)
        {
            var sentences = MaxentTagger.tokenizeText(new java.io.StringReader(text)).toArray();
            var Phrases = new List<Phrase>();
            string phrase = string.Empty;
            int nounCount = 0;
            var phraseList = new List<string>();
            StringLabel t;
            string tStr, tType;

            foreach (ArrayList sentence in sentences)
            {
                var taggedSentence = tagger.tagSentence(sentence);
                foreach (var term in taggedSentence.toArray())
                {
                    t = (edu.stanford.nlp.ling.StringLabel)term;
                    tStr = t.ToString();
                    tType = tStr.Substring(tStr.LastIndexOf("/") + 1);
                    tStr = tStr.Substring(0, tStr.LastIndexOf("/"));

                    if ((tType.IndexOf("NN") > -1) && (tStr.All(char.IsLetterOrDigit)) && (tStr.Length > 2))
                    {
                        phrase += " " + ps.Singularize(tStr);
                        nounCount += 1;
                    }
                    else if (((tType.IndexOf("JJ") > -1) || (tType.IndexOf("VB") > -1)) && (tStr.All(char.IsLetterOrDigit)) && (tStr.Length > 2))
                    {
                        phrase += " " + ps.Singularize(tStr);
                    }
                    else if (phrase != "")
                    {
                        phraseList.Add(phrase.ToLower().Trim());
                        phrase = "";
                    }
                }
            }
            return phraseList.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count()).OrderByDescending(y => y.Value);
        }

        public static List<string> RetrieveSummaries(string content, List<BM25Phrase> phrases)
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
