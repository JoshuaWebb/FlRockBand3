using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FlRockBand3.ConsoleApp
{
    class Program
    {
        private static FileInfo PromptForPath(string prompt, string defaultInput)
        {
            prompt = $"{prompt}: ";
            Console.Write(prompt);
            string response;
            if (defaultInput != null)
            {
                Console.WriteLine(defaultInput);
                response = defaultInput;
            }
            else
            {
                response = Console.ReadLine()?.Trim('"');
            }

            while (!File.Exists(response))
            {
                Console.WriteLine($"File: {response} does not exsit");
                Console.Write(prompt);
                response = Console.ReadLine()?.Trim('"');

                if (string.IsNullOrEmpty(response))
                {
                    Console.WriteLine("Quitting");
                    Environment.Exit(1);
                }
            }

            return new FileInfo(response);
        }

        static void Main(string[] args)
        {
            string defaultInput = null;
            if (args.Length > 0 && !string.IsNullOrEmpty(args[0]))
                defaultInput = args[0];

            var midiPath = PromptForPath("Enter MIDI path", defaultInput);

            var fileDir = midiPath.Directory.FullName;
            var inFileName = Path.GetFileNameWithoutExtension(midiPath.Name);
            var outFilePath = Path.Combine(fileDir, Path.ChangeExtension(inFileName, "txt"));
            var fixedOutFilePath = Path.Combine(fileDir, Path.ChangeExtension(inFileName + "_clean", "mid"));
            var fixedOutFilePathTxt = Path.ChangeExtension(fixedOutFilePath, "txt");

            var dumper = new Dumper();
            using (var fs = new FileStream(outFilePath, FileMode.Create))
                dumper.Dump(midiPath.FullName, fs);

            Console.WriteLine("");

            var fixer = new MidiFixer();
            fixer.Fix(midiPath.FullName, fixedOutFilePath);

            Console.WriteLine("");

            using (var fs = new FileStream(fixedOutFilePathTxt, FileMode.Create))
                dumper.Dump(fixedOutFilePath, fs);

            Console.WriteLine("Done!");
            Console.ReadKey();
        }
    }
}
