using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Twitter2D
{
    static class Bootstrap
    {
        public static async Task Main()
        {
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("████████╗██╗    ██╗██╗████████╗████████╗███████╗██████╗ ██████╗ ██████╗ ");
            Console.WriteLine("╚══██╔══╝██║    ██║██║╚══██╔══╝╚══██╔══╝██╔════╝██╔══██╗╚════██╗██╔══██╗");
            Console.WriteLine("   ██║   ██║ █╗ ██║██║   ██║      ██║   █████╗  ██████╔╝ █████╔╝██║  ██║");
            Console.WriteLine("   ██║   ██║███╗██║██║   ██║      ██║   ██╔══╝  ██╔══██╗██╔═══╝ ██║  ██║");
            Console.WriteLine("   ██║   ╚███╔███╔╝██║   ██║      ██║   ███████╗██║  ██║███████╗██████╔╝");
            Console.WriteLine("   ╚═╝    ╚══╝╚══╝ ╚═╝   ╚═╝      ╚═╝   ╚══════╝╚═╝  ╚═╝╚══════╝╚═════╝ ");
            Console.ForegroundColor = ConsoleColor.White;
            var version = typeof(Bootstrap).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            Log.WriteInfo($"Version: {version} - Runtime: {RuntimeInformation.FrameworkDescription}");
            var expander = new Program();
            await expander.Initialize();
            await expander.StartTwitterStream();
        }
    }
}
