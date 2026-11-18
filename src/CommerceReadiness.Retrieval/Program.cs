using System.Text;
using CommerceReadiness.Retrieval;

Console.OutputEncoding = Encoding.UTF8;
return await RetrievalCli.RunAsync(args, Console.Out, Console.Error, RetrievalSettings.Load(), TimeProvider.System);
