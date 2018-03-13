// This console app will use create a sqlite db (model) of key phrases based on a set of content from Azure Blob
// It leverages an algorithm called BM25 to identify the key phrases and allocate a score to each of the terms based on their
// importance in the overall corpus
// Potential phrases are identified using POS tagging

using edu.stanford.nlp.ling;
using edu.stanford.nlp.tagger.maxent;
using java.util;
using Microsoft.Azure.Search;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Entity.Design.PluralizationServices;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using TikaOnDotNet.TextExtraction;
using Console = System.Console;

namespace SharpNLPTestPOSTagger
{
    class Program
    {
        private static string TextFolder = @"d:\temp\historicsites\text\";    /*Case is important - as is the final backslash -> Fix this later...*/
        private static string sqliteFolder = @"d:\temp\historicsites\sql\";
        private static int MaxDocumentsToAnalyze = 100000;

        private static string BlobService = [Enter Blob Storage Name];
        private static string BlobKey = [Enter Blob Storage API Key];
        private static string BlobContainer = [Enter Blob Storage Container];
        private static string BlobFolder = "";  // Optional if you want to start in sub-folder of container

        private static string BlobConectionString = "DefaultEndpointsProtocol=https;AccountName=" + BlobService + ";AccountKey=" + BlobKey + ";";

        private static int MaxPhrasesInList = 1000000;
        static int MaxThreads = 16;
        static int SentencesToSummarize = 3;
        private static double MinBM25 = 5.0;
        private static string sqliteIndex = "processing.sqlite";
        private static SQLiteConnection _conn;

        private static CultureInfo ci = new CultureInfo("en-us");
        private static PluralizationService ps = PluralizationService.CreateService(ci);

        // Loading POS Tagger
        static string JarRoot = [Enter Location of stanford-postagger-2017-06-09];
        static string modelsDirectory = JarRoot + @"\models";
        //static MaxentTagger tagger = new MaxentTagger(modelsDirectory + @"\wsj-0-18-bidirectional-nodistsim.tagger"); // This is slow and often runs out of memory :-(
        static MaxentTagger tagger = new MaxentTagger(modelsDirectory + @"\english-left3words-distsim.tagger"); // This one is fast and does not use as much memory, but does a good job...

        static ConcurrentDictionary<string, DocPhrase> DocPhraseList = new ConcurrentDictionary<string, DocPhrase>();
        public static List<Task> TaskList = new List<Task>();
        private static ConcurrentDictionary<string, StreamWriter> swDictionary = new ConcurrentDictionary<string, StreamWriter>();

        private static DateTime stTime = DateTime.Now;

        static void Main(string[] args)
        {
            Console.WriteLine("{0}", "Deleting SQLite folder...\n");
            if ((Directory.Exists(sqliteFolder)))
            {
                Directory.Delete(sqliteFolder, true);
            }
            SQLIte sqlite = new SQLIte(sqliteFolder, sqliteIndex);
            Console.WriteLine("{0}", "Configuring SQLite index...\n");
            _conn = sqlite.CreateSQLiteDB();

            ServicePointManager.DefaultConnectionLimit = 10000; //(Or More)  

            GetListOfFiles();

            var randomizedFileList = GetRandomFiles();
            int fileCount = randomizedFileList.Count;

            int chunkCount = fileCount / MaxThreads;
            int randomFilesToTakeInChunk = MaxDocumentsToAnalyze / MaxThreads;

            // Split the fileList into chunks and get random values
            List<Task> TaskList = new List<Task>();

            var ListOfListOfFiles = new List<List<string>>();
            for (int i = 0; i < MaxThreads; i++)
            {
                var randomChunkFileList = randomizedFileList.Skip(i * chunkCount).Take(chunkCount);
                ListOfListOfFiles.Add(randomChunkFileList.ToList());
            }

            foreach (var fl in ListOfListOfFiles)
            {
                Task taskWrapper = Task.Run(() => DownloadFileForThread(fl));
                TaskList.Add(taskWrapper);
            }

            Task.WaitAll(TaskList.ToArray());

            System.Console.WriteLine(String.Format("Total Min: {0}", DateTime.Now.Subtract(stTime).TotalMinutes));

            // At this point all the phrases are in files that need to be bulk loaded into SQLite.
            // I did not do this in the last step as SQLite performs better by having a single connection
            var phrasefiles = System.IO.Directory.EnumerateFiles(sqliteFolder, "*.txt", System.IO.SearchOption.AllDirectories);
            foreach (var file in phrasefiles)
                sqlite.LoadPhrasesToSQLite(_conn, file);


            //WriteToSQLite();

            sqlite.CalculateBM25(_conn, MinBM25, SentencesToSummarize, TextFolder);

            Console.WriteLine(String.Format("[{0}:{1}] Processing Complete!",
                Convert.ToInt32(DateTime.Now.Subtract(stTime).TotalMinutes),
                Convert.ToInt32(DateTime.Now.Subtract(stTime).TotalSeconds) % 60));

        }

