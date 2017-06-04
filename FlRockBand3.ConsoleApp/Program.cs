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
        static void Main(string[] args)
        {
            var inFilePath = @"C:\Shared\midi\motherskyFLTESTING5.mid";
            var inFileName = Path.GetFileNameWithoutExtension(inFilePath);
            var fileDir = new FileInfo(inFilePath).Directory.FullName;
            var outFilePath = Path.Combine(fileDir, Path.ChangeExtension(inFileName, "txt"));
            var fixedOutFilePath = Path.Combine(fileDir, Path.ChangeExtension(inFileName + "_csharp", "mid"));
            var fixedOutFilePathTxt = Path.ChangeExtension(fixedOutFilePath, "txt");

            var dumper = new Dumper();
            using (var fs = new FileStream(outFilePath, FileMode.Create))
                dumper.Dump(inFilePath, fs);

            Console.WriteLine("");

            var fixer = new Fixer();
            fixer.Fix(inFilePath, fixedOutFilePath);

            Console.WriteLine("");

            using (var fs = new FileStream(fixedOutFilePathTxt, FileMode.Create))
                dumper.Dump(fixedOutFilePath, fs);

            Console.WriteLine("Done!");
            Console.ReadKey();
        }
    }
}
