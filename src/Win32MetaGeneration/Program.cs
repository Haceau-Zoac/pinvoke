﻿// Copyright © .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Win32.CodeGen
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using System.Threading;
    using Microsoft.CodeAnalysis.CSharp;

    internal class Program
    {
        private static void Main(string[] args)
        {
            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) =>
            {
                Console.WriteLine("Canceling...");
                cts.Cancel();
                e.Cancel = true;
            };

            Console.WriteLine("Generating code... (press Ctrl+C to cancel)");

            try
            {
                string outputDirectory = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "output");
                foreach (string file in Directory.EnumerateFiles(outputDirectory, "*", SearchOption.AllDirectories))
                {
                    File.Delete(file);
                }

                Directory.CreateDirectory(outputDirectory);

                var sw = Stopwatch.StartNew();

                var generator = new Generator(
                    new GeneratorOptions
                    {
                        WideCharOnly = true,
                        EmitSingleFile = true,
                    },
                    parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9));
                if (args.Length > 0)
                {
                    foreach (string name in args)
                    {
                        cts.Token.ThrowIfCancellationRequested();
                        if (name.EndsWith(".*"))
                        {
                            generator.TryGenerateAllExternMethods(name.Substring(0, name.Length - 2), cts.Token);
                        }
                        else
                        {
                            if (!generator.TryGenerateExternMethod(name))
                            {
                                generator.TryGenerateType(name);
                            }
                        }
                    }
                }
                else
                {
                    generator.GenerateAll(cts.Token);
                }

                var compilationUnits = generator.GetCompilationUnits(cts.Token);
                compilationUnits.AsParallel().WithCancellation(cts.Token).ForAll(unit =>
                {
                    string outputPath = Path.Combine(outputDirectory, unit.Key);
                    Console.WriteLine("Writing output file: {0}", outputPath);
                    using var generatedSourceStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                    using var generatedSourceWriter = new StreamWriter(generatedSourceStream, Encoding.UTF8);
                    unit.Value.WriteTo(generatedSourceWriter);
                });

                Console.WriteLine("Generation time: {0}", sw.Elapsed);
            }
            catch (OperationCanceledException oce) when (oce.CancellationToken == cts.Token)
            {
                Console.Error.WriteLine("Canceled.");
            }
        }
    }
}