        static void GetListOfFiles()
        {
            Console.WriteLine("Getting list of all files...");
            int fileCounter = 0;
            try
            {
                var transaction = _conn.BeginTransaction();

                string sqlBM25 = "insert into filelist (file) values " +
                    "(@file)";
                var cmdInsert = new SQLiteCommand(sqlBM25, _conn);
                cmdInsert.Prepare();

                CloudStorageAccount blobStorageAccount = CloudStorageAccount.Parse(BlobConectionString);
                var blobBlobClient = blobStorageAccount.CreateCloudBlobClient();
                var blobContainer = blobBlobClient.GetContainerReference(BlobContainer);
                foreach (var file in blobContainer.ListBlobs(BlobFolder, true))
                {
                    fileCounter++;
                    if (fileCounter % 100000 == 0)
                        Console.WriteLine(String.Format("Retrieved {0} files...", fileCounter));

                    cmdInsert.Parameters.AddWithValue("file", ((CloudBlob)file).Name);
                    cmdInsert.ExecuteNonQuery();

                    //FileList.Add(((CloudBlob)file).Name);
                }
                transaction.Commit();

            }
            catch (Exception ex)
            {
                // Most likely I have exceeeded 2GB limit, so let's go with this list
                // If this happens often, switch to a store or a differe big array type
                Console.WriteLine(ex.Message);
            }
            Console.WriteLine(String.Format("Retrieved File Count: {0}", fileCounter));
        }

        static List<string> GetRandomFiles()
        {
            var RandomFileList = new List<string>();
            try
            {
                string sql= "SELECT file FROM filelist ORDER BY RANDOM() LIMIT " + MaxDocumentsToAnalyze;
                var cmd = new SQLiteCommand(sql, _conn);
                var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                {
                    RandomFileList.Add(rdr["file"].ToString());
                }

            }
            catch (Exception ex)
            {
                // Most likely I have exceeeded 2GB limit, so let's go with this list
                // If this happens often, switch to a store or a differe big array type
                Console.WriteLine(ex.Message);
            }
            Console.WriteLine(String.Format("Retrieved Random File Count: {0}", RandomFileList.Count));

            return RandomFileList;

        }

        //static void DownloadAndProcess()
        //{
        //    List<Task> TaskList = new List<Task>();
        //    var swDictionary = new ConcurrentDictionary<int, StreamWriter>();

        //    for (int currentThread = 1; currentThread <= MaxThreads; currentThread++)
        //    {
        //        int idx = currentThread;
        //        Task taskWrapper = Task.Run(() => DownloadFilesForThread(idx));
        //        TaskList.Add(taskWrapper);
        //    }

        //    Task.WaitAll(TaskList.ToArray());
        //}

        static void ProcessFromLocalDir()
        {
            var files = System.IO.Directory.EnumerateFiles(TextFolder, "*.txt", System.IO.SearchOption.AllDirectories);
            int counter = 0;
            int threadCounter = 0;

            foreach (var file in files)
            {
                counter++;
                if (counter % 1000 == 0)
                {
                    Console.WriteLine(String.Format("[{0}:{1}] Processed {2} docs...",
                        Convert.ToInt32(DateTime.Now.Subtract(stTime).TotalMinutes),
                        Convert.ToInt32(DateTime.Now.Subtract(stTime).TotalSeconds) % 60, counter));
                }
                TaskList.Add(ProcessDoc(file, threadCounter));
                if (threadCounter >= MaxThreads)
                {
                    Task.WaitAll(TaskList.ToArray());
                    threadCounter = 0;
                    if (DocPhraseList.Count > MaxPhrasesInList)
                    {
                        Console.WriteLine("Writing phrases to SQLite");
                        WriteToSQLite();
                    }
                }
                else
                {
                    threadCounter++;
                }
            }

        }

