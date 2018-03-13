using Microsoft.Azure.Search;
using Microsoft.Azure.Search.Models;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace SharpNLPTestPOSTagger
{
    public class SQLIte
    {
        private static string _SQLiteFolder;
        private static string _SQLiteIndex;

        public SQLIte(string SQLiteFolder, string SQLiteIndex)
        {
            _SQLiteFolder = SQLiteFolder;
            _SQLiteIndex = SQLiteIndex;
        }

        public SQLiteConnection CreateSQLiteDB()
        {
            if (Directory.Exists(_SQLiteFolder))
                Directory.Delete(_SQLiteFolder, true);
            Directory.CreateDirectory(_SQLiteFolder);

            var conn = new SQLiteConnection("Data Source=" + Path.Combine(_SQLiteFolder, _SQLiteIndex) + ";Version=3;");
            
            conn.Open();
            SQLiteCommand stmt;
            stmt = new SQLiteCommand("PRAGMA synchronous=OFF", conn);
            stmt.ExecuteNonQuery();
            stmt = new SQLiteCommand("PRAGMA count_changes=OFF", conn);
            stmt.ExecuteNonQuery();
            stmt = new SQLiteCommand("PRAGMA journal_mode=MEMORY", conn);
            stmt.ExecuteNonQuery();
            stmt = new SQLiteCommand("PRAGMA temp_store=MEMORY", conn);
            stmt.ExecuteNonQuery();

            string sql = "DROP TABLE IF EXISTS docPhrases";
            SQLiteCommand cmd = new SQLiteCommand(sql, conn);
            cmd.ExecuteNonQuery();
            sql = "create table docPhrases (file nvarchar(1024), phrase nvarchar(512), count int)";
            cmd = new SQLiteCommand(sql, conn);
            cmd.ExecuteNonQuery();
            sql = "create index idx_docPhrases_file on docPhrases(file)";
            cmd = new SQLiteCommand(sql, conn);
            cmd.ExecuteNonQuery();
            sql = "create index idx_docPhrases_phrase on docPhrases(phrase)";
            cmd = new SQLiteCommand(sql, conn);
            cmd.ExecuteNonQuery();

            sql = "DROP TABLE IF EXISTS filelist";
            cmd = new SQLiteCommand(sql, conn);
            cmd.ExecuteNonQuery();
            sql = "create table filelist (file nvarchar(1024))";
            cmd = new SQLiteCommand(sql, conn);
            cmd.ExecuteNonQuery();

            return conn;

        }


        public void LoadPhrasesToSQLite(SQLiteConnection conn, string PhraseFile)
        {
            Console.WriteLine(String.Format("Processing {0}...", PhraseFile));

            using (StreamReader file = new StreamReader(PhraseFile))
            {
                string line;
                string[] fields;

                var transaction = conn.BeginTransaction();

                string sqlBM25 = "insert into docPhrases (file, phrase, count) values " +
                    "(@file, @phrase, @count)";
                var cmdInsert = new SQLiteCommand(sqlBM25, conn);
                cmdInsert.Prepare();

                while ((line = file.ReadLine()) != null)
                {
                    fields = line.Split('\t');
                    if (fields.Count() == 3)
                    {
                        cmdInsert.Parameters.AddWithValue("file", fields[0]);
                        cmdInsert.Parameters.AddWithValue("phrase", fields[1]);
                        cmdInsert.Parameters.AddWithValue("count", fields[2]);
                        cmdInsert.ExecuteNonQuery();
                    }
                }
                transaction.Commit();
            }
        }



        public void CalculateBM25(SQLiteConnection conn, double MinBM25, int SentencesToSummarize, string TextFolder)
        {
            int TotalDocCount = 0;
            int AvgWordCount = 0;
            double k = 1.2;
            double b = 0.75;

            // Create the bm25 sqlite to store results
            Console.WriteLine("Creating SQLite db to store key phrases...");
            var bm25conn = new SQLiteConnection("Data Source=" + Path.Combine(_SQLiteFolder, "keyphrases.sqlite") + ";Version=3;");
            bm25conn.Open();
            SQLiteCommand stmt;
            stmt = new SQLiteCommand("PRAGMA synchronous=OFF", conn);
            stmt.ExecuteNonQuery();
            stmt = new SQLiteCommand("PRAGMA count_changes=OFF", conn);
            stmt.ExecuteNonQuery();
            stmt = new SQLiteCommand("PRAGMA journal_mode=MEMORY", conn);
            stmt.ExecuteNonQuery();
            stmt = new SQLiteCommand("PRAGMA temp_store=MEMORY", conn);
            stmt.ExecuteNonQuery();
            string sql = "DROP TABLE IF EXISTS keyphrases";
            SQLiteCommand cmdBM25 = new SQLiteCommand(sql, bm25conn);
            cmdBM25.ExecuteNonQuery();
            sql = "create table keyphrases (file nvarchar(1024), phrase nvarchar(512), bm25 double)";
            cmdBM25 = new SQLiteCommand(sql, bm25conn);
            cmdBM25.ExecuteNonQuery();

            Console.WriteLine("Creating table to store phrase counts...");
            sql = "DROP TABLE IF EXISTS phraseCounts";
            SQLiteCommand cmd = new SQLiteCommand(sql, conn);
            cmd.ExecuteNonQuery();
            sql = "create table phraseCounts (phrase nvarchar(512), count int)";
            cmd = new SQLiteCommand(sql, conn);
            cmd.ExecuteNonQuery();


            Console.WriteLine("Creating table to store avg bm25 for phrases...");
            sql = "DROP TABLE IF EXISTS bm25";
            cmd = new SQLiteCommand(sql, bm25conn);
            cmd.ExecuteNonQuery();
            sql = "create table bm25 (phrase nvarchar(256), bm25 float)";
            cmd = new SQLiteCommand(sql, bm25conn);
            cmd.ExecuteNonQuery();

            Console.WriteLine("Creating table to store document phrase counts...");
            sql = "DROP TABLE IF EXISTS docPhraseCount";
            cmd = new SQLiteCommand(sql, conn);
            cmd.ExecuteNonQuery();
            sql = "create table docPhraseCount (file nvarchar(1024), count int)";
            cmd = new SQLiteCommand(sql, conn);
            cmd.ExecuteNonQuery();

            Console.WriteLine("Inserting document phrase counts...");
            sql = "insert into docPhraseCount select file, sum(count) count from docPhrases group by file";
            cmd = new SQLiteCommand(sql, conn);
            cmd.ExecuteNonQuery();

            Console.WriteLine("Inserting phrase counts...");
            sql = "insert into phraseCounts select phrase, sum(count) count from docPhrases group by phrase";
            cmd = new SQLiteCommand(sql, conn);
            cmd.ExecuteNonQuery();

            Console.WriteLine("Deleting phrases that only occur once ...");
            sql = "delete from docPhrases where phrase in (select phrase from phraseCounts where count = 1)";
            cmd = new SQLiteCommand(sql, conn);
            cmd.ExecuteNonQuery();
            sql = "delete from phraseCounts where count = 1";
            cmd = new SQLiteCommand(sql, conn);
            cmd.ExecuteNonQuery();

            Console.WriteLine("Getting total document count...");
            sql = "with fileCount as (select file from docPhrases group by file) select count(*) from fileCount";
            cmd = new SQLiteCommand(sql, conn);
            var rdr = cmd.ExecuteReader();
            while (rdr.Read())
                TotalDocCount = Convert.ToInt32(rdr[0]);
            Console.WriteLine(String.Format("Total documentcount: {0}", TotalDocCount));

            Console.WriteLine("Getting average word count...");
            sql = "with fileCount as (select file, sum(count)count from docPhrases group by file) select avg(count) from fileCount";
            cmd = new SQLiteCommand(sql, conn);
            rdr = cmd.ExecuteReader();
            while (rdr.Read())
                AvgWordCount = Convert.ToInt32(rdr[0]);
            Console.WriteLine(String.Format("Average word count: {0}", AvgWordCount));


            // Iterate through each unique phrase in each doc
            Console.WriteLine("Calculate BM25 and store results...");
            sql = "select dp.file, dp.phrase, dp.count termFreqInDocument, pc.count termFreqInIndex, " +
                "dpc.count docWordCount from " +
                "docPhrases dp, phraseCounts pc, docPhraseCount dpc " +
                "where dp.phrase = pc.phrase and dpc.file = dp.file " +
                "order by dp.file";
            cmd = new SQLiteCommand(sql, conn);
            rdr = cmd.ExecuteReader();
            string lastFile = string.Empty;

            var transaction = bm25conn.BeginTransaction();

            string sqlBM25 = "insert into keyphrases (file, phrase, bm25) values " +
                "(@file, @phrase, @bm25)";
            var cmdInsert = new SQLiteCommand(sqlBM25, bm25conn);
            cmdInsert.Prepare();
            double bm25;
            int counter = 0;
            int fileCounter = 0;

            var KeyPhraseList = new List<BM25Phrase>();
            var IndexBatchList = new List<KeyPhraseIndex>();

            while (rdr.Read())
            {
                bm25 = Math.Log(Convert.ToDouble((TotalDocCount - Convert.ToDouble(rdr["termFreqInIndex"]) + 0.5) /
                    (Convert.ToDouble(rdr["termFreqInIndex"]) + 0.5))) * (Convert.ToDouble(rdr["termFreqInDocument"]) * (k + 1)) / 
                    (Convert.ToDouble(rdr["termFreqInDocument"]) + k * (1 - b + (b * Convert.ToDouble(rdr["docWordCount"]) / AvgWordCount)));


                if (bm25 >= MinBM25)
                {
                    counter++;

                    KeyPhraseList.Add(new BM25Phrase { phrase = rdr["phrase"].ToString(), bm25 = bm25 });

                    cmdInsert.Parameters.AddWithValue("file", rdr["file"].ToString());
                    cmdInsert.Parameters.AddWithValue("phrase", rdr["phrase"].ToString());
                    cmdInsert.Parameters.AddWithValue("bm25", bm25);
                    cmdInsert.ExecuteNonQuery();

                    if (counter % 1000000 == 0)
                    {
                        Console.WriteLine(String.Format("Wrote {0} phrases, {1} files...", counter, fileCounter));
                        transaction.Commit();
                        transaction = bm25conn.BeginTransaction();
                        cmdInsert = new SQLiteCommand(sqlBM25, bm25conn);
                        cmdInsert.Prepare();
                    }
                }
            }

            Console.WriteLine(String.Format("Wrote {0} phrases...", counter));
            transaction.Commit();

            sql = "insert into bm25 select phrase, avg(bm25) from keyphrases group by phrase having avg(bm25) >= " + MinBM25;
            cmdInsert = new SQLiteCommand(sql, bm25conn);
            cmdInsert.ExecuteNonQuery();
            Console.WriteLine(String.Format("Wrote {0} avg bm25 phrases...", counter));

            Console.WriteLine(String.Format("Creating index on bm25 phrases...", counter));
            sql = "create index idx_bm25_phrase on bm25(phrase)";
            cmd = new SQLiteCommand(sql, bm25conn);
            cmd.ExecuteNonQuery();

            Console.WriteLine(String.Format("Dropping key phrases table...", counter));
            sql = "drop table keyphrases";
            cmd = new SQLiteCommand(sql, bm25conn);
            cmd.ExecuteNonQuery();

            Console.WriteLine(String.Format("Shrinking DB...", counter));
            sql = "vacuum";
            cmd = new SQLiteCommand(sql, bm25conn);
            cmd.ExecuteNonQuery();


            //var batchFinal = IndexBatch.Upload(IndexBatchList);
            //indexClient.Documents.Index(batchFinal);

        }

        public static string Base64Encode(string plainText)
        {
            var plainTextBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
            return System.Convert.ToBase64String(plainTextBytes);
        }


        public static string Base64Decode(string base64EncodedData)
        {
            var base64EncodedBytes = System.Convert.FromBase64String(base64EncodedData);
            return System.Text.Encoding.UTF8.GetString(base64EncodedBytes);
        }

    }
}
