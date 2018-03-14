# Keyphrase Extraction and Document Summarization over a Custom Corpus of Content

## Overview
The purpose of this project is to show a method for extracting key terms and phrases from a large set of content.  It also leverages the resulting key phrases to allow for document summarization.  The resulting data can be used in search applications such as Azure Search to allow you to more effectively explore this unstructured data.

## Key Phrase Extraction using Full Corpus vs API's
There are numerous API's that allow you to provide the ability to both extract key phrases and generate summaries over a set of text. These API's are extremely simply, albeit that they can be costly for large data sets. The biggest issue with these API's is that they have been trained against datasets that may very well not be related to your content. For example, if you have a medical dataset, and the API was trained using words from WikiPedia, the terms that are important in your content might not be the same as what was found from WikiPedia.

Using a combination of Part of Speech (POS) tagging as well as the BM25 algorithm, we can scan an entire set of documents to identify what is defined as the most important terms. BM25, has been used for quite some time in search engines to help identify important content, so leveraging this as a method for key phrase extraction has been well proven.

## Assumptions
This project assumes that you have stored your content in Azure Blob Storage and the content is in a format supported by Apache Tika (such as PDF, Office, HTML or Text). 

## Language Support
Currently, this processor supports English, however, you can cery easily add support for other languages by other POS taggers supported by Stanford NLP.

## Performance and Scale
This processing is done purely on a single machine. It has done really well for 100's of thousands of files and even millions of smaller documents (1-2KB), but if your content is larger, you will want to look at how to parallelize this using something like Spark or Azure Data Lake Analytics.

## Getting Started
### Extracting Key Phrases

The first step will be to extract a "model" of key phrases.  To do this, you will leverage the "CreateModelOfKeyPhrasesUsingPOSandBM25" project included within this solution.  Before running, you will need to modify [program.cs](https://github.com/liamca/keyphrase_extraction_and_summarization_over_custom_content/blob/master/CreateModelOfKeyPhrasesUsingPOSandBM25/Program.cs) and update the following parameters:

        private static string BlobService = [Enter Blob Storage Name];
        private static string BlobKey = [Enter Blob Storage API Key];
        private static string BlobContainer = [Enter Blob Storage Container];
        ...
        // Loading POS Tagger
        static string JarRoot = [Enter Location of stanford-postagger-2017-06-09];

NOTE: If you do not have a copy of the Stanford POS Tagger, you can download it [here](https://nlp.stanford.edu/software/tagger.shtml).

Once this is complete, you can run the project, which will complete the following tasks:

- Determine a set of phrases across all the documents which are stored in a SQLite database
- Using this set of phrases calculate their BM25 values according to their importance in the overall corpus of content
- Write the resuting phrases and their BM25 scores to a file called keyphrases.sqlite which will be used as the model for the next step

### Analyzing a Document to Extract Key Phrases and Create a Summary

Now that we have a model of key phrases from a corpus of content, we can use this to process any new content that we like.  An example of how to do this is provided in the project AFExtractKeyPhrasesBM25, which is an Azure Function that can be deployed for this purpose.  To do this, you will need to 

- Add the keyphrases.sqlite file created from the last step to new folder called "data" within the project
- Build and either run the project locally or deploy to Azure Function
- Open a tool such as [Postman](https://www.getpostman.com/) and execute a POST with the URL: http://localhost:7071/api/analyze (assuming you are running the project locally) with the following raw Body (where you can feel free to replace the content as needed):

```json
{
    "values": [
        {
            "recordId": "meta1",
            "data": {
                "name": "name1",
                "content": "Rockhaven Sanitarium Historic District, Glendale, California one of the best extant examples of an early twentieth century woman-owned, women-serving private sanitarium in the State. It was one of the first of its type in the nation. It reflects the vision of Agnes Richards, R.N., and represents a small but significant movement that sought to improve the conditions of mentally ill women in the early"
            }
        },
        {
            "recordId": "meta2",
            "data": {
                "name": "name2",
                "content": "The National Register (NR) of Historic Places program has initiated a project to update/revise the National Register Bulletin: How to Complete the National Register Registration Form.* This NR publication provides guidance for identifying, evaluating, and documenting properties that are historically significant in American history, architecture, engineering, landscape design, archeology, and culture at the local, state, "
            }
        }
    ]
}
```

If everything runs successfully (and depending on the key phrases you had previously created), the results will look something like this:
```json
{
    "values": [
        {
            "recordId": "meta1",
            "data": {
                "keyphrases": "[\"best extant example\",\"glendale\"]",
                "summaries": "[\"Rockhaven Sanitarium Historic District, Glendale, California one of the best extant examples of an early twentieth century woman-owned, women-serving private sanitarium in the State\"]"
            },
            "errors": [],
            "warnings": []
        },
        {
            "recordId": "meta2",
            "data": {
                "keyphrases": "[\"national register registration form\"]",
                "summaries": "[\"The National Register (NR) of Historic Places program has initiated a project to update/revise the National Register Bulletin: How to Complete the National Register Registration Form\"]"
            },
            "errors": [],
            "warnings": []
        }
    ]
}
```
