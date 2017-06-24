using System;
using System.IO;

namespace FlRockBand3.ConsoleApp
{
    public class Program
    {
        public const int HResultFileAlreadyExists = -2147024816; // 0x80070050

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

            if (File.Exists(fixedOutFilePath))
            {
                if (!PromptConfirm($"'{fixedOutFilePath}' already exists, overwrite it?"))
                {
                    Console.WriteLine("Aborted");
                    Console.ReadKey();
                    return;
                }
            }

            TryDumpFile(outFilePath, midiPath.FullName);

            var fixer = new MidiFixer();
            fixer.AddMessage += (sender, handlerArgs) => Console.WriteLine($"{handlerArgs.Type}: {handlerArgs.Message}");;

            try
            {
                fixer.Fix(midiPath.FullName, fixedOutFilePath);
            }
            catch (Exception e)
            {
                Console.WriteLine("Failed to clean midi: " + e.Message);
                Console.Write("Done");
                Console.ReadKey();
                Environment.Exit(1);
            }

            Console.WriteLine("");

            TryDumpFile(fixedOutFilePathTxt, fixedOutFilePath);

            Console.WriteLine("Done!");
            Console.ReadKey();
        }

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
                Console.WriteLine($"File: '{response}' does not exsit");
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

        private static bool PromptConfirm(string prompt)
        {
            prompt = $"{prompt} (y/n): ";
            Console.Write(prompt);

            var response = Console.ReadLine();

            bool boolResponse;
            while (!TryParse(response, out boolResponse))
            {
                Console.Write(prompt);
                response = Console.ReadLine();
            }

            return boolResponse;
        }

        private static bool TryParse(string input, out bool result)
        {
            if ("y".Equals(input, StringComparison.OrdinalIgnoreCase) ||
                "true".Equals(input, StringComparison.OrdinalIgnoreCase))
            {
                result = true;
                return true;
            }

            if ("n".Equals(input, StringComparison.OrdinalIgnoreCase) ||
                "false".Equals(input, StringComparison.OrdinalIgnoreCase))
            {
                result = false;
                return true;
            }

            result = false;
            return false;
        }

        private static void TryDumpFile(string outFilePath, string midiPath)
        {
            try
            {
                DumpFile(outFilePath, midiPath);
                Console.WriteLine("");
            }
            catch (IOException ioe)
            {
                Console.WriteLine("Could not write diagnostic file: " + ioe.Message);
            }
        }

        private static void DumpFile(string outFilePath, string midiPath)
        {
            var dumper = new Dumper();
            try
            {
                using (var fs = new FileStream(outFilePath, FileMode.CreateNew))
                    dumper.Dump(midiPath, fs);

                return;
            }
            catch (IOException ioe) when (ioe.HResult == HResultFileAlreadyExists)
            {
                if (!PromptConfirm($"Diagnostic file '{outFilePath}' already exists, overwrite?"))
                {
                    Console.WriteLine("Not overwriting file");
                    return;
                }
            }

            using (var fs = new FileStream(outFilePath, FileMode.Create))
                dumper.Dump(midiPath, fs);
        }
    }
}