        //static async Task DownloadFilesForThread(int currentThread)
        //{
        //    System.Console.WriteLine(String.Format("Staring thread: {0}", currentThread));

        //    swDictionary.TryAdd(currentThread, new StreamWriter(Path.Combine(sqliteFolder, currentThread + ".txt"), true));

        //    CloudStorageAccount blobStorageAccount = CloudStorageAccount.Parse(BlobConectionString);

        //    var blobBlobClient = blobStorageAccount.CreateCloudBlobClient();
        //    var blobContainer = blobBlobClient.GetContainerReference(BlobContainer);

        //    var containerUrl = blobContainer.Uri.AbsoluteUri;
        //    BlobContinuationToken token = null;
        //    List<string> blobDirectories = new List<string>();
        //    List<CloudBlobDirectory> cloudBlobDirectories = new List<CloudBlobDirectory>();

        //    int counter = 0;
        //    int loopCounter = 0;    // This allows me to download files in parallel 

        //    do
        //    {
        //        var blobPrefix = BlobFolder;//We want to fetch all blobs limited to the specified folder
        //        var useFlatBlobListing = true;//This will ensure all blobs are listed.
        //        var blobsListingResult = blobContainer.ListBlobsSegmented(blobPrefix, useFlatBlobListing, BlobListingDetails.None, 5000, token, null, null);
        //        token = blobsListingResult.ContinuationToken;
        //        var blobsList = blobsListingResult.Results;

        //        foreach (var blob in blobsList)
        //        {
        //            if (loopCounter == currentThread)
        //            {
        //                counter++;
        //                try
        //                {
        //                    //fileExtension = string.Empty;
        //                    //if (blobName.LastIndexOf(".") > -1)
        //                    //    fileExtension = blobName.Substring(blobName.LastIndexOf(".")).ToLower();

        //                    if (counter%1000==0)
        //                        System.Console.WriteLine(String.Format("Total Min: {0} - Completed Processing {1} docs for Thread {2}...", 
        //                            DateTime.Now.Subtract(stTime).TotalMinutes, counter, currentThread));
        //                    CloudBlob fileBlob = blobContainer.GetBlobReference(((CloudBlob)blob).Name);

        //                    MemoryStream memoryStream = new MemoryStream();
        //                    fileBlob.DownloadToStream(memoryStream);
        //                    memoryStream.Position = 0;
        //                    StreamReader streamReader = new StreamReader(memoryStream);
        //                    String blobText = streamReader.ReadToEnd();

        //                    ProcessDoc((blob as CloudBlob).Uri.ToString(), blobText, Thread.CurrentThread.ManagedThreadId, swDictionary[currentThread]).Wait();

        //                }
        //                catch (Exception ex)
        //                {
        //                    System.Console.WriteLine("Error: " + ex.Message);
        //                }
        //            }
        //            loopCounter++;
        //            if (loopCounter > MaxThreads)
        //                loopCounter = 0;

        //        }
        //    }
        //    while (token != null);

        //    swDictionary[currentThread].Dispose();

        //    System.Console.WriteLine(String.Format("Finished thread: {0}", currentThread));


        //}

