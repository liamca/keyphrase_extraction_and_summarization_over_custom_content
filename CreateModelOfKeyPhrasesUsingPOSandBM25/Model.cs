using Microsoft.Azure.Search;
using Microsoft.Azure.Search.Models;
using Microsoft.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpNLPTestPOSTagger
{
    [SerializePropertyNamesAsCamelCase]
    public partial class KeyPhraseIndex
    {
        [System.ComponentModel.DataAnnotations.Key]
        [IsFilterable]
        public string id { get; set; }

        [IsSearchable]
        public string filename { get; set; }

        [IsSearchable, IsFilterable, IsFacetable]
        public string filetype { get; set; }

        [IsSearchable]
        public string content { get; set; }

        [IsSearchable]
        public string[] summary { get; set; }

        [IsSearchable, IsFilterable, IsFacetable]
        public string[] terms { get; set; }

        [IsFilterable, IsFacetable]
        public DateTime creationDateTime { get; set; }

    }

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
}
