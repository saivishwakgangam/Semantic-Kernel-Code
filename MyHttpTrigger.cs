using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using static System.Net.WebRequestMethods;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.CoreSkills;
using UglyToad.PdfPig;
using System.Net;
using System.Collections.Generic;
using Microsoft.SemanticKernel.Text;
using UglyToad.PdfPig.Content;
using Microsoft.SemanticKernel.Orchestration;
using Microsoft.SemanticKernel.SkillDefinition;

namespace MyFunctionProj
{
    public static class MyHttpTrigger
    {

        public static string openaiEndPoint;
        public static string primaryKey;
        public static IKernel kernel;
        public static List<string> paras;
        public static string MemoryCollectionName;
        public static string myPrompt;
        public static SKContext kernelContext;
        public static string history = "";
        public static ISKFunction askBot;

        static MyHttpTrigger()
        {
            // initialising of end point and primary key
            openaiEndPoint = "https://ashishtest.openai.azure.com/";
            primaryKey = "4b2788972ebb45c29bce687ace26029e";
            kernel = new KernelBuilder()
            .Configure(c =>
            {
                c.AddAzureTextEmbeddingGenerationService(
                    "ada",
                    openaiEndPoint,
                    primaryKey);
                c.AddAzureChatCompletionService(
                    "ashishfhlmay",
                    openaiEndPoint,
                    primaryKey);
            })
            .WithMemoryStorage(new VolatileMemoryStore())
            .Build();
            kernel.ImportSkill(new TextMemorySkill());
            kernel.ImportSkill(new ConversationSummarySkill(kernel));
            MemoryCollectionName = "aboutGraphConnectors";
            string microsoftSearchText = "";
            using (PdfDocument document = PdfDocument.Open(@"C:\Users\saigangam\OneDrive - Microsoft\Desktop\microsoftsearch.pdf"))
            {
                foreach (Page page in document.GetPages())
                {
                    string pageText = page.Text;
                    microsoftSearchText += pageText;
                }
            }

            List<string> lines = TextChunker.SplitMarkDownLines(microsoftSearchText, 1024);
            paras = TextChunker.SplitMarkdownParagraphs(lines, 1024);
            myPrompt = @"
                You are a ChatBot(Name : Alice) and you can have a conversation Microsoft about Graph Connectors.
You can give explicit instructions or say 'I don't know' if you don't have an answer
Wherever you find image content or table content, extract data out of it.
Do not repeat yourself and..
Use CONTEXT to learn about Microsoft graph connectors.
If you have doubts among multiple answer then clarify it with the user first before answering the question.
If the user tries to confuse you, stick to your point and do not get confused.
Dont tell any information apart from Microsoft Graph Connectors.
If CONTEXT contains markdown formatting, you will sanitize it.
USE INFO WHEN TO THE POINT. Give PROFESSIONAL information only.

[CONTEXT]
- {{recall $userInput}}
[END CONTEXT]

Chat:
{{$history}}
User: {{$userInput}}
ChatBot: ";

            //Initialising the context
            kernelContext = kernel.CreateNewContext();
            kernelContext["fact1"] = "What is Microsoft Graph Connectors";
            kernelContext["fact2"] = "What all connectors does microsoft graph connector supports";
            kernelContext["fact3"] = "What is Azure DevOps Graph Connector?";
            kernelContext["fact4"] = "how to set up a Microsoft Graph Connector?";
            kernelContext["fact5"] = "What is Microsoft Graph Connector agent";
            kernelContext["fact6"] = "How to monitor your connections?";

        }

        [FunctionName("saveInformation")]
        public static async Task<IActionResult> saveInformation(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("Saving Data to kernel memory started");
            for(int i = 0; i < paras.Count; i++)
            {
                await kernel.Memory.SaveInformationAsync(
                    $"{MemoryCollectionName}",
                    text: $"{paras[i]}",
                    id: $"info_{i}").ConfigureAwait(false);
            }
            log.LogInformation("Saving Data to kernel memory completed");
            log.LogInformation("Updating Context Started");
            kernelContext[TextMemorySkill.CollectionParam] = MemoryCollectionName;
            kernelContext[TextMemorySkill.RelevanceParam] = "0.0";
            kernelContext[TextMemorySkill.LimitParam] = "2";
            kernelContext["history"] = history;
            askBot = kernel.CreateSemanticFunction(myPrompt, maxTokens : 256, temperature : 0);
            string responseMessage = "Saving Data to kernel memory completed a updating ";
            return new OkObjectResult(responseMessage);
            
        }




        [FunctionName("MyHttpTrigger")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation($"ChatBot Serving UserInput Started");

            string name = req.Query["input"];

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            name = name ?? data?.name;
            log.LogInformation($"here is what user asked {name}");
            kernelContext["userInput"] = name;
            if (askBot == null)
            {
                log.LogInformation($"Failed to run {name}");
                return new OkObjectResult("Failed to run");
            }
            var answer = await askBot.InvokeAsync(kernelContext).ConfigureAwait(false);
            // Append the new interaction to the chat history
            history += $"\nUser: {name}\nChatBot: {answer}\n";
            kernelContext["history"] = history;
            log.LogInformation($"ChatBot Serving UserInput Completed");
            string responseMessage = answer?.ToString();
            ResponseModel obj = new ResponseModel();
            obj.role = "bot";
            obj.message = responseMessage;
            string serialObj = JsonConvert.SerializeObject(obj);
            return new OkObjectResult(serialObj);
        }
    }
}
