using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

/* TODO
 * 1. Performance pass to clean up slowness (exceptions, noise) 
 * 2. Give warnings where can make code smaller (DP, or jsr to bsr, lb to b, etc)
 * 3. Mark unused labels and functions - gives space to use later?
 * 4. Export listing with addresses - eases debugging
 * 5. Add 6800 support for the sound rom
 * 6. Use color on output for clarity
 */

// ..\..\..\robotronCL.asm -o robotron2084.rom -s ..\..\output -r ..\..\..\..\..\roms -t 4 -i -c
// ..\..\..\..\DisasmSound\robotronSoundCL.asm -o robotronSND.rom -s ..\..\output -r ..\..\..\..\..\roms -t 4 -i -c

namespace Lomont.ClAsmTool
{
    class Program
    {
        class Options
        {
            /// <summary>
            /// Where source file lives, must be non-null
            /// </summary>
            public string SourceName{get; set; }
            /// <summary>
            /// If present, where to get roms to compare to. 
            /// </summary>
            public string RomPath{get; set; }
            /// <summary>
            /// If present, where to write total output file
            /// </summary>
            public string OutputName{get; set; }
            /// <summary>
            /// If present, where to split rom output to
            /// </summary>
            public string SplitPath{get; set; }
            /// <summary>
            /// If > 0, test to run
            /// </summary>
            public int TestNum { get; set; } = -1;
            /// <summary>
            /// If true, run in loop
            /// </summary>
            public bool Interactive{get; set; }

            /// <summary>
            /// Show robotron checksums, used to make code pass internal uses
            /// </summary>
            public bool ShowChecksums { get; set; }
        }
        static void ShowHelp(TextWriter output)
        {
            Console.WriteLine("asm filename      : assembles with messages");
            Console.WriteLine("    -o filename   : gives total rom as output");
            Console.WriteLine("    -r rompath    : validates against roms in this path");
            Console.WriteLine("    -s split path : splits into this path");
            Console.WriteLine("    -c            : display ROM checksums ");
            Console.WriteLine("    -t num        : runs test 1-4");
            Console.WriteLine("    -i            : interactive testing in loop");
            Console.WriteLine("       (q to quit loop, ? for help)");
        }

        // options - todo - make on console
        //var filename = @"C:\Users\Chris\OneDrive\Robotron\DisasmYellow\robotronY.asm";
        //var romPath = @"C:\Users\Chris\OneDrive\Robotron\Code\ClAsmTool\ClAsmTool\roms";
        //var outputPath = @"C:\Users\Chris\OneDrive\Robotron\Code\ClAsmTool\ClAsmTool\output";

        /*
         * my debug command line
            ..\..\..\..\..\DisasmYellow\robotronY.asm -o robotron2084.rom -s ..\..\output -r ..\..\..\..\..\roms -t 4 -i
         */


        static Options ParseOptions(string [] args)
        {
            var i = 0;
            var opt = new Options();
            while (i < args.Length)
            {
                var a = args[i];
                if (a == "-o")
                {
                    opt.OutputName = args[i + 1];
                    i += 2;
                }
                else if (a == "-r")
                {
                    opt.RomPath = args[i + 1];
                    i += 2;
                }
                else if (a == "-s")
                {
                    opt.SplitPath = args[i + 1];
                    i += 2;
                }
                else if (a == "-t")
                {
                    if (Int32.TryParse(args[i + 1], out var test))
                        opt.TestNum = test;
                    else
                        Console.Error.WriteLine($"Cannot parse test number {args[i+1]}");
                    i += 2;
                }
                else if (a == "-c")
                {
                    opt.ShowChecksums = true;
                    i += 1;
                }
                else if (a == "-i")
                {
                    opt.Interactive = true;
                    i += 1;
                }
                else if (!a.StartsWith("-"))
                {
                    opt.SourceName = a;
                    i++;
                }
                else
                {
                    Console.Error.WriteLine($"Could not parse option {args[i]}");
                    return null;
                }
            }
            return opt;
        }

        static void Main(string[] args)
        {
            Console.WriteLine("Chris Lomont's 6809 assembler for his Robotron 2084 reverse engineering project, v0.1.");

            var options = ParseOptions(args);
            if (String.IsNullOrEmpty(options.SourceName))
            {
                Console.Error.WriteLine("Must have source filename");
                ShowHelp(Console.Out);
                return;
            }
            if (!File.Exists(options.SourceName))
            {
                Console.Error.WriteLine($"File {options.SourceName} does not exist");
                    return;
            }


            var runInteractive = options.Interactive;

            var debugLength = 0xDE00;

            //options.TestNum = 2;
            do
            {
                var asm = new Assembler();
                if (asm.Assemble(options.SourceName, Console.Out))
                {
                    //var rd = new RomDiff();
                    //rd.DiffRoms(asm.State, options.RomPath);

                    if (options.ShowChecksums)
                        Validator.ShowChecksums(asm.State);

                    Validator.CheckDifferences(
                        options.RomPath,
                        asm.State,
                        debugLength,
                        options.TestNum,
                        20);
                    if (!String.IsNullOrEmpty(options.OutputName))
                    {
                        File.WriteAllBytes(options.OutputName, asm.State.RomImage);
                        Console.WriteLine($"ROM file {options.OutputName} written");
                    }
                    if (!String.IsNullOrEmpty(options.SplitPath))
                        Splitter.Split(asm.State, options.SplitPath);
                }

                if (runInteractive)
                {
                    while (!Console.KeyAvailable)
                    {
                    }
                    var c = Console.ReadKey(true).KeyChar;
                    if (c == 'q' || c == 'Q')
                        runInteractive = false;
                    else if (c == '+')
                        debugLength += 256;
                    else if (c == '-' && debugLength > 256)
                        debugLength += 256;
                    else if ('1' <= c && c <= '9')
                        options.TestNum = c-'1'+1;
                    else if (c == 'd')
                    {
                        Console.Write("Enter address: ");
                        var txt = Console.ReadLine();
                        var address = Convert.ToUInt32(txt, 16);
                        debugLength = (int)address;
                    }
                    else if (c == '?')
                    {
                        Console.WriteLine("Test values: ");
                        foreach (var msg in Validator.Descriptions)
                            Console.WriteLine(msg);
                        Console.WriteLine();
                    }
                    else
                    {// todo help
                        //ShowHelp(Console.Error);
                        Console.WriteLine("press 'q' to quit");
                    }
                }

            } while (runInteractive);

            Console.WriteLine("Done...");
        }
    }
}
