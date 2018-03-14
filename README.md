# Keyphrase Extraction and Document Summarization over a Custom Corpus of Content

## Overview
The purpose of this project is to show a method for extracting key terms and phrases from a large set of content.  It also leverages the resulting key phrases to allow for document summarization.  The resulting data can be used in search applications such as Azure Search to allow you to more effectively explore this unstructured data.

## Key Phrase Extraction using Full Corpus vs API's
There are numerous API's that allow you to provide the ability to both extract key phrases and generate summaries over a set of text. These API's are extremely simply, albeit that they can be costly for large data sets. The biggest issue with these API's is that they have been trained against datasets that may very well not be related to your content. For example, if you have a medical dataset, and the API was trained using words from WikiPedia, the terms that are important in your content might not be the same as what was found from WikiPedia.

Using a combination of Part of Speech (POS) tagging as well as the BM25 algorithm, we can scan an entire set of documents to identify what is defined as the most important terms. BM25, has been used for quite some time in search engines to help identify important content, so leveraging this as a method for key phrase extraction has been well proven.

## Assumptions
This project assumes that you have stored your content in Azure Blob Storage and the content is in a format supported by Apache Tika (such as PDF, Office, HTML or Text). 

## Language Support
Currently, this processor supports English, however, you can very easily add support for other languages by other POS taggers supported by Stanford NLP.

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
- Write the resulting phrases and their BM25 scores to a file called keyphrases.sqlite which will be used as the model for the next step

### Analyzing a Document to Extract Key Phrases and Create a Summary

Now that we have a model of key phrases from a corpus of content, we can use this to process any new content that we like.  An example of how to do this is provided in the project AFExtractKeyPhrasesBM25, which is an Azure Function that can be deployed for this purpose.  To do this, you will need to 

