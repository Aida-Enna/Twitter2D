using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace TwitterStreaming
{
    static class Bootstrap
    {
        public static async Task Main()
        {
            var version = typeof(Bootstrap).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            Log.WriteInfo($"Version: {version} - Runtime: {RuntimeInformation.FrameworkDescription}");
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("████████╗██╗    ██╗██╗████████╗████████╗███████╗██████╗ ██████╗ ██████╗ ");
            Console.WriteLine("╚══██╔══╝██║    ██║██║╚══██╔══╝╚══██╔══╝██╔════╝██╔══██╗╚════██╗██╔══██╗");
            Console.WriteLine("   ██║   ██║ █╗ ██║██║   ██║      ██║   █████╗  ██████╔╝ █████╔╝██║  ██║");
            Console.WriteLine("   ██║   ██║███╗██║██║   ██║      ██║   ██╔══╝  ██╔══██╗██╔═══╝ ██║  ██║");
            Console.WriteLine("   ██║   ╚███╔███╔╝██║   ██║      ██║   ███████╗██║  ██║███████╗██████╔╝");
            Console.WriteLine("   ╚═╝    ╚══╝╚══╝ ╚═╝   ╚═╝      ╚═╝   ╚══════╝╚═╝  ╚═╝╚══════╝╚═════╝ ");
            Console.ForegroundColor = ConsoleColor.White;
            var expander = new Program();
            await expander.Initialize();
            await expander.StartTwitterStream();
        }
    }
}