        static async Task DownloadFileForThread(List<string> fileList)
        {
            string currentThread = Guid.NewGuid().ToString();
            Console.WriteLine(String.Format("Getting Phrases for {0} files on thread {1}", fileList.Count, currentThread));
            swDictionary.TryAdd(currentThread, new StreamWriter(Path.Combine(sqliteFolder, currentThread + ".txt"), true));

            CloudStorageAccount blobStorageAccount = CloudStorageAccount.Parse(BlobConectionString);

            var blobBlobClient = blobStorageAccount.CreateCloudBlobClient();
            var blobContainer = blobBlobClient.GetContainerReference(BlobContainer);

            var containerUrl = blobContainer.Uri.AbsoluteUri;
            List<string> blobDirectories = new List<string>();
            List<CloudBlobDirectory> cloudBlobDirectories = new List<CloudBlobDirectory>();

            int counter = 0;

            foreach (var file in fileList)
            {
                counter++;
                try
                {
                    if (counter % 1000 == 0)
                        System.Console.WriteLine(String.Format("Total Min: {0} - Completed Processing {1} docs for Thread {2}...",
                            DateTime.Now.Subtract(stTime).TotalMinutes, counter, currentThread));

                    //CloudBlob fileBlob = blobContainer.GetBlobReference(file);

                    //MemoryStream memoryStream = new MemoryStream();
                    //fileBlob.DownloadToStream(memoryStream);
                    //memoryStream.Position = 0;
                    //StreamReader streamReader = new StreamReader(memoryStream);
                    //String blobText = streamReader.ReadToEnd();

                    string sasURL = GetBlobSasUri(blobContainer, file);
                    var textExtractor = new TextExtractor();

                    Uri uri = new Uri(sasURL);

                    var result = textExtractor.Extract(uri);
                    var blobText = result.Text;



                    ProcessDoc(file, blobText, Thread.CurrentThread.ManagedThreadId, swDictionary[currentThread]).Wait();

                }
                catch (Exception ex)
                {
                    System.Console.WriteLine("Error: " + ex.Message);
                }

            }

            swDictionary[currentThread].Dispose();

            System.Console.WriteLine(String.Format("Finished thread: {0}", currentThread));


        }

        static string GetBlobSasUri(CloudBlobContainer container, string blobFile)
        {
            //Get a reference to a blob within the container.
            CloudBlockBlob blob = container.GetBlockBlobReference(blobFile);

            //Set the expiry time and permissions for the blob.
            //In this case, the start time is specified as a few minutes in the past, to mitigate clock skew.
            //The shared access signature will be valid immediately.
            SharedAccessBlobPolicy sasConstraints = new SharedAccessBlobPolicy();
            sasConstraints.SharedAccessStartTime = DateTimeOffset.UtcNow.AddMinutes(-5);
            sasConstraints.SharedAccessExpiryTime = DateTimeOffset.UtcNow.AddHours(24);
            sasConstraints.Permissions = SharedAccessBlobPermissions.Read | SharedAccessBlobPermissions.Write;

            //Generate the shared access signature on the blob, setting the constraints directly on the signature.
            string sasBlobToken = blob.GetSharedAccessSignature(sasConstraints);

            //Return the URI string for the container, including the SAS token.
            return blob.Uri + sasBlobToken;
        }


        static void WriteToSQLite()
        {
            var transaction = _conn.BeginTransaction();

            string sql = "insert into docPhrases (file, phrase, count) values " +
                "(@file, @phrase, @count)";
            var cmdInsert = new SQLiteCommand(sql, _conn);
            cmdInsert.Prepare();

            foreach (var doc in DocPhraseList)
            {
                DocPhrase dp = doc.Value;
                cmdInsert.Parameters.AddWithValue("file", dp.file);
                cmdInsert.Parameters.AddWithValue("phrase", dp.phrase);
                cmdInsert.Parameters.AddWithValue("count", dp.count);
                cmdInsert.ExecuteNonQuery();
            }

            transaction.Commit();

            DocPhraseList.Clear();
        }

        public static async Task ProcessDoc(string fileName, string content, int num, StreamWriter sw)
        {
            //Console.WriteLine(String.Format("[{0}] - {1}", num, file));
            foreach (var phrase in RetrieveKeyPhrases(fileName.Substring(fileName.LastIndexOf("/") + 1).Replace(".txt", "") + ". " + content))
            {
                sw.WriteLine(fileName + "\t" + phrase.Key + "\t" + phrase.Value);
            }
        }


        public static async Task ProcessDoc(string file, int num)
        {
            //Console.WriteLine(String.Format("[{0}] - {1}", num, file));

            int counter = 0;
            foreach (var phrase in RetrieveKeyPhrases(file.Substring(file.LastIndexOf("\\") + 1).Replace(".txt", "") + ". " + System.IO.File.ReadAllText(file)))
            {
                DocPhraseList.TryAdd(counter + "||" + file, new DocPhrase { phrase = phrase.Key, count = phrase.Value, file = file });
                counter++;
            }
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
            IEnumerable<KeyValuePair<string, int>> counts = phraseList.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count()).OrderByDescending(y => y.Value);

            return counts;

        }


    }

}
