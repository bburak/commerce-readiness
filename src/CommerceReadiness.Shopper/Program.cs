using System.Text;
using CommerceReadiness.Shopper;

Console.OutputEncoding = Encoding.UTF8;
var (retrieval, shopper) = ShopperConfiguration.Load();
return await ShopperCli.RunAsync(args, Console.Out, Console.Error, retrieval, shopper, chat: null, TimeProvider.System);
