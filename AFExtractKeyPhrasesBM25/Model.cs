using System.Collections.Generic;

namespace AFExtractKeyPhrasesAndSummariesBM25
{
    public class DocPhrase
    {
        public string file { get; set; }
        public string phrase { get; set; }
        public int count { get; set; }
    }

    public class Phrase
    {
        public string phrase { get; set; }
        public double count { get; set; }
    }

    public class BM25Phrase
    {
        public string phrase { get; set; }
        public double bm25 { get; set; }
    }

    public class Match
    {
        public int sentence { get; set; }
        public double total { get; set; }
    }

    public class WebApiSkillRequest
    {
        public List<WebApiRequestRecord> values { get; set; } = new List<WebApiRequestRecord>();
    }

    public class WebApiSkillResponse
    {
        public List<WebApiResponseRecord> values { get; set; } = new List<WebApiResponseRecord>();
    }

    public class WebApiRequestRecord
    {
        public string recordId { get; set; }
        public Dictionary<string, object> data { get; set; } = new Dictionary<string, object>();
    }

    public class WebApiResponseRecord
    {
        public string recordId { get; set; }
        public Dictionary<string, object> data { get; set; } = new Dictionary<string, object>();
        public List<string> errors { get; set; } = new List<string>();
        public List<string> warnings { get; set; } = new List<string>();
    }

}