- Add the keyphrases.sqlite file created from the last step to new folder called "data" within the project
- Build and either run the project locally or deploy to Azure Function
- Open a tool such as [Postman](https://www.getpostman.com/) and execute a POST with the URL: http://localhost:7071/api/analyze (assuming you are running the project locally) with the following raw Body (where you can feel free to replace the content as needed).

The following example was using key phrases generated from a health care set of content:

```json
{
    "values": [
        {
            "recordId": "meta1",
            "data": {
                "content": "
				By Dennis Thompson - HealthDay Reporter 
				TUESDAY, March 13, 2018 (HealthDay News) -- It's well-known that the United States spends a lot more for its health care than other industrialized nations do.
				But a new study claims that some of the purported explanations for why America's health care bill is so huge simply do not wash.
				The United States does not use more health care than high-income peers like Canada, Germany, France and Japan, said study co-author Liana Woskie, assistant director of the Harvard Global Health Institute's strategic initiative on quality.
				Nor does America have too many high-paid specialists. 'At least compared to peers, we have a pretty similar mix of primary care to specialists,' Woskie added.
				Instead, it looks as though the United States pays more because it faces higher price tags for drugs, tests, office visits and administration, Woskie said.
				'We need to better understand why prices are so high and dive into that into much more detail, because some of the previous explanations may not actually be what's driving the U.S.'s spending,' she said.
				For this study, Woskie and her colleagues pulled together comprehensive data comparing U.S. health care against that of 10 other leading countries -- the United Kingdom, Canada, Germany, Australia, Japan, Sweden, France, the Netherlands, Switzerland and Denmark.
				The investigators found that the United States spends nearly twice as much of its wealth on health care -- 17.8 percent of its gross domestic product, compared with between 9.6 percent and 12.4 percent in other countries.
				That money is not buying the United States better health, however. For example, America had the lowest life expectancy and the highest infant mortality when compared to the other countries.
				The United States has about as many doctors and nurses as the other nations, and similar rates of treatment.
				But cost varied widely when it came to drugs. Pharmaceutical spending was $1,443 per person in the United States, compared to a range of $466 to $939 in other countries.
				Americans also appear to pay more for diagnostic tests and office visits, Woskie said.
				"
            }
        },
        {
            "recordId": "meta2",
            "data": {
                "content": "Date:March 13, 2018 Source:Louisiana State University Summary:One in 10 people in America is fighting a rare disease, or a disorder that affects fewer than 200,000 Americans. Researchers have developed a sophisticated and systematic way to identify existing drugs that can be repositioned to treat a rare disease or condition. Share:
					Chemotherapeutic vandetanib bound to its main target, Protein Tyrosine Kinase 6, or PTK6, in purple, which is involved in many cancers including gastrointestinal tumors and ovarian cancers. By modeling vandetanib and PTK6 complex, researchers at LSU found the KRAS protein to also contain a similar drug-binding site and therefore to be a good match for the same drug. The computer-generated model of KRAS in gold with vandetanib depicts the predicted interaction.
					Credit: Misagh Naderi, LSU.
					One in 10 people in America is fighting a rare disease, or a disorder that affects fewer than 200,000 Americans. Although there are more than 7,000 rare diseases that collectively affect more than 350 million people worldwide, it is not profitable for the pharmaceutical industry to develop new therapies to treat the small number of people suffering from each rare condition. Researchers at the LSU Computational Systems Biology group have developed a sophisticated and systematic way to identify existing drugs that can be repositioned to treat a rare disease or condition. They have fine-tuned a computer-assisted drug repositioning process that can save time and money in helping these patients receive effective treatment.
					'Rare diseases sometimes affect such a small population that discovering treatments would not be financially feasible unless through humanitarian and governmental incentives. These conditions that are sometimes left untreated are labeled 'orphan diseases.' We developed a way to computationally find matches between rare disease protein structures and functions and existing drug interactions that can help treat patients with some of these orphan diseases,' said Misagh Naderi, one of the paper's lead authors and a doctoral candidate in the LSU Department of Biological Sciences.
					This research will be published this week in the npj Systems Biology and Applications journal, published by the Nature Publishing Group in partnership the Systems Biology Institute.
					'In the past, most repurposed drugs were discovered serendipitously. For example, the drug amantadine was first introduced to treat respiratory infections. However, a few years later, a patient with Parkinson's disease experienced a dramatic improvement of her disease symptoms while taking the drug to treat the flu. This observation sparked additional research. Now, amantadine is approved by the Food Drug Administration as both an antiviral and an antiparkinsonian drug. But, we can not only rely on chance to find a treatment for an orphan disease,' said Dr. Michal Brylinski, the head of the Computational Systems Biology group at LSU.
					To systematize drug repurposing, Naderi, co-author Rajiv Gandhi Govindaraj and colleagues combined eMatchSite, a software developed by the same group with virtual screening to match FDA approved drugs and proteins that are involved in rare diseases. LSU super computers allows them to test millions of possibilities that will cost billions of dollars to test in the lab.
					This work was supported by the National Institute of General Medical Sciences of the National Institutes of Health [R35GM119524].
					 "
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
                "keyphrases": "[\"other nation\",\"came\",\"gross domestic product\",\"assistant director\",\"similar rate\",\"office visit\",\"other industrialized nation\",\"other country\",\"united state\",\"more detail\",\"many doctor\",\"peer\",\"diagnostic test\",\"strategic initiative\",\"understand\",\"health care bill\",\"dive\",\"denmark\",\"wash\",\"buying\",\"netherland\",\"wealth\",\"primary care\",\"huge\",\"switzerland\",\"said\",\"united kingdom\",\"healthday news\",\"sweden\"]",
                "summaries": "[\"\\n\\t\\t\\t\\tThe United States does not use more health care than high-income peers like Canada, Germany, France and Japan, said study co-author Liana Woskie, assistant director of the Harvard Global Health Institute's strategic initiative on quality\",\"\\n\\t\\t\\t\\tInstead, it looks as though the United States pays more because it faces higher price tags for drugs, tests, office visits and administration, Woskie said\",\"\\n\\t\\t\\t\\tThe United States has about as many doctors and nurses as the other nations, and similar rates of treatment\"]"
            },
            "errors": [],
            "warnings": []
        },
        {
            "recordId": "meta2",
            "data": {
                "keyphrases": "[\"nature publishing group\",\"biological science\",\"main target\",\"orphan disease\",\"systematic way\",\"lsu\",\"contain\",\"humanitarian\",\"dramatic improvement\",\"small population\",\"amantadine\",\"purple\",\"developed\",\"same group\",\"good match\",\"same drug\",\"few year\",\"protein tyrosine kinase\",\"doctoral candidate\",\"dollar\",\"rare condition\",\"rare disease\",\"profitable\",\"person suffering\",\"sophisticated\",\"antiviral\",\"involved\",\"ovarian cancer\",\"pharmaceutical industry\"]",
                "summaries": "[\" By modeling vandetanib and PTK6 complex, researchers at LSU found the KRAS protein to also contain a similar drug-binding site and therefore to be a good match for the same drug\",\" Researchers at the LSU Computational Systems Biology group have developed a sophisticated and systematic way to identify existing drugs that can be repositioned to treat a rare disease or condition\",\"' We developed a way to computationally find matches between rare disease protein structures and functions and existing drug interactions that can help treat patients with some of these orphan diseases,' said Misagh Naderi, one of the paper's lead authors and a doctoral candidate in the LSU Department of Biological Sciences\"]"
            },
            "errors": [],
            "warnings": []
        }
    ]
}
```
