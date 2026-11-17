using System.Text;
using CommerceReadiness.Analyzer;

Console.OutputEncoding = Encoding.UTF8;
return AnalyzerCli.Run(args, Console.Out, Console.Error);
